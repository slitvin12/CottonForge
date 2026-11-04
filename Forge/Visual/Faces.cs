using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Slime faces. <c>SlimeFace.ExpressionFaces</c> is an IL2CPP array of the VALUE type <c>SlimeExpressionFace</c> that
    /// Il2CppInterop exposes as a reference array: writing it through the wrapper corrupts faces or crashes. These helpers read
    /// and write the elements through raw memory with the GC write barrier, then call <c>UpdateCache()</c>.
    /// <para>Materials made for a face are protected from <c>Object.Destroy</c> (SR2 destroys what it believes is a per-slime
    /// mouth instance; with a shared runtime material that turned every mouth of the kind magenta) and self-healed every 2 s.</para>
    /// <para>Empty mouth slots of stock faces stay empty on purpose (SR2 animates an instance of the current mouth there).</para>
    /// </summary>
    public static unsafe partial class Faces
    {
        static bool _init;
        static int _elem, _offEx, _offEyes, _offMouth;

        static void Init()
        {
            if (_init) return;
            var cls = Il2CppClassPointerStore<SlimeExpressionFace>.NativeClassPtr;
            uint align = 0;
            _elem = (int)IL2CPP.il2cpp_class_value_size(cls, ref align);
            int hdr = 2 * IntPtr.Size; // value-type field offsets include the boxed object header
            _offEx = (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(cls, "SlimeExpression")) - hdr;
            _offEyes = (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(cls, "Eyes")) - hdr;
            _offMouth = (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(cls, "Mouth")) - hdr;
            _init = true;
            ForgeLog.Debug($"[faces] elem={_elem} ex={_offEx} eyes={_offEyes} mouth={_offMouth}");
        }

        static IntPtr Arr(SlimeFace f) { var a = f.ExpressionFaces; return a == null ? IntPtr.Zero : a.Pointer; }
        static int Len(IntPtr arr) => arr == IntPtr.Zero ? 0 : (int)*(long*)(arr + 3 * IntPtr.Size);
        static IntPtr Elem(IntPtr arr, int i) => arr + 4 * IntPtr.Size + i * _elem;
        static Material M(IntPtr p) => p == IntPtr.Zero ? null : new Material(p);
        static void Set(IntPtr arr, IntPtr addr, Material m) => IL2CPP.il2cpp_gc_wbarrier_set_field(arr, addr, m == null ? IntPtr.Zero : m.Pointer);

        /// <summary>One expression of a face.</summary>
        public readonly struct Expression
        {
            public readonly SlimeFace.SlimeExpression Kind; public readonly Material Eyes, Mouth;
            public Expression(SlimeFace.SlimeExpression k, Material e, Material m) { Kind = k; Eyes = e; Mouth = m; }
        }

        /// <summary>Reads every expression (eyes/mouth may be null).</summary>
        public static List<Expression> Read(SlimeFace f)
        {
            Init();
            var r = new List<Expression>();
            if (f == null) return r;
            var arr = Arr(f);
            int n = Len(arr);
            for (int i = 0; i < n; i++)
            {
                var e = Elem(arr, i);
                r.Add(new Expression((SlimeFace.SlimeExpression)(*(int*)(e + _offEx)), M(*(IntPtr*)(e + _offEyes)), M(*(IntPtr*)(e + _offMouth))));
            }
            return r;
        }

        /// <summary>
        /// Clones a face and remaps every eye/mouth material (null func or null result keeps the original). Results are
        /// cached per source material, so shared materials stay shared. Empty eye slots are filled with the HAPPY eyes
        /// (an empty slot renders magenta); empty mouth slots stay empty. The clone is registered for self-healing.
        /// </summary>
        public static SlimeFace Clone(SlimeFace src, string name, Func<Material, Material> eyes, Func<Material, Material> mouth)
        {
            Init();
            if (src == null) return null;
            var f = Object.Instantiate(src);
            f.name = name;
            f.hideFlags |= HideFlags.HideAndDontSave;
            var arr = Arr(f);
            int n = Len(arr);
            var ce = new Dictionary<IntPtr, Material>();
            var cm = new Dictionary<IntPtr, Material>();
            var srcE = new IntPtr[n];
            var srcM = new IntPtr[n];
            for (int i = 0; i < n; i++) { var e = Elem(arr, i); srcE[i] = *(IntPtr*)(e + _offEyes); srcM[i] = *(IntPtr*)(e + _offMouth); }
            for (int i = 0; i < n; i++)
            {
                var e = Elem(arr, i);
                Remap(arr, e + _offEyes, eyes, ce);
                Remap(arr, e + _offMouth, mouth, cm);
            }
            IntPtr happyE = IntPtr.Zero, fillSrcE = IntPtr.Zero;
            for (int i = 0; i < n; i++)
            {
                var e = Elem(arr, i);
                if ((SlimeFace.SlimeExpression)(*(int*)(e + _offEx)) != SlimeFace.SlimeExpression.HAPPY) continue;
                happyE = *(IntPtr*)(e + _offEyes);
                fillSrcE = srcE[i];
            }
            for (int i = 0; i < n && (happyE == IntPtr.Zero || fillSrcE == IntPtr.Zero); i++)
            {
                if (happyE == IntPtr.Zero) happyE = *(IntPtr*)(Elem(arr, i) + _offEyes);
                if (fillSrcE == IntPtr.Zero) fillSrcE = srcE[i];
            }
            for (int i = 0; i < n; i++) if (srcE[i] == IntPtr.Zero) srcE[i] = fillSrcE;
            int filled = 0;
            for (int i = 0; i < n; i++)
            {
                var e = Elem(arr, i);
                if (*(IntPtr*)(e + _offEyes) == IntPtr.Zero && happyE != IntPtr.Zero) { IL2CPP.il2cpp_gc_wbarrier_set_field(arr, e + _offEyes, happyE); filled++; }
            }
            f.UpdateCache();
            _regs.Add(new Reg { Face = f, Name = name, Eyes = eyes, Mouth = mouth, SrcE = srcE, SrcM = srcM, CE = ce, CM = cm });
            ForgeLog.Debug($"[faces] {name}: {n} expressions, filled eyes {filled}, new mats {ce.Count}/{cm.Count}");
            return f;
        }

        /// <summary>Material with the same shader/textures but a different colour property (helper for eye/mouth remaps).</summary>
        public static Func<Material, Material> Recolor(string prefix, string colorProperty, Color c) => src =>
        {
            var m = Mat.Copy(src, prefix + "_" + src.name);
            m.Col(colorProperty, c);
            return m;
        };

        // ------------------------------------------------------------------ protection + self-heal
        sealed class Reg
        {
            public SlimeFace Face; public string Name;
            public Func<Material, Material> Eyes, Mouth;
            public IntPtr[] SrcE, SrcM;
            public Dictionary<IntPtr, Material> CE, CM;
        }

        static readonly List<Reg> _regs = new();
        static float _next;

        /// <summary>Native pointers of materials that <c>Object.Destroy</c> must not destroy (see <see cref="DestroyGuard"/>).</summary>
        public static readonly HashSet<IntPtr> Protected = new();

        /// <summary>Destroy calls blocked / slots healed (diagnostics).</summary>
        public static int Blocked, Healed;

        /// <summary>Protects any runtime material from <c>Object.Destroy</c>.</summary>
        public static void Protect(Material m) { if (m != null) Protected.Add(m.Pointer); }

        public static bool IsOurs(SlimeFace f)
        {
            if (f == null) return false;
            foreach (var r in _regs) if (r.Face != null && r.Face.Pointer == f.Pointer) return true;
            return false;
        }

        static bool Alive(IntPtr p) => p != IntPtr.Zero && new Material(p) != null;

        /// <summary>Called by Forge every frame; heals dead slots every 2 s.</summary>
        public static void Tick()
        {
            if (_regs.Count == 0 || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            try
            {
                foreach (var r in _regs)
                {
                    if (r.Face == null) continue;
                    var arr = Arr(r.Face);
                    int n = Math.Min(Len(arr), r.SrcE.Length);
                    bool changed = false;
                    for (int i = 0; i < n; i++)
                    {
                        var e = Elem(arr, i);
                        changed |= Heal(arr, e + _offEyes, r.SrcE[i], r.Eyes, r.CE, r.Name + ".eyes");
                        if (r.SrcM[i] != IntPtr.Zero) changed |= Heal(arr, e + _offMouth, r.SrcM[i], r.Mouth, r.CM, r.Name + ".mouth");
                    }
                    if (changed) r.Face.UpdateCache();
                }
            }
            catch (Exception e) { ForgeLog.WarnOnce("faces.tick", "[faces] heal: " + e); }
        }

        static bool Heal(IntPtr arr, IntPtr addr, IntPtr src, Func<Material, Material> fn, Dictionary<IntPtr, Material> cache, string what)
        {
            var cur = *(IntPtr*)addr;
            if (Alive(cur)) return false;
            Material m = null;
            if (src != IntPtr.Zero)
            {
                if (cache.TryGetValue(src, out var c) && c != null) m = c;
                else if (Alive(src))
                {
                    var sm = new Material(src);
                    try { m = fn != null ? fn(sm) : sm; } catch (Exception ex) { ForgeLog.Warn("[faces] heal: " + ex.Message); }
                    if (m != null && fn != null) { cache[src] = m; Mat.Track(m); Protected.Add(m.Pointer); }
                }
            }
            if (m == null) return false;
            Set(arr, addr, m);
            Healed++;
            ForgeLog.MsgOnce("heal:" + what + m.name, $"[faces] healed {what} ({(cur == IntPtr.Zero ? "empty" : "destroyed material")}) -> {m.name}");
            return true;
        }

        static void Remap(IntPtr arr, IntPtr addr, Func<Material, Material> fn, Dictionary<IntPtr, Material> cache)
        {
            if (fn == null) return;
            var p = *(IntPtr*)addr;
            if (p == IntPtr.Zero) return;
            if (!cache.TryGetValue(p, out var nm))
            {
                try { nm = fn(new Material(p)); } catch (Exception ex) { ForgeLog.Warn("[faces] remap: " + ex.Message); nm = null; }
                cache[p] = nm;
            }
            if (nm != null) { Mat.Track(nm); Protected.Add(nm.Pointer); Set(arr, addr, nm); }
        }
    }

    /// <summary>Skips <c>Object.Destroy</c>/<c>DestroyImmediate</c> for objects in <see cref="Faces.Protected"/>.</summary>
    [HarmonyPatch]
    static class DestroyGuard
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var m in typeof(Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "Destroy" && m.Name != "DestroyImmediate") continue;
                var ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(Object)) yield return m;
            }
        }

        static bool Prefix(Object __0)
        {
            if (Faces.Protected.Count == 0 || (object)__0 == null) return true;
            IntPtr p;
            try { p = __0.Pointer; } catch { return true; }
            if (!Faces.Protected.Contains(p)) return true;
            if (Faces.Blocked++ == 0) ForgeLog.Msg("[faces] the game tried to destroy protected material " + __0.name + " - kept alive (further cases are silent)");
            return false;
        }
    }
}
