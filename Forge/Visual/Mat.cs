using System;
using System.Collections.Generic;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Materials: lookup by name, cloning, property helpers and the "keeper".
    /// <para>SR1 shaders do not exist in SR2 (HDRP), every look is rebuilt from SR2 materials: <see cref="Clone"/> one and
    /// change its properties. Never edit a stock material in place (it is shared by every slime of that kind).</para>
    /// <para>Keeper: clones of materials that live in Addressables bundles turn magenta when the bundle is unloaded on a region
    /// change. Every material made by <see cref="Clone"/>/<see cref="CloneByShader"/> (or passed to <see cref="Track"/>) is
    /// snapshotted while healthy and re-bound to a live shader/texture with the same name when the original dies.</para>
    /// <para>Shader features are switched by a keyword AND a float property: change both (<see cref="Kw"/> + <see cref="Flt"/>).
    /// Assigning <c>material.shader</c> resets <c>renderQueue</c> to 2000 (faces need 2450): restore it.</para>
    /// </summary>
    public static partial class Mat
    {
        static Dictionary<string, Material> _cache;

        static bool Healthy(Shader sh) => sh != null && sh.isSupported && !sh.name.StartsWith("Hidden/InternalError");

        /// <summary>A loaded stock material by exact name (prefers a copy whose shader is alive), or null.</summary>
        public static Material Find(string name)
        {
            if (_cache == null || !_cache.TryGetValue(name, out var m) || m == null) Index();
            return _cache.TryGetValue(name, out var r) && r != null ? r : null;
        }

        static void Index()
        {
            _cache = new Dictionary<string, Material>();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || IsMade(m)) continue;
                if (!_cache.TryGetValue(m.name, out var have) || (!Healthy(have.shader) && Healthy(m.shader))) _cache[m.name] = m;
            }
        }

        /// <summary>Copy of the first found template (by name, in order). Tracked by the keeper, survives unloading.</summary>
        public static Material Clone(string newName, params string[] templates)
        {
            foreach (var t in templates)
            {
                var src = Find(t);
                if (src == null) continue;
                return Copy(src, newName);
            }
            ForgeLog.Warn($"[mat] no template material for {newName} ({string.Join(",", templates)})");
            return null;
        }

        /// <summary>Copy of a given material, tracked by the keeper.</summary>
        public static Material Copy(Material src, string newName = null)
        {
            if (src == null) return null;
            var m = new Material(src) { name = newName ?? src.name + "_copy", hideFlags = HideFlags.DontUnloadUnusedAsset };
            return Track(m);
        }

        /// <summary>Copy of the first loaded material that uses the given shader.</summary>
        public static Material CloneByShader(string newName, string shaderName)
        {
            foreach (var src in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (src == null || IsMade(src)) continue;
                var sh = src.shader;
                if (!Healthy(sh) || sh.name != shaderName) continue;
                return Copy(src, newName);
            }
            ForgeLog.Warn($"[mat] no material with shader {shaderName} for {newName}");
            return null;
        }

        // ------------------------------------------------------------------ property helpers (no-ops for missing properties)
        public static void Col(this Material m, string p, Color c) { if (m != null && m.HasProperty(p)) m.SetColor(p, c); }
        public static void Flt(this Material m, string p, float v) { if (m != null && m.HasProperty(p)) m.SetFloat(p, v); }
        public static void Tx(this Material m, string p, Texture t) { if (m != null && t != null && m.HasProperty(p)) m.SetTexture(p, t); }
        public static void Vec(this Material m, string p, Vector4 v) { if (m != null && m.HasProperty(p)) m.SetVector(p, v); }
        public static void Kw(this Material m, string kw, bool on) { if (m == null) return; if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw); }
        public static Color GetCol(this Material m, string p, Color fallback = default) => m != null && m.HasProperty(p) ? m.GetColor(p) : fallback;
        public static float GetFlt(this Material m, string p, float fallback = 0f) => m != null && m.HasProperty(p) ? m.GetFloat(p) : fallback;

        /// <summary>Slime body gradient (<c>_TopColor/_MiddleColor/_BottomColor</c>).</summary>
        public static void Palette(this Material m, Color top, Color mid, Color bot) { m.Col("_TopColor", top); m.Col("_MiddleColor", mid); m.Col("_BottomColor", bot); }

        /// <summary>Twin effect gradient.</summary>
        public static void TwinPalette(this Material m, Color top, Color mid, Color bot) { m.Col("_TwinTopColor", top); m.Col("_TwinMiddleColor", mid); m.Col("_TwinBottomColor", bot); }

        /// <summary>Changes the shader and keeps the render queue (assigning a shader resets it to 2000).</summary>
        public static void SetShaderKeepQueue(this Material m, Shader sh)
        {
            if (m == null || sh == null) return;
            int q = m.renderQueue;
            m.shader = sh;
            m.renderQueue = q;
        }

        public static Color Mul(this Color c, float f) => new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);
        public static Color HDR(this Color c, float intensity) => new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a);
        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Colour from 0-255 components (SR1 palettes are often noted that way).</summary>
        public static Color32 Rgb(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        /// <summary>Colour from 0-1 components (SR1 SlimeAppearance values).</summary>
        public static Color32 Rgb(float r, float g, float b) =>
            new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255), 255);

        /// <summary>Colour from "#RRGGBB" / "RRGGBB".</summary>
        public static Color32 Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c)) return c;
            ForgeLog.Warn("[mat] bad colour " + hex);
            return new Color32(255, 0, 255, 255);
        }

        /// <summary>Small horizontal colour ramp (replaces rainbow lookup textures of radiant/aura shaders).</summary>
        public static Texture2D Ramp(string name, params Color[] stops)
        {
            const int W = 64;
            var t = new Texture2D(W, 4, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            for (int x = 0; x < W; x++)
            {
                float f = stops.Length < 2 ? 0f : x / (float)(W - 1) * (stops.Length - 1);
                int i = Mathf.Min((int)f, Math.Max(0, stops.Length - 2));
                var c = stops.Length < 2 ? stops[0] : Color.Lerp(stops[i], stops[i + 1], f - i);
                for (int y = 0; y < 4; y++) t.SetPixel(x, y, c);
            }
            t.Apply(false, true);
            return t;
        }

        /// <summary>Removes the rainbow from radiant-style FX materials by giving them a single-hue ramp.</summary>
        public static void Tint(this Material m, string name, params Color[] stops)
        {
            if (m == null) return;
            var r = Ramp(name, stops);
            m.Tx("_RainbowAura", r);
            m.Tx("_RadiantGradient", r);
            m.Flt("_RainbowSaturation", 0f);
        }

        // ------------------------------------------------------------------ keeper
        sealed class Snap { public string Shader; public string[] Kw; public readonly Dictionary<string, string> Tex = new(); }
        static readonly List<Material> _made = new();
        static readonly HashSet<IntPtr> _madeSet = new();
        static readonly Dictionary<IntPtr, Snap> _snaps = new();
        static readonly Dictionary<IntPtr, string> _names = new();
        static float _nextCheck;

        /// <summary>Tracked materials destroyed by the game / re-bound by the keeper (diagnostics).</summary>
        public static int Destroyed, Revived;

        /// <summary>Puts a material under the keeper's protection.</summary>
        public static Material Track(Material m)
        {
            if (m != null && _madeSet.Add(m.Pointer)) { _made.Add(m); _names[m.Pointer] = m.name; }
            return m;
        }

        public static IEnumerable<Material> Made => _made;
        public static bool IsMade(Material m) => m != null && _madeSet.Contains(m.Pointer);

        static Snap Snapshot(Material m)
        {
            var s = new Snap { Shader = m.shader.name };
            var kw = m.shaderKeywords;
            if (kw != null) { s.Kw = new string[kw.Length]; for (int i = 0; i < kw.Length; i++) s.Kw[i] = kw[i]; }
            try
            {
                foreach (var p in m.GetTexturePropertyNames())
                {
                    var t = m.GetTexture(p);
                    if (t != null && !string.IsNullOrEmpty(t.name)) s.Tex[p] = t.name;
                }
            }
            catch { }
            return s;
        }

        static Dictionary<string, Shader> LiveShaders()
        {
            var d = new Dictionary<string, Shader>();
            foreach (var sh in Resources.FindObjectsOfTypeAll<Shader>())
                if (Healthy(sh) && !d.ContainsKey(sh.name)) d[sh.name] = sh;
            return d;
        }

        static Dictionary<string, Texture> LiveTextures()
        {
            var d = new Dictionary<string, Texture>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture>())
                if (t != null && !string.IsNullOrEmpty(t.name) && !d.ContainsKey(t.name)) d[t.name] = t;
            return d;
        }

        /// <summary>Called by Forge every frame; checks the tracked materials every 2 s (<paramref name="force"/>: now).</summary>
        public static void Tick(bool force = false)
        {
            if (_made.Count == 0 || (!force && Time.unscaledTime < _nextCheck)) return;
            _nextCheck = Time.unscaledTime + 2f;
            try { Check(); } catch (Exception e) { ForgeLog.WarnOnce("mat.tick", "[mat] keeper: " + e); }
        }

        static void Check()
        {
            Dictionary<string, Shader> shaders = null;
            Dictionary<string, Texture> texes = null;
            for (int i = _made.Count - 1; i >= 0; i--)
            {
                var m = _made[i];
                if (m == null)
                {
                    _made.RemoveAt(i); Destroyed++;
                    _madeSet.Remove(m.Pointer);
                    _names.TryGetValue(m.Pointer, out var dn);
                    ForgeLog.Debug($"[mat] tracked material destroyed by the game: {dn}");
                    continue;
                }
                _snaps.TryGetValue(m.Pointer, out var s);
                var sh = m.shader;
                if (Healthy(sh))
                {
                    if (s == null || s.Shader != sh.name) { _snaps[m.Pointer] = Snapshot(m); continue; } // new or changed on purpose
                    foreach (var kv in s.Tex)
                    {
                        if (m.GetTexture(kv.Key) != null) continue;
                        texes ??= LiveTextures();
                        if (texes.TryGetValue(kv.Value, out var nt)) { m.SetTexture(kv.Key, nt); Revived++; ForgeLog.Debug($"[mat] re-bound texture {kv.Value} on {m.name}.{kv.Key}"); }
                    }
                    continue;
                }
                if (s == null) continue;
                shaders ??= LiveShaders();
                if (shaders.TryGetValue(s.Shader, out var ns))
                {
                    int q = m.renderQueue;
                    m.shader = ns;
                    m.renderQueue = q;
                    if (s.Kw != null) m.shaderKeywords = s.Kw;
                    Revived++;
                    ForgeLog.Debug($"[mat] re-bound shader {s.Shader} on {m.name}");
                }
            }
        }
    }
}
