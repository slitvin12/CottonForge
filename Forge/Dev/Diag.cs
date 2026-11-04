using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Text dumps for porting looks and debugging content: object hierarchies with components, slime definitions with
    /// diet / appearance structures / meshes / materials. Files go to <c>UserData\CottonLibrary</c>.
    /// </summary>
    public static class Diag
    {
        static string N(UnityEngine.Object o) => o == null ? "null" : o.name;
        static string TN(Il2CppSystem.Object o) { try { return o == null ? "null" : o.GetIl2CppType().Name; } catch { return "?"; } }
        static string C(Color c) => string.Format(CultureInfo.InvariantCulture, "({0:0.###},{1:0.###},{2:0.###},{3:0.###})", c.r, c.g, c.b, c.a);
        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.###},{1:0.###},{2:0.###})", v.x, v.y, v.z);
        static string J<T>(Il2CppReferenceArray<T> arr) where T : UnityEngine.Object => arr == null ? "" : string.Join(",", arr.Select(x => N(x)));

        /// <summary>Writes <paramref name="text"/> to <c>UserData\CottonLibrary\<paramref name="file"/></c> and returns the full path.</summary>
        public static string Write(string file, string text)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) file = file.Replace(ch, '_');
            var path = Path.Combine(DevConsole.Dir, file);
            File.WriteAllText(path, text);
            return path;
        }

        /// <summary>
        /// Transform tree with active state, layer, local position/scale and components (with key values of colliders,
        /// rigidbodies, lights, audio and damage sources when <paramref name="details"/>).
        /// </summary>
        public static string Hierarchy(Transform root, bool details = true, int maxDepth = 8)
        {
            var sb = new StringBuilder();
            if (root != null) Tree(sb, root, "", 0, details, maxDepth);
            return sb.ToString();
        }

        static void Tree(StringBuilder sb, Transform t, string indent, int depth, bool details, int maxDepth)
        {
            var cn = new List<string>();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string s = TN(c);
                if (details)
                {
                    try
                    {
                        var col = c.TryCast<SphereCollider>(); if (col != null) s += $"(r={col.radius},trig={col.isTrigger},c={V(col.center)})";
                        var rb = c.TryCast<Rigidbody>(); if (rb != null) s += $"(mass={rb.mass},drag={rb.drag},kin={rb.isKinematic})";
                        var l = c.TryCast<Light>(); if (l != null) s += $"(type={l.type},range={l.range},int={l.intensity},col={C(l.color)})";
                        var au = c.TryCast<AudioSource>(); if (au != null) s += $"(mixer={N(au.outputAudioMixerGroup)},clip={N(au.clip)},spatial={au.spatialBlend})";
                        var r = c.TryCast<Renderer>(); if (r != null) s += $"(en={r.enabled},mats=[{string.Join(",", r.sharedMaterials.Select(m => N(m)))}])";
                        var dm = c.TryCast<Il2CppMonomiPark.SlimeRancher.Damage.Damager>(); if (dm != null) s += $"(amt={dm.DamageAmount},src={N(dm.DamageSource)})";
                    }
                    catch { }
                }
                cn.Add(s);
            }
            sb.AppendLine($"{indent}{t.name} act={t.gameObject.activeSelf} layer={t.gameObject.layer} pos={V(t.localPosition)} scl={V(t.localScale)} [{string.Join(", ", cn)}]");
            if (depth >= maxDepth) { if (t.childCount > 0) sb.AppendLine($"{indent}  ({t.childCount} children)"); return; }
            for (int i = 0; i < t.childCount; i++) Tree(sb, t.GetChild(i), indent + "  ", depth + 1, details, maxDepth);
        }

        /// <summary>
        /// A slime definition: flags, diet, toys, modules, sounds, and for every appearance its palette, face, structures with
        /// elements, materials (+ shaders), appearance objects with bones, meshes and renderers.
        /// </summary>
        public static string Slime(SlimeDefinition d)
        {
            var sb = new StringBuilder();
            if (d == null) return "null definition";
            sb.AppendLine($"# {d.name} largo={d.IsLargo} canLargo={d.CanLargofy} scale={d.PrefabScale} prefab={N(d.prefab)} ref={d.referenceId} color={C(d.color)}");
            var di = d.Diet;
            if (di != null)
                sb.AppendLine($"  diet groups=[{J(di.MajorFoodIdentifiableTypeGroups)}] fav=[{J(di.FavoriteIdents)}] add=[{J(di.AdditionalFoodIdents)}] prod=[{J(di.ProduceIdents)}] favCount={di.FavoriteProductionCount} eatmap={di.EatMap?.Count}");
            sb.AppendLine($"  toys=[{J(d.FavoriteToyIdents)}] base={N(d.BaseModule)} modules=[{J(d.SlimeModules)}] sounds={N(d.Sounds)}");
            if (d.IsLargo && d.BaseSlimes != null) sb.AppendLine($"  bases=[{J(d.BaseSlimes)}]");
            if (d.AppearancesDefault == null) return sb.ToString();
            foreach (var app in d.AppearancesDefault)
            {
                if (app == null) continue;
                var p = app._colorPalette;
                sb.AppendLine($"  app {app.name} face={N(app._face)} splat={C(app._splatColor)} pal T{C(p.Top)} M{C(p.Middle)} B{C(p.Bottom)} A{C(p.Ammo)} icon={N(app._icon)}");
                if (app._structures == null) continue;
                foreach (var s in app._structures)
                {
                    if (s == null) { sb.AppendLine("   struct null"); continue; }
                    var e = s.Element;
                    var mats = new List<string>();
                    if (s.DefaultMaterials != null)
                        foreach (var m in s.DefaultMaterials) mats.Add(N(m) + "/" + (m != null && m.shader != null ? m.shader.name : "-"));
                    sb.AppendLine($"   struct el={N(e)} type={(e != null ? e.Type.ToString() : "-")} faces={s.SupportsFaces} mats=[{string.Join(", ", mats)}]");
                    if (e?.Prefabs == null) continue;
                    foreach (var po in e.Prefabs) AppearanceObject(sb, po);
                }
            }
            return sb.ToString();
        }

        static void AppearanceObject(StringBuilder sb, SlimeAppearanceObject po)
        {
            if (po == null) { sb.AppendLine("    sao null"); return; }
            var bones = new List<string>();
            if (po.AttachedBones != null) foreach (var b in po.AttachedBones) bones.Add(b.ToString());
            sb.AppendLine($"    sao {po.name} root={po.RootBone} parent={po.ParentBone} attached=[{string.Join(",", bones)}] lod={po.LODIndex} pos={V(po.transform.localPosition)} scale={V(po.transform.localScale)}");
            foreach (var r in po.GetComponentsInChildren<Renderer>(true))
            {
                var smr = r.TryCast<SkinnedMeshRenderer>();
                Mesh mesh = smr != null ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                string mi = mesh == null ? "nomesh" : $"{mesh.name} vc={mesh.vertexCount} sub={mesh.subMeshCount} readable={mesh.isReadable} bounds=c{V(mesh.bounds.center)} e{V(mesh.bounds.extents)}";
                sb.AppendLine($"     rend {TN(r)} {r.name} {mi} mats=[{string.Join(",", r.sharedMaterials.Select(m => N(m)))}]");
            }
        }
    }
}
