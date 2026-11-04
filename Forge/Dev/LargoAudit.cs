using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using ET = Il2Cpp.SlimeAppearanceElement.ElementType;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Checks generated largos against what a vanilla largo has (dev command <c>largoaudit</c>): definition, registration,
    /// stable ids, groups, diet (does a parent eating the other's plort become this largo, does it turn into tarr),
    /// appearance (face, dependents, template leftovers, radiant variants, director), prefab (references, components).
    /// <see cref="SelfTest"/> rebuilds every vanilla largo from its parents without changing anything and compares the
    /// engine's choices with the real one. Reports go to <c>UserData/CottonLibrary/</c>.
    /// </summary>
    public static class LargoAudit
    {
        /// <summary>Audit every generated largo once after generation and log one summary line (file largo_audit.txt).</summary>
        public static bool AutoRun = true;
        public static float AutoDelay = 10f;

        public sealed class Report
        {
            public string Largo;
            public readonly List<string> Problems = new(), Notes = new();
            public bool Ok => Problems.Count == 0;
            public string Text(bool notes)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{Largo}: {(Ok ? "OK" : Problems.Count + " problem(s)")}");
                foreach (var p in Problems) sb.AppendLine("  ! " + p);
                if (notes) foreach (var n in Notes) sb.AppendLine("  - " + n);
                return sb.ToString();
            }
        }

        static bool Same(Il2CppObjectBase a, Il2CppObjectBase b) => (object)a != null && (object)b != null && a.Pointer == b.Pointer;
        static SlimeAppearance Default(SlimeDefinition d) => VanillaSlimes.App(d);
        static string N(UnityEngine.Object o) => o == null ? "null" : o.name;

        static IEnumerable<SlimeAppearanceStructure> Structures(SlimeAppearance app)
        {
            var arr = app != null ? app.Structures : null;
            if (arr == null) yield break;
            for (int i = 0; i < arr.Length; i++) if (arr[i] != null && arr[i].Element != null) yield return arr[i];
        }

        static HashSet<IntPtr> MaterialsOf(SlimeAppearance app)
        {
            var s = new HashSet<IntPtr>();
            foreach (var st in Structures(app))
            {
                var m = st.DefaultMaterials;
                if (m != null) for (int i = 0; i < m.Length; i++) if (m[i] != null) s.Add(m[i].Pointer);
            }
            return s;
        }

        static List<SlimeDefinition> AllSlimes()
        {
            var res = new List<SlimeDefinition>();
            var defs = Library.slimeDefinitions?.Slimes;
            if (defs != null) foreach (var d in defs) if (d != null) res.Add(d);
            return res;
        }

        /// <summary>Generated (not vanilla) largos.</summary>
        public static List<SlimeDefinition> Generated()
        {
            var seen = new HashSet<IntPtr>();
            return AllSlimes().Where(d => d.IsLargo && !VanillaSlimes.IsVanilla(d) && seen.Add(d.Pointer)).ToList();
        }

        static SlimeDefinition TemplateOf(SlimeDefinition largo)
        {
            var name = LargoBlend.InfoOf(largo)?.Template;
            var t = name != null ? VanillaSlimes.Largos.FirstOrDefault(l => l.name == name) : null;
            return t ?? Library.GetLargo("PinkRock");
        }

        // ------------------------------------------------------------------ one largo

        public static Report Check(SlimeDefinition largo)
        {
            var rep = new Report { Largo = largo != null ? largo.name : "null" };
            if (largo == null) { rep.Problems.Add("null definition"); return rep; }
            VanillaSlimes.Learn();
            Action<string> P = rep.Problems.Add, Nt = rep.Notes.Add;
            var b = largo.BaseSlimes;
            SlimeDefinition one = b != null && b.Length > 0 ? b[0] : null, two = b != null && b.Length > 1 ? b[1] : null;
            var tpl = TemplateOf(largo);
            Nt($"template {N(tpl)}, parents {N(one)} + {N(two)}; " + (LargoBlend.InfoOf(largo)?.ToString() ?? "no engine record"));
            Safe.Run("audit def " + largo.name, () => Definition(largo, one, two, tpl, P, Nt));
            Safe.Run("audit registration " + largo.name, () => Registration(largo, one, two, P, Nt));
            Safe.Run("audit groups " + largo.name, () => Groups(largo, one, two, P, Nt));
            Safe.Run("audit diet " + largo.name, () => Diet(largo, one, two, tpl, P, Nt));
            Safe.Run("audit appearance " + largo.name, () => Appearance(largo, one, two, tpl, P, Nt));
            Safe.Run("audit prefab " + largo.name, () => Prefab(largo, one, two, tpl, P, Nt));
            var par = LargoParity.ResultOf(largo);
            if (par != null) Nt("parity: " + par);
            return rep;
        }

        static void Definition(SlimeDefinition l, SlimeDefinition one, SlimeDefinition two, SlimeDefinition tpl, Action<string> P, Action<string> Nt)
        {
            if (!l.IsLargo) P("IsLargo is false");
            if (one == null || two == null) P("BaseSlimes is not two slimes");
            if (string.IsNullOrEmpty(l.referenceId)) P("no referenceId");
            if (l.prefab == null) P("no prefab");
            if (l.localizedName == null) P("no localized name");
            if (l.icon == null) { if (SlimeIcons.Enabled && SlimeIcons.Pending > 0) Nt("no icon yet (icons pending)"); else P("no icon"); }
            else if (tpl != null && Same(l.icon, tpl.icon))
            {
                if (SlimeIcons.Enabled && SlimeIcons.Pending == 0) P($"icon is still the template's ({N(tpl)})");
                else Nt($"icon is still the template's ({N(tpl)}){(SlimeIcons.Pending > 0 ? ", icons pending" : ", LargoIcons off")}");
            }
            else Nt("icon " + N(l.icon) + (SlimeIcons.HowOf(l) != null ? " (" + SlimeIcons.HowOf(l) + ")" : ""));
            if (l._requiresFullArt) Nt("requires full art (template's pedia picture?)");
            if (tpl != null && Same(l.Diet, tpl.Diet)) P("shares the template's diet object");
        }

        static void Registration(SlimeDefinition l, SlimeDefinition one, SlimeDefinition two, Action<string> P, Action<string> Nt)
        {
            var all = AllSlimes();
            int listed = all.Count(d => Same(d, l));
            if (listed == 0) P("not in SlimeDefinitions.Slimes");
            else if (listed > 1) P($"listed {listed} times in SlimeDefinitions.Slimes");
            var defs = Library.slimeDefinitions;
            Safe.Quiet(() => { if (!defs._slimeDefinitionsByIdentifiable.ContainsKey(l)) P("not in SlimeDefinitions._slimeDefinitionsByIdentifiable"); });
            if (one != null && two != null)
            {
                SlimeDefinition found = null;
                Safe.Quiet(() => found = defs.GetLargoByBaseSlimes(one, two) ?? defs.GetLargoByBaseSlimes(two, one));
                if (!Same(found, l)) P($"GetLargoByBaseSlimes({one.name}, {two.name}) returns {N(found)}");
            }
            var dupId = all.Where(d => !Same(d, l) && d.referenceId == l.referenceId).Select(d => d.name).Distinct().ToList();
            if (dupId.Count > 0) P("referenceId also used by " + string.Join(", ", dupId));
            Safe.Quiet(() =>
            {
                var lk = Library.gameContext.LookupDirector;
                if (lk != null && lk._identifiableTypeByRefId != null)
                {
                    IdentifiableType x = null;
                    if (!lk._identifiableTypeByRefId.TryGetValue(l.referenceId, out x) || !Same(x, l)) P($"LookupDirector does not map {l.referenceId} to it ({N(x)})");
                }
            });
            Safe.Quiet(() =>
            {
                int h = l.StableHashedId;
                var dup = all.Where(d => !Same(d, l) && d.StableHashedId == h).Select(d => d.name).Distinct().ToList();
                if (dup.Count > 0) P($"StableHashedId {h} also used by " + string.Join(", ", dup));
            });
        }

        static void Groups(SlimeDefinition l, SlimeDefinition one, SlimeDefinition two, Action<string> P, Action<string> Nt)
        {
            if (!Idents.InGroup(Library.largos, l)) P("not in LargoGroup");
            if (!Idents.InGroup(Library.slimes, l)) P("not in SlimesGroup");
            foreach (var p in new[] { one, two })
            {
                if (p == null) continue;
                var g = Library.Get<IdentifiableTypeGroup>(p.name + "LargoGroup");
                if (g != null && !Idents.InGroup(g, l)) P($"not in {g.name}");
                foreach (var pg in VanillaSlimes.ParentGroups(p.name)) if (!Idents.InGroup(pg, l)) P($"not in {pg.name} (every vanilla {p.name} largo is)");
            }
            foreach (var g in VanillaSlimes.LargoGroups) if (!Idents.InGroup(g, l)) P($"not in {g.name} (every vanilla largo is)");
            foreach (var p in new[] { one, two })
            {
                var prod = p != null && !VanillaSlimes.IsVanilla(p) && p.Diet != null ? p.Diet.ProduceIdents : null;
                if (prod == null) continue;
                for (int i = 0; i < prod.Length; i++)
                    if (prod[i] != null)
                        foreach (var g in VanillaSlimes.PlortGroups)
                            if (!Idents.InGroup(g, prod[i])) P($"{p.name}'s plort {prod[i].name} is not in {g.name} (every vanilla largoable plort is)");
            }
        }

        static HashSet<IntPtr> Produces(SlimeDefinition d)
        {
            var s = new HashSet<IntPtr>();
            var arr = d != null && d.Diet != null ? d.Diet.ProduceIdents : null;
            if (arr != null) for (int i = 0; i < arr.Length; i++) if (arr[i] != null) s.Add(arr[i].Pointer);
            return s;
        }

        static void Diet(SlimeDefinition l, SlimeDefinition one, SlimeDefinition two, SlimeDefinition tpl, Action<string> P, Action<string> Nt)
        {
            var d = l.Diet;
            if (d == null) { P("no diet"); return; }
            var prod = Produces(l);
            foreach (var p in new[] { one, two })
                foreach (var x in Produces(p)) if (!prod.Contains(x)) P($"does not produce {p.name}'s plort");
            var groups = VanillaSlimes.Set(d.MajorFoodIdentifiableTypeGroups);
            foreach (var p in new[] { one, two })
                if (p != null && p.Diet != null)
                    foreach (var g in VanillaSlimes.Set(p.Diet.MajorFoodIdentifiableTypeGroups)) if (!groups.Contains(g)) P($"does not eat {p.name}'s food group");
            int entries = d.EatMap != null ? d.EatMap.Count : 0, becomes = 0;
            if (d.EatMap != null) for (int i = 0; i < d.EatMap.Count; i++) if (d.EatMap[i]?.BecomesIdent != null) becomes++;
            int tb = 0;
            if (tpl?.Diet?.EatMap != null) for (int i = 0; i < tpl.Diet.EatMap.Count; i++) if (tpl.Diet.EatMap[i]?.BecomesIdent != null) tb++;
            Nt($"eat map {entries} entries, {becomes} transform (template {tb}), favourite production {d.FavoriteProductionCount} (vanilla {VanillaSlimes.FavoriteProduction})");
            if (entries == 0) P("empty eat map");
            if (tb > 0 && becomes == 0) P("never turns into tarr (no BecomesIdent entries)");
            if (d.BecomesOnTarrifyIdentifiableType == null) P("BecomesOnTarrify is null");
            if (VanillaSlimes.FavoriteProduction > 0 && d.FavoriteProductionCount != VanillaSlimes.FavoriteProduction) P($"FavoriteProductionCount {d.FavoriteProductionCount}, vanilla largos {VanillaSlimes.FavoriteProduction}");
            // the way the largo is born: a parent eats the other's plort
            foreach (var (a, x) in new[] { (one, two), (two, one) })
            {
                if (a?.Diet?.EatMap == null || x == null) continue;
                var px = Produces(x);
                bool born = false, eats = false;
                for (int i = 0; i < a.Diet.EatMap.Count; i++)
                {
                    var e = a.Diet.EatMap[i];
                    if (e?.EatsIdent == null || !px.Contains(e.EatsIdent.Pointer)) continue;
                    eats = true;
                    if (Same(e.BecomesIdent, l)) born = true;
                }
                if (!born) P(eats ? $"{a.name} eating a {x.name} plort does not become this largo" : $"{a.name} does not eat {x.name} plorts");
            }
        }

        static void Appearance(SlimeDefinition l, SlimeDefinition one, SlimeDefinition two, SlimeDefinition tpl, Action<string> P, Action<string> Nt)
        {
            var app = Default(l);
            if (app == null) { P("no default appearance"); return; }
            var st = Structures(app).ToList();
            if (st.Count == 0) P("no structures");
            if (!st.Any(s => s.Element.Type == ET.BODY)) P("no BODY structure");
            if (app._face == null) P("no face");
            var dep = app._dependentAppearances;
            foreach (var p in new[] { one, two })
            {
                var pa = Default(p);
                bool has = false;
                if (dep != null) for (int i = 0; i < dep.Length; i++) if (Same(dep[i], pa)) has = true;
                if (pa != null && !has) P($"default appearance does not depend on {p.name}'s");
            }
            var mine = MaterialsOf(app);
            var parents = MaterialsOf(Default(one)); parents.UnionWith(MaterialsOf(Default(two)));
            var tmats = MaterialsOf(Default(tpl)); tmats.ExceptWith(parents);
            foreach (var r in VanillaSlimes.PartRules)   // learned largo versions of parts: shared by the vanilla largos too
            {
                var sm = r.Substitute != null ? r.Substitute.DefaultMaterials : null;
                if (sm != null) for (int k = 0; k < sm.Length; k++) if (sm[k] != null) tmats.Remove(sm[k].Pointer);
            }
            int sharedP = mine.Count(parents.Contains), sharedT = mine.Count(tmats.Contains);
            Nt($"{st.Count} structures: {string.Join(", ", st.Select(s => s.Element.Name))}; {sharedP} materials shared with the parents");
            if (sharedT > 0) P($"{sharedT} material(s) of the template {N(tpl)} still used");
            Safe.Quiet(() =>
            {
                var dir = Library.mainAppearanceDirector;
                var chosen = dir != null ? dir.GetChosenSlimeAppearance(l) : null;
                var apps = l.AppearancesDefault;
                bool ours = false;
                if (apps != null) for (int i = 0; i < apps.Length; i++) if (Same(apps[i], chosen)) ours = true;
                if (dir != null && chosen == null) Nt("appearance director has no chosen appearance yet (no save loaded?)");
                else if (dir != null && !ours) P($"appearance director chose {N(chosen)} (not one of its appearances)");
            });
            // radiant variants
            var ad = l.AppearancesDefault;
            Nt($"{(ad != null ? ad.Length : 0)} appearances: {(ad != null ? string.Join(", ", Enumerable.Range(0, ad.Length).Select(i => ad[i] != null ? ad[i].name + " " + ad[i]._appearType : "null")) : "")}");
            foreach (var x in new[] { l.RadiantLargo0, l.RadiantLargo1 })
                if (x != null && tpl != null && (Same(x, tpl.RadiantLargo0) || Same(x, tpl.RadiantLargo1))) P($"radiant variant {x.name} is the template's");
            if (LargoBlend.BuildRadiants)
                foreach (var p in new[] { one, two })
                {
                    var rb = LargoBlend.RadiantOf(p);
                    if (rb == null) continue;
                    bool found = false;
                    if (ad != null)
                        for (int i = 0; i < ad.Length; i++)
                        {
                            var dd = ad[i] != null ? ad[i]._dependentAppearances : null;
                            if (dd != null) for (int k = 0; k < dd.Length; k++) if (Same(dd[k], rb)) found = true;
                        }
                    if (!found) P($"no radiant variant for radiant {p.name}");
                }
            Safe.Quiet(() =>
            {
                var dyn = l.AppearancesDynamic;
                if (dyn == null || dyn.Count == 0) return;
                var tset = new HashSet<IntPtr>();
                var tdyn = tpl != null ? tpl.AppearancesDynamic : null;
                if (tdyn != null) for (int i = 0; i < tdyn.Count; i++) if (tdyn[i] != null) tset.Add(tdyn[i].Pointer);
                var keep = VanillaSlimes.Set(one?.AppearancesDefault); keep.UnionWith(VanillaSlimes.Set(two?.AppearancesDefault));
                var names = new List<string>(); int fromT = 0;
                for (int i = 0; i < dyn.Count; i++) if (dyn[i] != null) { names.Add(dyn[i].name); if (tset.Contains(dyn[i].Pointer) && !keep.Contains(dyn[i].Pointer)) fromT++; }
                if (tpl != null && tdyn != null && tdyn.Pointer == dyn.Pointer) P("AppearancesDynamic is the template's list");
                if (fromT > 0) P($"{fromT} dynamic appearance(s) of the template {N(tpl)}");
                Nt($"{dyn.Count} dynamic appearances: {string.Join(", ", names)}");
            });
            // animation overrides (vanilla rule: the body parent's, else the other parent's)
            Safe.Quiet(() =>
            {
                for (int i = 0; i < LargoParity.ClipNames.Length; i++)
                {
                    var want = LargoParity.ExpectedClip(l, one, two, i);
                    foreach (var a in LargoParity.OwnAppearances(l, tpl, one, two))
                    {
                        var have = LargoParity.Clip(a, i);
                        if ((want == null && have == null) || Same(want, have)) continue;
                        var msg = $"{a.name}: {LargoParity.ClipNames[i]} animation {N(have)}, parents give {N(want)}";
                        if (VanillaSlimes.AnimationsFromParents) P(msg); else Nt(msg + " (vanilla does not follow the rule)");
                    }
                }
            });
        }

        static void Prefab(SlimeDefinition l, SlimeDefinition one, SlimeDefinition two, SlimeDefinition tpl, Action<string> P, Action<string> Nt)
        {
            var go = l.prefab;
            if (go == null) return;
            void Is(string what, Il2CppObjectBase v) { if (!Same(v, l)) P($"{what} is {(v is UnityEngine.Object o ? N(o) : v == null ? "null" : "?")}"); }
            Is("Identifiable.identType", go.GetComponent<Identifiable>()?.identType);
            Is("SlimeEat.SlimeDefinition", go.GetComponent<SlimeEat>()?.SlimeDefinition);
            Is("SlimeAppearanceApplicator.SlimeDefinition", go.GetComponent<SlimeAppearanceApplicator>()?.SlimeDefinition);
            var ptw = go.GetComponent<PlayWithToys>(); if (ptw != null) Is("PlayWithToys.SlimeDefinition", ptw.SlimeDefinition);
            var rtt = go.GetComponent<ReactToToyNearby>(); if (rtt != null) Is("ReactToToyNearby.SlimeDefinition", rtt.SlimeDefinition);

            // every definition / appearance reference, every reference into a parent prefab
            var okDefs = new HashSet<IntPtr> { l.Pointer };
            if (one != null) okDefs.Add(one.Pointer);
            if (two != null) okDefs.Add(two.Pointer);
            var tplApps = new HashSet<IntPtr>();
            if (tpl?.AppearancesDefault != null) for (int i = 0; i < tpl.AppearancesDefault.Length; i++) if (tpl.AppearancesDefault[i] != null) tplApps.Add(tpl.AppearancesDefault[i].Pointer);
            var roots = new List<(string, Transform)>();
            foreach (var p in new[] { one, two, tpl }) if (p?.prefab != null) roots.Add((p.name, p.prefab.transform));
            int refs = 0;
            foreach (var comp in go.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                var tn = comp.GetIl2CppType().Name;
                foreach (var f in LargoParity.FieldsOf(comp.GetIl2CppType()))
                {
                    if (f.FieldType == null || f.FieldType.IsValueType) continue;
                    Il2CppSystem.Object v;
                    try { v = f.GetValue(comp); } catch { continue; }
                    if (v == null) continue;
                    if (tpl != null && v.Pointer == tpl.Pointer) { P($"{tn}.{f.Name} -> template {tpl.name}"); continue; }
                    if (tplApps.Contains(v.Pointer)) { P($"{tn}.{f.Name} -> template appearance"); continue; }
                    var sd = v.TryCast<SlimeDefinition>();
                    if (sd != null && !okDefs.Contains(sd.Pointer)) { Nt($"{tn}.{f.Name} -> {sd.name}"); continue; }
                    var et = LargoParity.ElementOf(f.FieldType);
                    if (et != null)
                    {
                        bool scene = LargoParity.IsSceneType(et);
                        if (!scene && !LargoParity.IsRefType(et)) continue;
                        LargoParity.Seq s = null;
                        try { s = LargoParity.Seq.Of(v); } catch { }
                        if (s == null) continue;
                        for (int i = 0; i < s.Count; i++)
                        {
                            Il2CppSystem.Object e;
                            try { e = s[i]; } catch { break; }
                            if (e == null) continue;
                            if (tpl != null && e.Pointer == tpl.Pointer) { P($"{tn}.{f.Name}[{i}] -> template {tpl.name}"); continue; }
                            if (tplApps.Contains(e.Pointer)) { P($"{tn}.{f.Name}[{i}] -> template appearance"); continue; }
                            if (!scene) continue;
                            refs++;
                            Transform et2 = null;
                            var eg = e.TryCast<GameObject>();
                            if (eg != null) et2 = eg.transform; else { var ec = e.TryCast<Component>(); if (ec != null) et2 = ec.transform; }
                            foreach (var (pn, root) in roots)
                                if (LargoParity.PathIn(et2, root) != null) { P($"{tn}.{f.Name}[{i}] points into {pn}'s prefab"); break; }
                        }
                        continue;
                    }
                    if (!LargoParity.IsSceneRef(f)) continue;
                    refs++;
                    Transform t = null;
                    var g = v.TryCast<GameObject>();
                    if (g != null) t = g.transform; else { var c = v.TryCast<Component>(); if (c != null) t = c.transform; }
                    foreach (var (pn, root) in roots)
                        if (LargoParity.PathIn(t, root) != null) { P($"{tn}.{f.Name} points into {pn}'s prefab"); break; }
                }
            }
            var comps = VanillaSlimes.RootComponents(go);
            foreach (var c in VanillaSlimes.CoreComponents) if (!comps.Contains(c)) P($"missing component {c} (every vanilla largo has it)");
            foreach (var p in new[] { one, two })
                if (p != null)
                    foreach (var c in VanillaSlimes.ParentComponents(p.name))
                        if (!comps.Contains(c) && !LargoParity.SkipComponents.Any(c.Contains)) P($"missing component {c} (every vanilla {p.name} largo has it)");
            if (tpl?.BaseSlimes != null)
                foreach (var tp in tpl.BaseSlimes)
                {
                    if (tp == null || Same(tp, one) || Same(tp, two)) continue;
                    foreach (var c in VanillaSlimes.ParentComponents(tp.name))
                        if (comps.Contains(c) && !RootHas(one, c) && !RootHas(two, c)) Nt($"component {c} comes from the template's parent {tp.name}");
                }
            Nt($"{comps.Count} root components, {refs} object references checked");
        }

        static bool RootHas(SlimeDefinition d, string comp) => d != null && d.prefab != null && VanillaSlimes.RootComponents(d.prefab).Contains(comp);

        // ------------------------------------------------------------------ all

        /// <summary>Audits every generated largo; returns the report text (problems only unless <paramref name="notes"/>).</summary>
        public static string All(bool notes, out int total, out int bad)
        {
            var sb = new StringBuilder();
            total = 0; bad = 0;
            var counts = new Dictionary<string, int>();
            var reports = new List<Report>();
            foreach (var l in Generated())
            {
                var r = Check(l);
                reports.Add(r);
                total++;
                if (!r.Ok) bad++;
                foreach (var p in r.Problems)
                {
                    var key = Generic(p, l);
                    counts[key] = counts.TryGetValue(key, out var k) ? k + 1 : 1;
                }
            }
            sb.AppendLine($"largo audit: {total} generated largos, {bad} with problems; icons made {SlimeIcons.Made}, failed {SlimeIcons.Failed}, pending {SlimeIcons.Pending}");
            if (counts.Count > 0)
            {
                sb.AppendLine("most common problems:");
                foreach (var kv in counts.OrderByDescending(k => k.Value).Take(25)) sb.AppendLine($"  {kv.Value,4} x {kv.Key}");
            }
            sb.AppendLine();
            foreach (var r in reports) if (notes || !r.Ok) sb.Append(r.Text(notes));
            return sb.ToString();
        }

        static string Generic(string problem, SlimeDefinition l)
        {
            var s = problem;
            var b = l.BaseSlimes;
            if (b != null) foreach (var p in b) if (p != null) s = s.Replace(p.name, "<parent>");
            return s.Replace(l.name, "<largo>");
        }

        static int _waits;

        internal static void Auto()
        {
            // the icons are made in the background: wait for them (up to 30 s more)
            if (SlimeIcons.Enabled && SlimeIcons.Pending > 0 && _waits++ < 30) { Scheduler.Seconds(1f, () => Safe.Run("largo audit", Auto), "largo audit"); return; }
            var text = All(true, out int total, out int bad);
            var path = Diag.Write("largo_audit.txt", text);
            ForgeLog.Msg($"[largo audit] {total} generated largos, {bad} with problems -> {path}");
        }

        // ------------------------------------------------------------------ self test

        public sealed class TestRow
        {
            public string Largo, A, B, BestMode, Missing, Extra, Error;
            public int BodyVanilla = -1, BodyEngine = -1, BodyHeuristic = -1, FaceVanilla = -1, FaceEngine = -1;
            public float DeltaE, BestDelta, Parts;
        }

        static float Dist(Color a, Color b) => (ColorMix.ToOklab(a) - ColorMix.ToOklab(b)).magnitude;
        static float Dist(LargoPalette a, LargoPalette b) => (Dist(a.Top, b.Top) + Dist(a.Middle, b.Middle) + Dist(a.Bottom, b.Bottom)) / 3f;

        static string BestMode(LargoPalette p0, LargoPalette p1, LargoPalette target, out float best)
        {
            var cands = new List<(string, LargoPalette)> { ("First", p0), ("Second", p1) };
            for (int i = 1; i <= 9; i++) cands.Add(("Blend " + (i / 10f).ToString("0.0", CultureInfo.InvariantCulture), LargoPalette.Mix(p0, p1, i / 10f)));
            for (int dom = 0; dom < 2; dom++)
            {
                var d = dom == 0 ? p0 : p1; var o = dom == 0 ? p1 : p0;
                cands.Add(("Split " + (dom == 0 ? "first" : "second"), new LargoPalette(ColorMix.Mix(d.Top, o.Top, 0.25f), ColorMix.Mix(d.Middle, o.Middle, 0.5f), ColorMix.Mix(d.Bottom, o.Bottom, 0.75f), d.Ammo)));
                cands.Add(("Dominant " + (dom == 0 ? "first" : "second"), LargoPalette.Mix(d, o, LargoBlend.DominantHint)));
            }
            string name = "?"; best = float.MaxValue;
            foreach (var (n, p) in cands) { float e = Dist(p, target); if (e < best) { best = e; name = n; } }
            return name;
        }

        /// <summary>Rebuilds every vanilla largo from its vanilla parents (dry: nothing is created or changed) and compares.</summary>
        public static List<TestRow> SelfTest()
        {
            VanillaSlimes.Learn();
            var rows = new List<TestRow>();
            foreach (var l in VanillaSlimes.Largos)
            {
                var a = l.BaseSlimes[0]; var b = l.BaseSlimes[1];
                var row = new TestRow { Largo = l.name, A = a.name, B = b.name };
                rows.Add(row);
                try
                {
                    var la = Default(l); var aa = Default(a); var ba = Default(b);
                    row.BodyVanilla = VanillaSlimes.BodyOwner(la, aa, ba, a, b);
                    row.FaceVanilla = VanillaSlimes.FaceOwner(la, aa, ba);
                    var c = LargoBlend.Preview(a, b, Library.LargoSettings.Auto, true);
                    row.BodyEngine = c.BodySide; row.FaceEngine = c.FaceSide;
                    bool keep = LargoBlend.LearnFromVanilla;
                    LargoBlend.LearnFromVanilla = false;
                    try { row.BodyHeuristic = LargoBlend.Preview(a, b).BodySide; } finally { LargoBlend.LearnFromVanilla = keep; }
                    var vp = LargoBlend.PaletteOf(la);
                    row.DeltaE = Dist(c.Palette, vp);
                    row.BestMode = BestMode(c.FirstPalette, c.SecondPalette, vp, out row.BestDelta);
                    var have = new HashSet<string>(c.Structures.Where(s => s?.Element != null).Select(s => s.Element.Name ?? ""), StringComparer.OrdinalIgnoreCase);
                    var want = new HashSet<string>(Structures(la).Select(s => s.Element.Name ?? ""), StringComparer.OrdinalIgnoreCase);
                    int inter = have.Count(want.Contains), uni = have.Count + want.Count - inter;
                    row.Parts = uni > 0 ? inter / (float)uni : 1f;
                    row.Missing = string.Join(",", want.Except(have, StringComparer.OrdinalIgnoreCase));
                    row.Extra = string.Join(",", have.Except(want, StringComparer.OrdinalIgnoreCase));
                }
                catch (Exception e) { row.Error = e.GetType().Name + ": " + e.Message; }
            }
            return rows;
        }

        public static string SelfTestText()
        {
            var rows = SelfTest();
            var sb = new StringBuilder();
            int bodyKnown = rows.Count(r => r.BodyVanilla >= 0), bodyOk = rows.Count(r => r.BodyVanilla >= 0 && r.BodyEngine == r.BodyVanilla);
            int heurOk = rows.Count(r => r.BodyVanilla >= 0 && r.BodyHeuristic == r.BodyVanilla);
            int faceKnown = rows.Count(r => r.FaceVanilla >= 0), faceOk = rows.Count(r => r.FaceVanilla >= 0 && r.FaceEngine == r.FaceVanilla);
            var ok = rows.Where(r => r.Error == null).ToList();
            sb.AppendLine($"self test: {rows.Count} vanilla largos rebuilt from their parents (dry run), {rows.Count - ok.Count} errors");
            sb.AppendLine($"body owner as vanilla: {bodyOk}/{bodyKnown} (heuristics only {heurOk}/{bodyKnown}); face owner: {faceOk}/{faceKnown}");
            if (ok.Count > 0)
            {
                sb.AppendLine($"palette distance (OKLab, 0 = same): engine {ok.Average(r => r.DeltaE):0.000}, best simple mode {ok.Average(r => r.BestDelta):0.000}; parts overlap {ok.Average(r => r.Parts) * 100f:0}%");
                sb.AppendLine($"vanilla colours: {VanillaSlimes.ColorExact} largos have one parent's palette, {VanillaSlimes.ColorOther} neither; engine takes one parent's: {VanillaSlimes.OneParentColors && LargoBlend.LearnFromVanilla}");
                sb.AppendLine("note: the rules are learned from these same largos (in-sample check that the engine applies them, not a prediction test)");
                sb.AppendLine("best fitting colour mode of vanilla largos: " + string.Join(", ", ok.GroupBy(r => r.BestMode).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count())));
            }
            sb.AppendLine();
            string side(int s, TestRow r) => s < 0 ? "?" : s == 0 ? r.A : r.B;
            foreach (var r in rows)
            {
                if (r.Error != null) { sb.AppendLine($"{r.Largo}: ERROR {r.Error}"); continue; }
                sb.AppendLine($"{r.Largo} ({r.A}+{r.B}): body vanilla {side(r.BodyVanilla, r)} / engine {side(r.BodyEngine, r)}{(r.BodyVanilla >= 0 && r.BodyVanilla != r.BodyEngine ? " MISMATCH" : "")}, " +
                              $"face {side(r.FaceVanilla, r)} / {side(r.FaceEngine, r)}{(r.FaceVanilla >= 0 && r.FaceVanilla != r.FaceEngine ? " MISMATCH" : "")}, " +
                              $"dE {r.DeltaE:0.000} (best {r.BestMode} {r.BestDelta:0.000}), parts {r.Parts * 100f:0}%" +
                              (r.Missing.Length > 0 ? " missing [" + r.Missing + "]" : "") + (r.Extra.Length > 0 ? " extra [" + r.Extra + "]" : ""));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ dev console

        internal static void RegisterCommands()
        {
            DevConsole.Register("largoaudit", "largoaudit [<largo> | all | vanilla | learn] - check generated largos against vanilla ones (files in UserData/CottonLibrary)", a =>
            {
                var x = DevConsole.Arg(a, 1);
                if (x == null || x.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    var text = All(x != null, out int total, out int bad);
                    var path = Diag.Write("largo_audit.txt", text);
                    foreach (var line in text.Split('\n').Take(30)) DevConsole.Print(line.TrimEnd());
                    DevConsole.Print($"{total} largos, {bad} with problems -> {path}");
                    return;
                }
                if (x.Equals("vanilla", StringComparison.OrdinalIgnoreCase))
                {
                    var text = SelfTestText();
                    var path = Diag.Write("largo_selftest.txt", text);
                    foreach (var line in text.Split('\n').Take(5)) DevConsole.Print(line.TrimEnd());
                    DevConsole.Print("-> " + path);
                    return;
                }
                if (x.Equals("learn", StringComparison.OrdinalIgnoreCase))
                {
                    VanillaSlimes.Relearn();
                    var text = VanillaSlimes.Describe();
                    var path = Diag.Write("largo_vanilla.txt", text);
                    foreach (var line in text.Split('\n').Take(12)) DevConsole.Print(line.TrimEnd());
                    DevConsole.Print("-> " + path);
                    return;
                }
                var l = Library.GetLargo(x);
                if (l == null) { DevConsole.Print("unknown largo " + x); return; }
                foreach (var line in Check(l).Text(true).Split('\n')) if (line.Length > 0) DevConsole.Print(line.TrimEnd());
            });
        }
    }
}
