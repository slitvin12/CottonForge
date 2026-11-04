using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.AddressableAssets;
using BF = Il2CppSystem.Reflection.BindingFlags;
using FieldInfo = Il2CppSystem.Reflection.FieldInfo;
using IlType = Il2CppSystem.Type;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Makes generated largos behave like vanilla ones beyond their look (config <c>LargoParity</c>, with SmartLargos):
    /// <list type="bullet">
    /// <item>Prefab references: components of the template prefab that still point at the template definition / appearance
    /// (Cotton fixes five of them) are repointed to the largo.</item>
    /// <item>Components merged from the parents (Cotton's <c>MergeComponents</c>): references into the parent's prefab
    /// (its children, its other components) are remapped to the same object on the largo prefab, references to the parent
    /// definition / appearance go to the largo; base-class fields are copied too; injected (mod) types are skipped.</item>
    /// <item>Groups: every group that holds every vanilla largo, and the groups that hold every vanilla largo of a parent
    /// (learned by <see cref="VanillaSlimes"/>).</item>
    /// <item>No template full art (the pedia showed the Pink+X largo), zones and favourite production as vanilla largos
    /// have them (learned), an own icon (<see cref="SlimeIcons"/>).</item>
    /// <item>Plorts of modded parents join the groups every vanilla largoable plort is in (EdiblePlortFoodGroup ...): the other
    /// parent eats them and becomes the largo (Cotton never added them, nobody ate Rad plorts).</item>
    /// </list>
    /// Every step is separate and logged; a failing step changes nothing else. <c>largoaudit</c> checks the result.
    /// </summary>
    public static class LargoParity
    {
        /// <summary>Config <c>LargoParity</c> (default on).</summary>
        public static bool Enabled => ForgeConfig.LargoParity == null || ForgeConfig.LargoParity.Value;

        /// <summary>Group names never joined automatically.</summary>
        public static readonly HashSet<string> SkipGroups = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Parent components never merged (Cotton's list).</summary>
        public static readonly HashSet<string> SkipComponents = new(StringComparer.Ordinal) { "AweTowardsLargos", "SlimeEyeComponents", "SlimeMouthComponents", "ColliderTotemLinkerHelper" };

        /// <summary>What parity did for one largo.</summary>
        public sealed class Result
        {
            public int Repointed, Remapped, Unmapped, Components, Groups, Dropped, Animations, Dynamic, Plorts;
            public bool FullArt, Zones;
            public readonly List<string> Added = new(), Notes = new();
            public override string ToString() =>
                $"{Components} components merged ({Remapped} refs remapped, {Unmapped} left in parent prefab), {Repointed} refs repointed, {Groups} groups" +
                (Dropped > 0 ? $", {Dropped} template leftovers removed" : "") + (Animations > 0 ? $", {Animations} animation overrides from parents" : "") +
                (Dynamic > 0 ? $", dynamic appearances fixed ({Dynamic})" : "") + (Plorts > 0 ? $", plorts joined {Plorts} groups" : "") +
                (FullArt ? ", template full art cleared" : "") + (Zones ? ", zones of parents" : "") + (Notes.Count > 0 ? "; " + string.Join("; ", Notes) : "");
        }

        static readonly Dictionary<string, Result> _results = new(StringComparer.OrdinalIgnoreCase);
        public static Result ResultOf(SlimeDefinition largo) => largo != null && _results.TryGetValue(largo.name, out var r) ? r : null;
        static Result For(SlimeDefinition largo)
        {
            if (!_results.TryGetValue(largo.name, out var r)) _results[largo.name] = r = new Result();
            return r;
        }

        static bool _auditQueued;

        // ------------------------------------------------------------------ IL2CPP reflection

        static IlType _identT, _appT, _goT, _compT;
        static void Types()
        {
            if (_identT != null) return;
            _identT = Il2CppType.Of<IdentifiableType>();
            _appT = Il2CppType.Of<SlimeAppearance>();
            _goT = Il2CppType.Of<GameObject>();
            _compT = Il2CppType.Of<Component>();
        }

        static readonly Dictionary<string, List<FieldInfo>> _fields = new();
        static readonly Dictionary<string, List<FieldInfo>> _refFields = new();

        static string Key(IlType t) => t.AssemblyQualifiedName ?? t.FullName ?? t.Name;

        /// <summary>Instance fields of a component type and its game base classes (stops at UnityEngine / System types).</summary>
        internal static List<FieldInfo> FieldsOf(IlType t)
        {
            var key = Key(t);
            if (_fields.TryGetValue(key, out var list)) return list;
            list = new List<FieldInfo>();
            for (var x = t; x != null; x = x.BaseType)
            {
                var fn = x.FullName ?? "";
                if (fn.StartsWith("UnityEngine.", StringComparison.Ordinal) || fn.StartsWith("System.", StringComparison.Ordinal)) break;
                foreach (var f in x.GetFields(BF.Instance | BF.Public | BF.NonPublic | BF.DeclaredOnly))
                    if (f != null && !f.IsStatic && !f.IsLiteral) list.Add(f);
            }
            _fields[key] = list;
            return list;
        }

        /// <summary>Fields typed as a definition (IdentifiableType and subclasses) or a SlimeAppearance.</summary>
        internal static List<FieldInfo> RefFields(IlType t)
        {
            Types();
            var key = Key(t);
            if (_refFields.TryGetValue(key, out var list)) return list;
            list = FieldsOf(t).Where(f => { var ft = f.FieldType; return ft != null && (IsRefType(ft) || IsRefType(ElementOf(ft))); }).ToList();
            _refFields[key] = list;
            return list;
        }

        internal static bool IsSceneRef(FieldInfo f)
        {
            Types();
            var ft = f.FieldType;
            return ft != null && (_goT.IsAssignableFrom(ft) || _compT.IsAssignableFrom(ft));
        }

        static bool IsGameType(IlType t)
        {
            try
            {
                var n = t.Assembly != null ? t.Assembly.FullName ?? "" : "";
                return n.IndexOf("Injected", StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch { return true; }
        }

        internal static string PathIn(Transform t, Transform root)
        {
            if (t == null || root == null) return null;
            var parts = new List<string>();
            for (var x = t; x != null; x = x.parent)
            {
                if (x.Pointer == root.Pointer) { parts.Reverse(); return string.Join("/", parts); }
                parts.Add(x.name);
            }
            return null;
        }

        static Transform TransformOf(Il2CppSystem.Object v, out GameObject go, out Component comp)
        {
            go = v.TryCast<GameObject>();
            comp = go == null ? v.TryCast<Component>() : null;
            return go != null ? go.transform : comp != null ? comp.transform : null;
        }

        /// <summary>The object on <paramref name="dstRoot"/> that matches <paramref name="v"/> (inside <paramref name="srcRoot"/>): same path, same component type and index.</summary>
        static Il2CppSystem.Object Remap(Il2CppSystem.Object v, Transform srcRoot, Transform dstRoot, out bool inside)
        {
            inside = false;
            var t = TransformOf(v, out var go, out var comp);
            var path = PathIn(t, srcRoot);
            if (path == null) return null;
            inside = true;
            var dt = path.Length == 0 ? dstRoot : dstRoot.Find(path);
            if (dt == null) return null;
            if (go != null) return dt.gameObject;
            var type = comp.GetIl2CppType();
            var src = t.gameObject.GetComponents(type);
            var dst = dt.gameObject.GetComponents(type);
            if (dst == null || dst.Length == 0) return null;
            int idx = 0;
            if (src != null) for (int i = 0; i < src.Length; i++) if (src[i] != null && src[i].Pointer == comp.Pointer) { idx = i; break; }
            return dst[Math.Min(idx, dst.Length - 1)];
        }

        // ------------------------------------------------------------------ arrays and lists

        /// <summary>Element type of an array or <c>List&lt;T&gt;</c> field type (reference elements only), else null.</summary>
        internal static IlType ElementOf(IlType ft)
        {
            if (ft == null || ft.IsValueType) return null;
            try
            {
                IlType et = null;
                if (ft.IsArray) et = ft.GetElementType();
                else if (ft.IsGenericType && (ft.Name ?? "").StartsWith("List`", StringComparison.Ordinal))
                {
                    var ga = ft.GetGenericArguments();
                    if (ga != null && ga.Length == 1) et = ga[0];
                }
                return et != null && !et.IsValueType ? et : null;
            }
            catch { return null; }
        }

        /// <summary>A definition (IdentifiableType and subclasses) or a SlimeAppearance type.</summary>
        internal static bool IsRefType(IlType t) { Types(); return t != null && (_identT.IsAssignableFrom(t) || _appT.IsAssignableFrom(t)); }
        /// <summary>A GameObject or Component type.</summary>
        internal static bool IsSceneType(IlType t) { Types(); return t != null && (_goT.IsAssignableFrom(t) || _compT.IsAssignableFrom(t)); }

        /// <summary>Read / write access to an IL2CPP object array or <c>List&lt;T&gt;</c> held in an <c>Il2CppSystem.Object</c>.</summary>
        internal sealed class Seq
        {
            public readonly Il2CppSystem.Object Value;
            readonly IlType _type;
            readonly Il2CppReferenceArray<Il2CppSystem.Object> _arr;
            readonly Il2CppSystem.Reflection.MethodInfo _get, _set, _add;
            public readonly int Count;

            Seq(Il2CppSystem.Object v, IlType t, bool array)
            {
                Value = v; _type = t;
                if (array) { _arr = new Il2CppReferenceArray<Il2CppSystem.Object>(v.Pointer); Count = _arr.Length; return; }
                _get = t.GetMethod("get_Item"); _set = t.GetMethod("set_Item"); _add = t.GetMethod("Add");
                var c = t.GetMethod("get_Count").Invoke(v, null);
                Count = c != null ? c.Unbox<int>() : 0;
            }

            /// <summary>Null when <paramref name="v"/> is not an object array or a List&lt;T&gt;.</summary>
            public static Seq Of(Il2CppSystem.Object v)
            {
                if (v == null) return null;
                try
                {
                    var t = v.GetIl2CppType();
                    if (t == null) return null;
                    if (t.IsArray) { var et = t.GetElementType(); return et != null && !et.IsValueType ? new Seq(v, t, true) : null; }
                    if (t.IsGenericType && (t.Name ?? "").StartsWith("List`", StringComparison.Ordinal)) return new Seq(v, t, false);
                }
                catch { }
                return null;
            }

            static Il2CppReferenceArray<Il2CppSystem.Object> Args(params Il2CppSystem.Object[] a) => new(a);
            static unsafe Il2CppSystem.Object Box(int i) => new Il2CppSystem.Object(IL2CPP.il2cpp_value_box(Il2CppClassPointerStore<int>.NativeClassPtr, (IntPtr)(&i)));

            public Il2CppSystem.Object this[int i]
            {
                get => _arr != null ? _arr[i] : _get.Invoke(Value, Args(Box(i)));
                set { if (_arr != null) _arr[i] = value; else _set.Invoke(Value, Args(Box(i), value)); }
            }

            /// <summary>A new collection of the same type with the same elements.</summary>
            public Seq Clone()
            {
                if (_arr != null) return new Seq(Value.TryCast<Il2CppSystem.Array>().Clone(), _type, true);
                var l = Il2CppSystem.Activator.CreateInstance(_type);
                for (int i = 0; i < Count; i++) _add.Invoke(l, Args(this[i]));
                return new Seq(l, _type, false);
            }
        }

        /// <summary>
        /// A copy of collection <paramref name="v"/> (component of a parent prefab) whose elements are repointed (<paramref name="map"/>)
        /// or remapped onto the largo prefab; null when nothing changes (the value is copied as is).
        /// </summary>
        static Il2CppSystem.Object RemapSeq(Il2CppSystem.Object v, Transform srcRoot, Transform dstRoot, Dictionary<IntPtr, Il2CppSystem.Object> map, Result r, string what)
        {
            var s = Seq.Of(v);
            if (s == null || s.Count == 0) return null;
            var repl = new Dictionary<int, Il2CppSystem.Object>();
            int unmapped = 0, repointed = 0;
            for (int i = 0; i < s.Count; i++)
            {
                var e = s[i];
                if (e == null) continue;
                if (map != null && map.TryGetValue(e.Pointer, out var to)) { repl[i] = to; repointed++; continue; }
                var m = Remap(e, srcRoot, dstRoot, out bool inside);
                if (m != null) repl[i] = m;
                else if (inside) unmapped++;
            }
            if (unmapped > 0) { r.Unmapped += unmapped; r.Notes.Add($"{what}: {unmapped} element(s) still point into the parent prefab"); }
            if (repl.Count == 0) return null;
            var c = s.Clone();
            foreach (var kv in repl) c[kv.Key] = kv.Value;
            r.Repointed += repointed; r.Remapped += repl.Count - repointed;
            return c.Value;
        }

        static SlimeAppearance Default(SlimeDefinition d) => VanillaSlimes.App(d);

        // ------------------------------------------------------------------ references

        /// <summary>
        /// Points every definition / appearance reference in <paramref name="root"/> (all components, children included) that
        /// is a key of <paramref name="map"/> to its value. Returns the number of changed fields.
        /// </summary>
        public static int Repoint(GameObject root, Dictionary<IntPtr, Il2CppSystem.Object> map)
        {
            if (root == null || map == null || map.Count == 0) return 0;
            int n = 0;
            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                List<FieldInfo> fields;
                try { fields = RefFields(comp.GetIl2CppType()); } catch { continue; }
                foreach (var f in fields)
                {
                    Il2CppSystem.Object v;
                    try { v = f.GetValue(comp); } catch { continue; }
                    if (v == null) continue;
                    if (ElementOf(f.FieldType) != null)
                    {
                        // the prefab is a copy: its arrays / lists are its own, change the elements in place
                        Seq s = null;
                        try { s = Seq.Of(v); } catch { }
                        if (s == null) continue;
                        for (int i = 0; i < s.Count; i++)
                            try { var e = s[i]; if (e != null && map.TryGetValue(e.Pointer, out var te)) { s[i] = te; n++; } } catch { break; }
                        continue;
                    }
                    if (!map.TryGetValue(v.Pointer, out var to)) continue;
                    try { f.SetValue(comp, to); n++; } catch { }
                }
            }
            return n;
        }

        /// <summary>Repoints a prefab copied from <paramref name="from"/> to <paramref name="to"/> (definition and appearance); used by <see cref="SlimeBuilder"/>.</summary>
        public static int Repoint(GameObject prefab, SlimeDefinition from, SlimeDefinition to, SlimeAppearance fromApp = null, SlimeAppearance toApp = null)
        {
            if (!Enabled || prefab == null || from == null || to == null) return 0;
            var map = new Dictionary<IntPtr, Il2CppSystem.Object> { [from.Pointer] = to };
            if (fromApp != null && toApp != null) map[fromApp.Pointer] = toApp;
            return Safe.Run("repoint " + prefab.name, () => Repoint(prefab, map), 0);
        }

        static Dictionary<IntPtr, Il2CppSystem.Object> TemplateMap(SlimeDefinition template, SlimeDefinition largo)
        {
            var map = new Dictionary<IntPtr, Il2CppSystem.Object>();
            if (template == null || largo == null) return map;
            map[template.Pointer] = largo;
            var own = Default(largo);
            var ta = template.AppearancesDefault;
            if (ta != null && own != null)
                for (int i = 0; i < ta.Length; i++)
                {
                    var t = ta[i];
                    if (t == null) continue;
                    SlimeAppearance to = null;
                    if (t._appearType == SlimeAppearance.AppearanceType.RADIANT_LARGO_0) to = largo.RadiantLargo0;
                    else if (t._appearType == SlimeAppearance.AppearanceType.RADIANT_LARGO_1) to = largo.RadiantLargo1;
                    map[t.Pointer] = to ?? own;
                }
            return map;
        }

        // ------------------------------------------------------------------ components

        /// <summary>
        /// Replacement of Cotton's <c>MergeComponents</c> for largos: adds the parents' MonoBehaviours the largo prefab lacks
        /// (same skip list), then copies their fields with references remapped onto the largo prefab.
        /// </summary>
        public static void MergeComponents(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two)
        {
            if (largo == null || largo.prefab == null || one == null || one.prefab == null) return;
            if (!Enabled) { Library.MergeComponents(largo.prefab, one.prefab, two != null ? two.prefab : null); return; }
            var r = For(largo);
            try { MergeCore(largo, one, two, r); }
            catch (Exception e)
            {
                r.Notes.Add("component merge failed: " + e.Message);
                ForgeLog.WarnOnce("parcomp:" + largo.name, $"Component merge of {largo.name} failed ({e.GetType().Name}: {e.Message}), using Cotton's");
                Safe.Run("MergeComponents " + largo.name, () => Library.MergeComponents(largo.prefab, one.prefab, two != null ? two.prefab : null));
            }
        }

        static void MergeCore(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two, Result r)
        {
            Types();
            var obj = largo.prefab;
            var have = new HashSet<string>(VanillaSlimes.RootComponents(obj));
            var jobs = new List<(Component dst, MonoBehaviour src, SlimeDefinition from)>();
            foreach (var parent in new[] { one, two })
            {
                var pf = parent != null ? parent.prefab : null;
                if (pf == null) continue;
                foreach (var c in pf.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    var t = c.GetIl2CppType();
                    var n = t.Name ?? "";
                    if (have.Contains(n) || SkipComponents.Any(s => n.Contains(s))) continue;
                    have.Add(n);
                    if (!IsGameType(t)) { r.Notes.Add("skipped injected " + n); continue; }
                    if (obj.TryAddComponentTypeIfNull(t, out var comp) && comp != null) jobs.Add((comp, c, parent));
                }
            }
            var app = Default(largo);
            foreach (var (dst, src, from) in jobs)
            {
                var map = new Dictionary<IntPtr, Il2CppSystem.Object> { [from.Pointer] = largo };
                var fa = Default(from);
                if (fa != null && app != null) map[fa.Pointer] = app;
                Copy(dst, src, from.prefab.transform, obj.transform, map, r);
                r.Components++;
                r.Added.Add(src.GetIl2CppType().Name + " (" + from.name + ")");
            }
        }

        static void Copy(Component dst, Component src, Transform srcRoot, Transform dstRoot, Dictionary<IntPtr, Il2CppSystem.Object> map, Result r)
        {
            foreach (var f in FieldsOf(src.GetIl2CppType()))
            {
                Il2CppSystem.Object v;
                try { v = f.GetValue(src); } catch { continue; }
                if (v != null && f.FieldType != null && !f.FieldType.IsValueType)
                {
                    if (map.TryGetValue(v.Pointer, out var to)) { v = to; r.Repointed++; }
                    else if (IsSceneRef(f))
                    {
                        var m = Safe.Run("remap " + f.Name, () => Remap(v, srcRoot, dstRoot, out _), null);
                        bool inside = m == null && PathIn(TransformOf(v, out _, out _), srcRoot) != null;
                        if (m != null) { v = m; r.Remapped++; }
                        else if (inside) { r.Unmapped++; r.Notes.Add($"{src.GetIl2CppType().Name}.{f.Name} still points into the parent prefab"); }
                    }
                    else
                    {
                        var et = ElementOf(f.FieldType);
                        if (et != null && (IsSceneType(et) || IsRefType(et)))
                        {
                            var what = src.GetIl2CppType().Name + "." + f.Name;
                            var nv = Safe.Run("remap list " + what, () => RemapSeq(v, srcRoot, dstRoot, map, r, what), null);
                            if (nv != null) v = nv;
                        }
                    }
                }
                try { f.SetValue(dst, v); } catch { }
            }
        }

        // ------------------------------------------------------------------ groups

        static bool Skip(IdentifiableTypeGroup g) => g == null || SkipGroups.Contains(g.name ?? "");

        /// <summary>Joins the groups of every vanilla largo and the groups of every vanilla largo of each parent.</summary>
        public static int JoinLargoGroups(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two)
        {
            if (largo == null) return 0;
            VanillaSlimes.Learn();
            int n = 0;
            foreach (var g in VanillaSlimes.LargoGroups) if (!Skip(g) && Idents.AddToGroup(g, largo)) n++;
            foreach (var p in new[] { one, two })
                if (p != null)
                    foreach (var g in VanillaSlimes.ParentGroups(p.name))
                        if (!Skip(g) && Idents.AddToGroup(g, largo)) n++;
            return n;
        }

        /// <summary>Joins the groups of every vanilla base slime (<see cref="SlimeBuilder"/> slimes).</summary>
        public static int JoinBaseGroups(SlimeDefinition slime)
        {
            if (!Enabled || slime == null || slime.IsLargo) return 0;
            VanillaSlimes.Learn();
            int n = 0;
            var names = new List<string>();
            foreach (var g in VanillaSlimes.BaseGroups)
            {
                if (Skip(g) || (!slime.CanLargofy && VanillaSlimes.IsLargoableOnly(g))) continue;
                if (Idents.AddToGroup(g, slime)) { n++; names.Add(g.name); }
            }
            if (n > 0) ForgeLog.Debug($"[parity] {slime.name} joined {string.Join(", ", names)}");
            return n;
        }

        // ------------------------------------------------------------------ definition

        static bool ClearFullArt(SlimeDefinition largo)
        {
            largo._fullArt = new AssetReferenceT<Sprite>("");
            largo._requiresFullArt = false;
            var apps = largo.AppearancesDefault;
            if (apps != null) for (int i = 0; i < apps.Length; i++) if (apps[i] != null) apps[i]._fullArt = new AssetReferenceT<Sprite>("");
            return true;
        }

        static Il2CppReferenceArray<T> Union<T>(Il2CppReferenceArray<T> a, Il2CppReferenceArray<T> b) where T : Il2CppObjectBase
        {
            var list = new List<T>();
            var seen = new HashSet<IntPtr>();
            foreach (var arr in new[] { a, b })
                if (arr != null) for (int i = 0; i < arr.Length; i++) if (arr[i] != null && seen.Add(arr[i].Pointer)) list.Add(arr[i]);
            return Gc.Array(list);
        }

        static bool Zones(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two)
        {
            bool any = false;
            if (VanillaSlimes.ZonesUnion >= 0.8f)
            {
                var u = Union(one.NativeZones, two.NativeZones);
                if (!VanillaSlimes.Set(u).SetEquals(VanillaSlimes.Set(largo.NativeZones))) { largo.NativeZones = u; any = true; }
            }
            if (VanillaSlimes.ShowZonesUnion >= 0.8f)
            {
                var u = Union(one.showForZones, two.showForZones);
                if (!VanillaSlimes.Set(u).SetEquals(VanillaSlimes.Set(largo.showForZones))) { largo.showForZones = u; any = true; }
            }
            return any;
        }

        static void Diet(SlimeDefinition largo, SlimeDefinition template, Result r)
        {
            var d = largo.Diet;
            if (d == null) return;
            var t = template != null ? template.Diet : null;
            bool refresh = false;
            int fav = VanillaSlimes.FavoriteProduction;
            if (fav > 0 && d.FavoriteProductionCount != fav) { r.Notes.Add($"favourite production {d.FavoriteProductionCount} -> {fav}"); d.FavoriteProductionCount = fav; refresh = true; }
            if (t != null)
            {
                if (d.BecomesOnTarrifyIdentifiableType == null && t.BecomesOnTarrifyIdentifiableType != null) { d.BecomesOnTarrifyIdentifiableType = t.BecomesOnTarrifyIdentifiableType; r.Notes.Add("tarr type set"); refresh = true; }
                if (d.EdiblePlortIdentifiableTypeGroup == null && t.EdiblePlortIdentifiableTypeGroup != null) { d.EdiblePlortIdentifiableTypeGroup = t.EdiblePlortIdentifiableTypeGroup; refresh = true; }
                if (d.StableResourceIdentifiableTypeGroup == null) d.StableResourceIdentifiableTypeGroup = t.StableResourceIdentifiableTypeGroup;
                if (d.UnstableResourceIdentifiableTypeGroup == null) d.UnstableResourceIdentifiableTypeGroup = t.UnstableResourceIdentifiableTypeGroup;
                if (d.UnstablePlort == null) d.UnstablePlort = t.UnstablePlort;
            }
            if (refresh) largo.RefreshEatmap();
        }

        /// <summary>
        /// Plorts of modded parents join the groups every vanilla largoable plort is in and the group the other parent's diet eats
        /// plorts from; the modded parent joins the base groups. Both parents' eat maps are refreshed when anything changed
        /// (CreateCompleteLargo refreshes them itself, after RefreshIndexes).
        /// </summary>
        public static int Plorts(SlimeDefinition one, SlimeDefinition two)
        {
            if (one == null || two == null) return 0;
            VanillaSlimes.Learn();
            int n = 0;
            foreach (var (p, o) in new[] { (one, two), (two, one) })
            {
                if (VanillaSlimes.IsVanilla(p)) continue;
                var prod = p.Diet != null ? p.Diet.ProduceIdents : null;
                if (prod == null) continue;
                var groups = new List<IdentifiableTypeGroup>(VanillaSlimes.PlortGroups);
                var og = o.Diet != null ? o.Diet.EdiblePlortIdentifiableTypeGroup : null;
                if (og != null && !groups.Any(g => Same(g, og))) groups.Add(og);
                for (int i = 0; i < prod.Length; i++)
                {
                    var x = prod[i];
                    if (x == null) continue;
                    foreach (var g in groups)
                        if (!Skip(g) && !Idents.InGroup(g, x) && Idents.AddToGroup(g, x)) { n++; ForgeLog.Debug($"[parity] plort {x.name} joined {g.name}"); }
                }
                JoinBaseGroups(p);
            }
            if (n > 0) { one.RefreshEatmap(); two.RefreshEatmap(); }
            return n;
        }

        // ------------------------------------------------------------------ template leftovers, animations, dynamic appearances

        static bool Same(Il2CppObjectBase a, Il2CppObjectBase b) => (object)a != null && (object)b != null && a.Pointer == b.Pointer;
        static bool RootHas(SlimeDefinition d, string comp) => d != null && d.prefab != null && VanillaSlimes.RootComponents(d.prefab).Contains(comp);

        /// <summary>
        /// Removes components that the template brought from its own other parent (the one this largo does not have), e.g. a
        /// Rock-only behaviour of PinkRock on a largo without Rock. Learned per parent by <see cref="VanillaSlimes"/>.
        /// </summary>
        static int DropTemplateLeftovers(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two, SlimeDefinition template, Result r)
        {
            var go = largo.prefab;
            var tb = template != null ? template.BaseSlimes : null;
            if (go == null || tb == null) return 0;
            int n = 0;
            foreach (var tp in tb)
            {
                if (tp == null || Same(tp, one) || Same(tp, two)) continue;
                foreach (var c in VanillaSlimes.ParentComponents(tp.name))
                {
                    if (RootHas(one, c) || RootHas(two, c)) continue;
                    foreach (var comp in go.GetComponents<MonoBehaviour>())
                    {
                        if (comp == null || comp.GetIl2CppType().Name != c) continue;
                        UnityEngine.Object.DestroyImmediate(comp);
                        n++;
                        r.Notes.Add($"removed {c} (from the template's parent {tp.name})");
                    }
                }
            }
            if (n > 0) Safe.Run("plexer " + largo.name, () => Largos.FixPlexer(go, false));
            return n;
        }

        internal static readonly string[] ClipNames = { "idle", "wing flap", "bite" };
        internal static AnimationClip Clip(SlimeAppearance a, int i) => a == null ? null : i == 0 ? a._idleAnimationOverride : i == 1 ? a._wingFlapAnimationOverride : a._biteAnimationOverride;
        static void SetClip(SlimeAppearance a, int i, AnimationClip c)
        {
            if (i == 0) a._idleAnimationOverride = c;
            else if (i == 1) a._wingFlapAnimationOverride = c;
            else a._biteAnimationOverride = c;
        }

        /// <summary>The animation override a largo should have (vanilla rule): the body parent's, else the other parent's.</summary>
        internal static AnimationClip ExpectedClip(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two, int i)
        {
            var body = LargoBlend.InfoOf(largo)?.Body;
            bool second = two != null && body != null && string.Equals(body, two.name, StringComparison.OrdinalIgnoreCase);
            SlimeDefinition first = second ? two : one, other = second ? one : two;
            return Clip(Default(first), i) ?? Clip(Default(other), i);
        }

        /// <summary>Every own appearance of a largo (default, radiant variants), without the template's and the parents'.</summary>
        internal static List<SlimeAppearance> OwnAppearances(SlimeDefinition largo, params SlimeDefinition[] foreign)
        {
            var skip = new HashSet<IntPtr>();
            foreach (var d in foreign)
            {
                if (d == null) continue;
                skip.UnionWith(VanillaSlimes.Set(d.AppearancesDefault));
                if (d.RadiantLargo0 != null) skip.Add(d.RadiantLargo0.Pointer);
                if (d.RadiantLargo1 != null) skip.Add(d.RadiantLargo1.Pointer);
            }
            var list = new List<SlimeAppearance>();
            var seen = new HashSet<IntPtr>();
            void Add(SlimeAppearance a) { if (a != null && !skip.Contains(a.Pointer) && seen.Add(a.Pointer)) list.Add(a); }
            var arr = largo.AppearancesDefault;
            if (arr != null) for (int i = 0; i < arr.Length; i++) Add(arr[i]);
            Add(largo.RadiantLargo0); Add(largo.RadiantLargo1);
            return list;
        }

        /// <summary>Idle / wing flap / bite animation overrides from the parents instead of the template (as vanilla largos have them).</summary>
        static int Animations(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two, SlimeDefinition template)
        {
            if (!VanillaSlimes.AnimationsFromParents) return 0;
            int n = 0;
            foreach (var app in OwnAppearances(largo, template, one, two))
            {
                bool changed = false;
                for (int i = 0; i < ClipNames.Length; i++)
                {
                    var want = ExpectedClip(largo, one, two, i);
                    var have = Clip(app, i);
                    if ((want == null && have == null) || Same(want, have)) continue;
                    SetClip(app, i, want);
                    changed = true; n++;
                }
                if (changed) app._cachedAnimatorOverrideController = null;
            }
            return n;
        }

        /// <summary>
        /// <c>AppearancesDynamic</c> without the template's entries: its appearances become the largo's (same mapping as the
        /// prefab references), its other dynamic appearances are dropped, duplicates removed.
        /// </summary>
        static int Dynamic(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two, SlimeDefinition template)
        {
            var dyn = largo.AppearancesDynamic;
            if (dyn == null || dyn.Count == 0 || template == null) return 0;
            var map = TemplateMap(template, largo);
            var tdyn = template.AppearancesDynamic;
            var tset = new HashSet<IntPtr>();
            if (tdyn != null) for (int i = 0; i < tdyn.Count; i++) if (tdyn[i] != null) tset.Add(tdyn[i].Pointer);
            var keep = VanillaSlimes.Set(one != null ? one.AppearancesDefault : null);
            keep.UnionWith(VanillaSlimes.Set(two != null ? two.AppearancesDefault : null));
            bool shared = tdyn != null && tdyn.Pointer == dyn.Pointer;
            var list = new Il2CppSystem.Collections.Generic.List<SlimeAppearance>();
            var seen = new HashSet<IntPtr>();
            int changed = 0;
            for (int i = 0; i < dyn.Count; i++)
            {
                var a = dyn[i];
                if (a == null) { changed++; continue; }
                var to = a;
                if (map.TryGetValue(a.Pointer, out var m)) { to = m.TryCast<SlimeAppearance>(); changed++; }
                else if (tset.Contains(a.Pointer) && !keep.Contains(a.Pointer)) { changed++; continue; }
                if (to != null && seen.Add(to.Pointer)) list.Add(to); else changed++;
            }
            if (changed == 0 && !shared) return 0;
            largo.AppearancesDynamic = list;
            return Math.Max(changed, 1);
        }

        // ------------------------------------------------------------------ entry point (CreateCompleteLargo)

        /// <summary>
        /// Everything except the look and the components (see the class summary). Called by <c>CreateCompleteLargo</c> after
        /// the components were merged, before <see cref="LargoBlend.Finish"/> (so OnCreated hooks see the final largo).
        /// </summary>
        public static Result Fix(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two, SlimeDefinition template)
        {
            if (!Enabled || largo == null || one == null || two == null) return null;
            var r = For(largo);
            VanillaSlimes.Learn();
            Safe.Run("parity refs " + largo.name, () => r.Repointed += Repoint(largo.prefab, TemplateMap(template, largo)));
            Safe.Run("parity leftovers " + largo.name, () => r.Dropped += DropTemplateLeftovers(largo, one, two, template, r));
            Safe.Run("parity groups " + largo.name, () => r.Groups += JoinLargoGroups(largo, one, two));
            Safe.Run("parity full art " + largo.name, () => r.FullArt = ClearFullArt(largo));
            Safe.Run("parity zones " + largo.name, () => r.Zones = Zones(largo, one, two));
            Safe.Run("parity diet " + largo.name, () => Diet(largo, template, r));
            Safe.Run("parity plorts " + largo.name, () => r.Plorts += Plorts(one, two));
            Safe.Run("parity animations " + largo.name, () => r.Animations += Animations(largo, one, two, template));
            Safe.Run("parity dynamic " + largo.name, () => r.Dynamic += Dynamic(largo, one, two, template));
            SlimeIcons.Queue(largo, one, two, template);
            var info = LargoBlend.InfoOf(largo);
            if (info != null) info.Parity = r.ToString();
            ForgeLog.Debug("[parity] " + largo.name + ": " + r);
            if (!_auditQueued && LargoAudit.AutoRun)
            {
                _auditQueued = true;
                Scheduler.Seconds(LargoAudit.AutoDelay, () => Safe.Run("largo audit", LargoAudit.Auto), "largo audit");
            }
            return r;
        }
    }
}
