using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.Slime.SquashWobble;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Mesh tools for slime looks: inflated / faceted copies of a body mesh, extra skinned layers on the slime's own bones
    /// (they squash and wobble with it), and baking a pattern defined in 3D onto a mesh's UV layout.
    /// </summary>
    public static class MeshKit
    {
        static readonly Dictionary<string, Mesh> _inflated = new();

        /// <summary>
        /// Copy of <paramref name="src"/> pushed out by <paramref name="d"/> along its normals (cached). <paramref name="facets"/>
        /// &gt; 0 snaps the normals towards that many directions (gem / crystal look). The mesh must be readable.
        /// </summary>
        public static Mesh Inflated(Mesh src, float d, int facets = 0)
        {
            if (src == null) return null;
            string k = src.GetInstanceID() + "|" + d + "|" + facets;
            if (_inflated.TryGetValue(k, out var have) && have != null) return have;
            var m = Object.Instantiate(src);
            m.name = src.name + "_inflated";
            Vector3[] v = m.vertices, n = m.normals;
            if (n != null && n.Length == v.Length)
            {
                for (int i = 0; i < v.Length; i++) v[i] = v[i] + n[i] * d;
                m.vertices = v;
                if (facets > 0) m.normals = Facet(n, facets);
                m.RecalculateBounds();
            }
            else ForgeLog.WarnOnce("inflate:" + src.name, $"MeshKit.Inflated: {src.name} has no normals (not readable?)");
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _inflated[k] = m;
            return m;
        }

        /// <summary>Normals bent towards the nearest of <paramref name="k"/> evenly spread directions (Fibonacci sphere).</summary>
        public static Vector3[] Facet(Vector3[] n, int k)
        {
            var dirs = new Vector3[k];
            float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < k; i++)
            {
                float y = 1f - (i + 0.5f) / k * 2f, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), a = ga * i;
                dirs[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            var o = new Vector3[n.Length];
            for (int i = 0; i < n.Length; i++)
            {
                float best = -2f; var bd = n[i];
                foreach (var d in dirs) { float dot = Vector3.Dot(n[i], d); if (dot > best) { best = dot; bd = d; } }
                o[i] = Vector3.Slerp(n[i], bd, 0.85f).normalized;
            }
            return o;
        }

        /// <summary>
        /// Body renderer of a slime actor: the skinned renderer using the definition's body material, else the one named like
        /// a body, else the one with the most vertices. Renderers made by <see cref="AddLayer"/> are skipped.
        /// </summary>
        public static SkinnedMeshRenderer BodyRenderer(GameObject slime)
        {
            if (slime == null) return null;
            Material body = null;
            try
            {
                var d = World.IdentOf(slime)?.TryCast<SlimeDefinition>();
                if (d != null && d.AppearancesDefault != null && d.AppearancesDefault.Length > 0) body = Looks.BodyMaterial(d.AppearancesDefault[0]);
            }
            catch { }
            SkinnedMeshRenderer named = null, biggest = null;
            int most = -1;
            foreach (var r in slime.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r == null || r.gameObject.name.StartsWith(LayerPrefix, StringComparison.Ordinal)) continue;
                var m = r.sharedMaterial;
                if (body != null && m != null && (m == body || m.name.StartsWith(body.name, StringComparison.Ordinal))) return r;
                if (named == null && r.gameObject.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0) named = r;
                int vc = r.sharedMesh != null ? r.sharedMesh.vertexCount : 0;
                if (vc > most) { most = vc; biggest = r; }
            }
            return named ?? biggest;
        }

        /// <summary>Name prefix of the objects made by <see cref="AddLayer"/>.</summary>
        public const string LayerPrefix = "ForgeLayer_";

        /// <summary>
        /// Adds an extra skinned layer around a slime's body (glass shell, coat, glow): the body mesh inflated by
        /// <paramref name="inflate"/> on the same bones, drawn with <paramref name="mat"/>. It is registered with SR2's
        /// squash/wobble, so it deforms with the body when the shader supports it. An existing layer with the same name is
        /// replaced; <paramref name="mat"/> null just removes it. Returns the renderer or null.
        /// </summary>
        public static SkinnedMeshRenderer AddLayer(GameObject slime, string name, Material mat, float inflate = 0.02f, int facets = 0, Mesh mesh = null)
        {
            if (slime == null) return null;
            string goName = LayerPrefix + name;
            foreach (var t in slime.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == goName) Object.Destroy(t.gameObject);
            if (mat == null) return null;
            var body = BodyRenderer(slime);
            if (body == null || (body.sharedMesh == null && mesh == null)) return null;
            var sh = new GameObject(goName);
            sh.layer = body.gameObject.layer;
            sh.transform.SetParent(body.transform.parent, false);
            sh.transform.localPosition = body.transform.localPosition;
            sh.transform.localRotation = body.transform.localRotation;
            sh.transform.localScale = body.transform.localScale;
            var smr = sh.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh ?? (Mathf.Abs(inflate) > 1e-5f || facets > 0 ? Inflated(body.sharedMesh, inflate, facets) : body.sharedMesh);
            smr.rootBone = body.rootBone;
            smr.bones = body.bones;
            smr.sharedMaterials = new Il2CppReferenceArray<Material>(new[] { mat });
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.localBounds = body.localBounds;
            smr.updateWhenOffscreen = true;
            try { var mpb = new MaterialPropertyBlock(); body.GetPropertyBlock(mpb); smr.SetPropertyBlock(mpb); } catch { }
            var sw = slime.GetComponentInChildren<SquashWobbleViaShader>();
            if (sw != null)
            {
                try
                {
                    if (sw.renderers != null && !sw.renderers.Contains(smr)) sw.renderers.Add(smr);
                    sw.SetGlobalArrayIndex(sw.slimeGlobalArrayIndex);
                }
                catch (Exception e) { ForgeLog.WarnOnce("layer:squash", "MeshKit.AddLayer squash/wobble: " + e.Message); }
            }
            return smr;
        }

        /// <summary>Ray / triangle intersection (Möller–Trumbore); <paramref name="t"/> = distance along <paramref name="d"/>.</summary>
        public static bool RayTri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            var e1 = b - a; var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-8f) return false;
            float inv = 1f / det;
            var s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        // ------------------------------------------------------------------ surface baking
        /// <summary>Statistics of the last <see cref="BakeSurface"/> call (mesh, UV set, texels, overlaps).</summary>
        public static string LastBakeInfo { get; private set; } = "";
        /// <summary>UV set used by the last <see cref="BakeSurface"/> call (0 = uv, 1 = uv2), for <c>_ColorMaskUV1</c>-style switches.</summary>
        public static int LastBakeUvSet { get; private set; }

        /// <summary>
        /// Paints a pattern defined in the mesh's own 3D space onto its UV layout: every texel is mapped back to the surface
        /// point it covers (triangles rasterised in UV space) and gets <paramref name="pattern"/>(point, normal). So a spiral,
        /// stripes or spots look continuous on the body whatever its UV islands and seams are. Islands are grown a few texels
        /// against seam bleeding. <paramref name="uvSet"/> -1 picks the UV set with fewer overlaps. Returns null when the mesh is
        /// not readable. Takes ~0.1-0.5 s for 512², so bake once and keep the texture.
        /// </summary>
        public static Texture2D BakeSurface(Mesh mesh, int size, Func<Vector3, Vector3, Color> pattern, int uvSet = -1, bool linear = true, string name = null)
        {
            if (mesh == null || pattern == null) { LastBakeInfo = "no mesh"; return null; }
            if (!mesh.isReadable) { LastBakeInfo = mesh.name + " not readable"; return null; }
            var verts = mesh.vertices;
            var norms = mesh.normals;
            var tris = mesh.triangles;
            var uv0 = mesh.uv; var uv1 = mesh.uv2;
            float o0 = OverlapFraction(uv0, verts, tris), o1 = OverlapFraction(uv1, verts, tris);
            int set = uvSet >= 0 ? uvSet : (o1 < 1f && o1 < o0 * 0.5f ? 1 : 0);
            var uv = set == 1 ? uv1 : uv0;
            if (uv == null || uv.Length != verts.Length) { LastBakeInfo = "no uv" + set; return null; }
            bool hasN = norms != null && norms.Length == verts.Length;
            int n = size;
            var pos = new Vector3[n * n]; var nor = new Vector3[n * n]; var has = new bool[n * n];
            int ov = Raster(uv, verts, hasN ? norms : null, tris, n, pos, nor, has, out int written);
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++) if (has[i]) px[i] = pattern(pos[i], nor[i]);
            Dilate(px, has, n, 4);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, linear) { name = name ?? "Forge_baked_" + mesh.name };
            tex.SetPixels(px);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            LastBakeUvSet = set;
            LastBakeInfo = $"{mesh.name} v={verts.Length} t={tris.Length / 3} overlap uv0={o0:P1} uv1={(o1 > 1f ? "-" : o1.ToString("P1"))} -> uv{set}, texels {written} (+{ov} overlapping)";
            ForgeLog.Debug("[bake] " + LastBakeInfo);
            return tex;
        }

        static int Raster(Vector2[] uv, Vector3[] v, Vector3[] nrm, int[] tris, int n, Vector3[] pos, Vector3[] nor, bool[] has, out int written)
        {
            int overlaps = 0; written = 0;
            const float eps = 0.02f;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                Vector2 a = uv[i0] * n, b = uv[i1] * n, c = uv[i2] * n;
                float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area) < 1e-6f) continue;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, n - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, n - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, n - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, n - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float wa = ((b.x - px) * (c.y - py) - (b.y - py) * (c.x - px)) / area;
                        float wb = ((c.x - px) * (a.y - py) - (c.y - py) * (a.x - px)) / area;
                        float wc = 1f - wa - wb;
                        if (wa < -eps || wb < -eps || wc < -eps) continue;
                        var p = v[i0] * wa + v[i1] * wb + v[i2] * wc;
                        int idx = y * n + x;
                        if (has[idx]) { if ((p - pos[idx]).sqrMagnitude > 0.08f * 0.08f) overlaps++; continue; }
                        has[idx] = true; pos[idx] = p; written++;
                        if (nor != null) nor[idx] = nrm != null ? (nrm[i0] * wa + nrm[i1] * wb + nrm[i2] * wc).normalized : Vector3.up;
                    }
            }
            return overlaps;
        }

        static float OverlapFraction(Vector2[] uv, Vector3[] v, int[] tris)
        {
            if (uv == null || uv.Length != v.Length) return 2f;
            const int n = 128;
            var pos = new Vector3[n * n]; var has = new bool[n * n];
            int ov = Raster(uv, v, null, tris, n, pos, null, has, out int written);
            return written == 0 ? 2f : ov / (float)written;
        }

        static void Dilate(Color[] px, bool[] has, int n, int passes)
        {
            var grown = (bool[])has.Clone();
            for (int pass = 0; pass < passes; pass++)
            {
                var next = (bool[])grown.Clone();
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int idx = y * n + x;
                        if (grown[idx]) continue;
                        Color sum = default; int cnt = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                                int j = yy * n + xx;
                                if (!grown[j]) continue;
                                sum += px[j]; cnt++;
                            }
                        if (cnt == 0) continue;
                        px[idx] = sum / cnt; next[idx] = true;
                    }
                grown = next;
            }
        }
    }
}
