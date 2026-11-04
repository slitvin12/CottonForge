using System;
using System.Collections.Generic;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Visuals &amp; Shaders Factory: high-level material recipes over SR2's own shaders (cloned from stock materials, so they
    /// work with the game's lighting, faces and body colouring). Built-ins: <c>jelly</c>, <c>glass</c>, <c>prismatic</c>,
    /// <c>neon</c>, <c>metal</c>, <c>glitch</c>, <c>stone</c>; add recipes with <see cref="Register(IMaterialPreset)"/>.
    /// <code>var m = MaterialPresets.Build("neon", "MyNeonBody", new PresetColors(top, mid, bottom, glow));
    /// Looks.SetBodyMaterial(app, m);</code>
    /// Materials are cached per (preset, name) and protected by the material keeper (<see cref="Mat.Track"/>).
    /// </summary>
    public static class MaterialPresets
    {
        sealed class Preset : IMaterialPreset
        {
            readonly Func<string, PresetColors, Material> _f;
            public Preset(string id, Func<string, PresetColors, Material> f) { Id = id; _f = f; }
            public string Id { get; }
            public Material Build(string name, PresetColors c) => _f(name, c);
        }

        static readonly Dictionary<string, IMaterialPreset> _presets = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Material> _cache = new(StringComparer.Ordinal);
        static bool _builtins;

        static void Builtins()
        {
            if (_builtins) return;
            _builtins = true;
            Add("jelly", BuildJelly);
            Add("glass", BuildGlass);
            Add("prismatic", BuildPrismatic);
            Add("neon", BuildNeon);
            Add("metal", BuildMetal);
            Add("glitch", BuildGlitch);
            Add("stone", BuildStone);
        }

        static void Add(string id, Func<string, PresetColors, Material> f) { if (!_presets.ContainsKey(id)) _presets[id] = new Preset(id, f); }

        /// <summary>Adds or replaces a recipe.</summary>
        public static void Register(IMaterialPreset preset) { Builtins(); if (preset != null) _presets[preset.Id] = preset; }
        public static void Register(string id, Func<string, PresetColors, Material> build) => Register(new Preset(id, build));
        public static IMaterialPreset Get(string id) { Builtins(); return id != null && _presets.TryGetValue(id, out var p) ? p : null; }
        public static IEnumerable<string> Ids { get { Builtins(); return _presets.Keys; } }

        /// <summary>Material <paramref name="name"/> from preset <paramref name="id"/> (cached; <paramref name="fresh"/> = build a new one).</summary>
        public static Material Build(string id, string name, PresetColors colors, bool fresh = false)
        {
            var p = Get(id);
            if (p == null) { ForgeLog.Warn($"[presets] unknown preset '{id}' ({string.Join(", ", Ids)})"); return null; }
            string key = p.Id + "/" + name;
            if (!fresh && _cache.TryGetValue(key, out var have) && have != null) return have;
            Material m = null;
            try { m = p.Build(name, colors); }
            catch (Exception e) { ForgeLog.Warn($"[presets] {id} {name}: {e.Message}"); }
            if (m != null) _cache[key] = m;
            return m;
        }

        public static Material Jelly(string name, PresetColors c) => Build("jelly", name, c);
        public static Material Glass(string name, PresetColors c) => Build("glass", name, c);
        public static Material Prismatic(string name, PresetColors c) => Build("prismatic", name, c);
        public static Material Neon(string name, PresetColors c) => Build("neon", name, c);
        public static Material Metal(string name, PresetColors c) => Build("metal", name, c);
        public static Material Glitch(string name, PresetColors c) => Build("glitch", name, c);
        public static Material Stone(string name, PresetColors c) => Build("stone", name, c);

        /// <summary>A flat colour set becomes a soft top-to-bottom gradient (slime shaders look dull with three equal colours).</summary>
        public static PresetColors Shade(PresetColors c)
        {
            if (c.Top == c.Middle && c.Middle == c.Bottom) { c.Top = c.Middle.Mul(1.18f); c.Bottom = c.Middle.Mul(0.72f); }
            return c;
        }

        static void NoHyper(Material m) { m.Kw("_ENABLEHYPEREFFECT_ON", false); m.Flt("_EnableHyperEffect", 0f); m.Kw("_ENABLEVORTEX_ON", false); m.Flt("_EnableVortex", 0f); }

        // ------------------------------------------------------------------ recipes

        // SR2 puddle slime body: translucent, wobbly slime shader.
        static Material BuildJelly(string name, PresetColors c)
        {
            var m = Mat.Clone(name, "slimePuddleBase", "slimePinkBase");
            if (m == null) return null;
            c = Shade(c);
            m.Palette(c.Top, c.Middle, c.Bottom);
            m.Flt("_GlossPower", 0.9f);
            return m;
        }

        // SR2 lab glass (refraction + reflections); the colour lives in the reflection tint, the body stays clear.
        static Material BuildGlass(string name, PresetColors c)
        {
            var m = Mat.Clone(name, "envLabGlass", "SimpleGlass");
            if (m == null) return null;
            m.Col("_Color", c.Middle.WithAlpha(0f));
            m.Flt("_Opacity", 0.06f);
            m.Flt("_Smoothness", 1f);
            m.Flt("_Metallic", 0f);
            m.Flt("_BaseMapAlpha", 0f);
            m.Flt("_BaseMapColor", 0f);
            m.Flt("_ReflectionIntensity", 120f);
            m.Col("_ReflectionTint", c.Top.WithAlpha(0f));
            return m;
        }

        // Hyper slime body with a colour-cycling fresnel rim.
        static Material BuildPrismatic(string name, PresetColors c)
        {
            var m = Mat.Clone(name, "HyperDefaultRingtailDefault_Body", "slimeHyperBase", "slimePinkBase");
            if (m == null) return null;
            c = Shade(c);
            m.Palette(c.Top, c.Middle, c.Bottom);
            m.Kw("_ENABLEHYPEREFFECT_ON", true); m.Flt("_EnableHyperEffect", 1f);
            m.Col("_HyperFresnelColor", c.Glow.WithAlpha(0f));
            m.Flt("_HyperFresnelIntensity", 1.6f);
            m.Flt("_HyperBodyColorAlbedoIntensity", 0.9f);
            m.Flt("_GlossPower", 0.8f);
            MaterialAnimator.HueCycle(m, "_HyperFresnelColor", 0.08f, 0.85f, 1f, 1.4f);
            return m;
        }

        // Phosphor body with a strong pulsing self-lit glow.
        static Material BuildNeon(string name, PresetColors c)
        {
            var m = Mat.Clone(name, "slimePhosphorBase", "slimePinkBase");
            if (m == null) return null;
            c = Shade(c);
            m.Palette(c.Top.Mul(0.6f), c.Middle.Mul(0.45f), c.Bottom.Mul(0.3f));
            m.Col("_GlowTop", c.Glow.HDR(2.5f).WithAlpha(1f));
            m.Flt("_GlowStrength", 1f);
            m.Flt("_GlowSpeed", 1.2f);
            m.Flt("_GlowMin", 0.6f);
            m.Flt("_GlowMax", 1f);
            m.Flt("_GlossPower", 0.7f);
            NoHyper(m);
            return m;
        }

        // Rock/crystal body pushed to full gloss.
        static Material BuildMetal(string name, PresetColors c)
        {
            var m = Mat.Clone(name, "slimeCrystalBase", "slimeRockBase", "slimePinkBase");
            if (m == null) return null;
            c = Shade(c);
            m.Palette(c.Top, c.Middle, c.Bottom);
            m.Flt("_GlossPower", 1f);
            m.Flt("_Smoothness", 0.95f);
            m.Flt("_Metallic", 1f);
            m.Flt("_ReflectionIntensity", 1.5f);
            NoHyper(m);
            Mat.NeutralScatter(m);
            return m;
        }

        // Hyper body broken into pixel cells (multi body colouring + pixel mask) with a jittering mask.
        static Material BuildGlitch(string name, PresetColors c)
        {
            var m = Mat.Clone(name, "HyperDefaultRingtailDefault_Body", "slimeHyperBase", "slimePinkBase");
            if (m == null) return null;
            c = Shade(c);
            m.Palette(c.Top, c.Middle, c.Bottom);
            m.Kw("_BODYCOLORING_DEFAULT", false); m.Kw("_BODYCOLORING_MULTI", true); m.Flt("_BodyColoring", 1f);
            m.Tx("_ColorMask", Gfx.PixelMask(name.GetHashCode() & 0xff, 16, 0.2f, 0.16f, 0.07f));
            m.Flt("_ColorMaskUV1", 0f);
            m.Col("_RedTopColor", c.Top.Mul(1.2f)); m.Col("_RedMiddleColor", c.Middle.Mul(1.2f)); m.Col("_RedBottomColor", c.Bottom.Mul(1.2f));
            m.Col("_GreenTopColor", c.Top.Mul(0.7f)); m.Col("_GreenMiddleColor", c.Middle.Mul(0.7f)); m.Col("_GreenBottomColor", c.Bottom.Mul(0.7f));
            m.Col("_BlueTopColor", Color.Lerp(c.Top, Color.white, 0.6f)); m.Col("_BlueMiddleColor", Color.Lerp(c.Middle, Color.white, 0.6f)); m.Col("_BlueBottomColor", Color.Lerp(c.Bottom, Color.white, 0.6f));
            m.Kw("_ENABLEHYPEREFFECT_ON", true); m.Flt("_EnableHyperEffect", 1f);
            m.Col("_HyperFresnelColor", c.Glow.WithAlpha(0f));
            m.Flt("_HyperBodyColorAlbedoIntensity", 0.95f);
            m.Flt("_HyperFresnelIntensity", 1.7f);
            m.Flt("_GlossPower", 0.35f);
            MaterialAnimator.Jitter(m, "_ColorMask", 0.25f, 0.12f);
            return m;
        }

        // Matte rock body (Ringtail petrification, statues).
        static Material BuildStone(string name, PresetColors c)
        {
            var m = Mat.Clone(name, "slimeRockBase", "slimePinkBase");
            if (m == null) return null;
            c = Shade(c);
            m.Palette(c.Top, c.Middle, c.Bottom);
            m.Flt("_GlossPower", 0.1f);
            NoHyper(m);
            return m;
        }
    }

    /// <summary>
    /// Per-frame material animation (shared materials, so one update animates every slime using it): colour pulses, hue
    /// cycles, texture jitter or any custom <c>Action&lt;Material, float time&gt;</c>. Runs as module <c>matanim</c>.
    /// </summary>
    public static class MaterialAnimator
    {
        sealed class Anim { public Material M; public Action<Material, float> Run; public string Key; }
        static readonly List<Anim> _list = new();

        public static int Count => _list.Count;

        /// <summary>Adds an animation (<paramref name="key"/> replaces an earlier one with the same key on the same material).</summary>
        public static void Add(Material m, Action<Material, float> animate, string key = null)
        {
            if (m == null || animate == null) return;
            if (key != null) _list.RemoveAll(a => a.Key == key && a.M != null && a.M.Pointer == m.Pointer);
            _list.Add(new Anim { M = m, Run = animate, Key = key });
        }

        /// <summary>Stops every animation of <paramref name="m"/>.</summary>
        public static int Remove(Material m) => m == null ? 0 : _list.RemoveAll(a => a.M == null || a.M.Pointer == m.Pointer);

        /// <summary>Colour property oscillating between <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static void Pulse(Material m, string prop, Color a, Color b, float speed = 1f) =>
            Add(m, (x, t) => x.Col(prop, Color.Lerp(a, b, 0.5f + 0.5f * Mathf.Sin(t * speed * Mathf.PI * 2f))), "pulse:" + prop);

        /// <summary>Float property oscillating between <paramref name="min"/> and <paramref name="max"/>.</summary>
        public static void Pulse(Material m, string prop, float min, float max, float speed = 1f) =>
            Add(m, (x, t) => x.Flt(prop, Mathf.Lerp(min, max, 0.5f + 0.5f * Mathf.Sin(t * speed * Mathf.PI * 2f))), "pulsef:" + prop);

        /// <summary>Rainbow colour cycle (<paramref name="speed"/> = turns per second; <paramref name="intensity"/> &gt; 1 = HDR).</summary>
        public static void HueCycle(Material m, string prop, float speed = 0.1f, float saturation = 1f, float value = 1f, float intensity = 1f) =>
            Add(m, (x, t) =>
            {
                var c = Color.HSVToRGB(Mathf.Repeat(t * speed, 1f), saturation, value).HDR(intensity);
                c.a = x.GetCol(prop).a;
                x.Col(prop, c);
            }, "hue:" + prop);

        /// <summary>Random texture offset jumps of up to <paramref name="amount"/> every <paramref name="interval"/> seconds (glitchy masks).</summary>
        public static void Jitter(Material m, string textureProp, float amount = 0.2f, float interval = 0.1f)
        {
            float next = 0f;
            Add(m, (x, t) =>
            {
                if (t < next || !x.HasProperty(textureProp)) return;
                next = t + interval * World.Rand(0.5f, 1.5f);
                x.SetTextureOffset(textureProp, World.Chance(0.6f) ? Vector2.zero : new Vector2(World.Rand(-amount, amount), World.Rand(-amount, amount)));
            }, "jitter:" + textureProp);
        }

        internal static void Tick(float time)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var a = _list[i];
                if (a.M == null) { _list.RemoveAt(i); continue; }
                try { a.Run(a.M, time); }
                catch (Exception e) { ForgeLog.WarnOnce("matanim:" + a.Key, $"MaterialAnimator {a.Key}: {e.Message}"); _list.RemoveAt(i); }
            }
        }

        internal sealed class Module : IForgeModule
        {
            public string Id => "matanim";
            public void Init() { }
            public void Tick(float dt) { if (_list.Count > 0) MaterialAnimator.Tick(Time.time); }
        }
    }
}
