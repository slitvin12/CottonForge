using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Icons of generated largos (config <c>LargoIcons</c>). Cotton leaves the template's icon: every Cotton largo showed the
    /// PinkRock (or Pink+X) largo. Forge paints one per largo, a few per frame after generation (GPU read-back):
    /// <list type="bullet">
    /// <item><see cref="LargoRecipe.Icon"/> when a recipe gives one;</item>
    /// <item>the template is the vanilla largo of a parent: its icon repainted from the template palette to the largo palette
    /// (hue-matched, outlines / eyes / grey parts untouched);</item>
    /// <item>otherwise the body parent's icon repainted the same way;</item>
    /// <item>when the largo wears the other parent's colours, the repaint goes to the colours that parent's own icon shows
    /// (<see cref="Ramp"/>: icons are lit and use the palette differently — the Rad icon body is its bottom colour, the Pink
    /// one its top colour; repainting by palette colour made Rad + Pink dark maroon);</item>
    /// <item>plus a small icon of the parent that is not shown yet in a corner (<see cref="Badges"/>).</item>
    /// </list>
    /// Radiant variants get their own (template's radiant icon or the default one, repainted to the radiant palette).
    /// The template's icon stays until the new one is ready.
    /// </summary>
    public static class SlimeIcons
    {
        /// <summary>Config <c>LargoIcons</c> (default on).</summary>
        public static bool Enabled => ForgeConfig.LargoIcons == null || ForgeConfig.LargoIcons.Value;
        /// <summary>Largest icon side (larger sources are box-filtered down).</summary>
        public static int Size = 256;
        /// <summary>Icons made per frame.</summary>
        public static int PerFrame = 2;
        /// <summary>Corner icon of the other parent.</summary>
        public static bool Badges = true;
        public static float BadgeScale = 0.44f;
        /// <summary>Hue distance (degrees) up to which a pixel counts as a palette colour.</summary>
        public static float HueWindow = 38f;

        public static int Made, Failed;
        public static int Pending => _queue.Count;

        sealed class Job { public SlimeDefinition Largo, One, Two, Template; public Sprite Was; }
        static readonly Queue<Job> _queue = new();
        static bool _pumping;
        static readonly Dictionary<IntPtr, string> _how = new();
        static readonly Dictionary<IntPtr, Sprite> _made = new();
        static readonly Dictionary<IntPtr, Px> _read = new();   // source icons read back while the queue runs (read-only)

        /// <summary>How the icon of a largo was made, or null.</summary>
        public static string HowOf(SlimeDefinition largo) => largo != null && _how.TryGetValue(largo.Pointer, out var s) ? s : null;
        /// <summary>True when <paramref name="sprite"/> was made here.</summary>
        public static bool IsMade(Sprite sprite) => sprite != null && _made.ContainsKey(sprite.Pointer);

        internal static void Queue(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two, SlimeDefinition template)
        {
            if (!Enabled || largo == null || one == null || two == null) return;
            var forced = LargoRecipe.Find(one.name, two.name)?.IconSprite;
            if (forced != null) { Set(largo, null, forced, "recipe"); return; }
            _queue.Enqueue(new Job { Largo = largo, One = one, Two = two, Template = template, Was = largo.icon });
            if (_pumping) return;
            _pumping = true;
            Scheduler.Every(0f, Pump, "largo icons");
        }

        static bool Pump()
        {
            for (int i = 0; i < PerFrame && _queue.Count > 0; i++)
            {
                var j = _queue.Dequeue();
                if (j.Largo == null) continue;
                if (Safe.Run("largo icon " + j.Largo.name, () => Make(j), false)) Made++; else Failed++;
            }
            if (_queue.Count > 0) return true;
            _pumping = false;
            _read.Clear();
            ForgeLog.Debug($"[icons] {Made} largo icons, {Failed} failed");
            return false;
        }

        static bool Same(Il2CppObjectBase a, Il2CppObjectBase b) => (object)a != null && (object)b != null && a.Pointer == b.Pointer;
        static bool SameOrNone(Il2CppObjectBase a, Il2CppObjectBase b) => ((object)a == null && (object)b == null) || Same(a, b);
        static SlimeAppearance Default(SlimeDefinition d) => VanillaSlimes.App(d);

        static bool Related(SlimeDefinition tpl, SlimeDefinition d)
        {
            var b = tpl != null ? tpl.BaseSlimes : null;
            if (b == null || d == null) return false;
            for (int i = 0; i < b.Length; i++) if (Same(b[i], d)) return true;
            var sb = SlimeRegistry.Of(d);
            if (sb != null && !string.IsNullOrEmpty(sb.TemplateName))
                for (int i = 0; i < b.Length; i++) if (b[i] != null && string.Equals(b[i].name, sb.TemplateName, StringComparison.OrdinalIgnoreCase) && !string.Equals(sb.TemplateName, "Pink", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ------------------------------------------------------------------ pixels

        sealed class Px
        {
            public Color[] C; public int W, H; public Sprite Like;
        }

        static Px Get(Sprite s)
        {
            if (s == null) return null;
            if (_read.TryGetValue(s.Pointer, out var p)) return p;
            p = Read(s);
            if (p != null) _read[s.Pointer] = p;
            return p;
        }

        static Px Read(Sprite s)
        {
            if (s == null || s.texture == null) return null;
            var tex = s.texture;
            Rect r;
            try { r = s.textureRect; } catch { r = s.rect; }
            int x0 = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, tex.width - 1), y0 = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, tex.height - 1);
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, tex.width - x0), h = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, tex.height - y0);
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Texture2D t = null;
            Color[] c;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                t = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                t.ReadPixels(new Rect(x0, y0, w, h), 0, 0);
                t.Apply();
                var src = t.GetPixels();
                c = new Color[src.Length];
                for (int i = 0; i < c.Length; i++) c[i] = src[i];
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (t != null) UnityEngine.Object.Destroy(t);
            }
            var px = new Px { C = c, W = w, H = h, Like = s };
            int k = Mathf.CeilToInt(Mathf.Max(w, h) / (float)Mathf.Max(16, Size));
            return k > 1 ? Shrink(px, k) : px;
        }

        /// <summary>Box filter with premultiplied alpha (no dark fringes).</summary>
        static Px Shrink(Px p, int k)
        {
            int w = Math.Max(1, p.W / k), h = Math.Max(1, p.H / k);
            var o = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0; int n = 0;
                    for (int dy = 0; dy < k; dy++)
                        for (int dx = 0; dx < k; dx++)
                        {
                            int sx = x * k + dx, sy = y * k + dy;
                            if (sx >= p.W || sy >= p.H) continue;
                            var c = p.C[sy * p.W + sx];
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a; n++;
                        }
                    o[y * w + x] = a > 0f ? new Color(r / a, g / a, b / a, a / Math.Max(1, n)) : new Color(0, 0, 0, 0);
                }
            return new Px { C = o, W = w, H = h, Like = p.Like };
        }

        static Px Clone(Px p) => new Px { C = (Color[])p.C.Clone(), W = p.W, H = p.H, Like = p.Like };

        static Vector3[] Lch(LargoPalette p) => new[] { ColorMix.ToLch(p.Top), ColorMix.ToLch(p.Middle), ColorMix.ToLch(p.Bottom) };

        /// <summary>Repaints palette-coloured pixels from one palette to another (LCh: lightness shift, chroma ratio, hue rotation).</summary>
        static void Repaint(Px p, LargoPalette from, LargoPalette to) => Repaint(p, Lch(from), Lch(to));

        /// <summary>
        /// Same with the palettes as LCh; with <paramref name="rampFrom"/> / <paramref name="rampTo"/> (<see cref="Ramp"/>) coloured
        /// pixels follow the target icon's lightness ramp instead of the palette colours.
        /// </summary>
        static void Repaint(Px p, Vector3[] src, Vector3[] dst, Vector3[] rampFrom = null, Vector3[] rampTo = null)
        {
            bool ramp = rampFrom != null && rampTo != null, change = ramp;
            for (int k = 0; k < 3; k++) if ((src[k] - dst[k]).sqrMagnitude > 1e-6f) change = true;
            if (!change) return;
            var cache = new Dictionary<int, Color>();
            for (int i = 0; i < p.C.Length; i++)
            {
                var c = p.C[i];
                if (c.a < 0.02f) continue;
                int key = (Mathf.RoundToInt(c.r * 255f) << 16) | (Mathf.RoundToInt(c.g * 255f) << 8) | Mathf.RoundToInt(c.b * 255f);
                if (!cache.TryGetValue(key, out var o)) { var px = new Color(c.r, c.g, c.b, 1f); cache[key] = o = ramp ? MapRamp(px, src, dst, rampFrom, rampTo) : Map(px, src, dst); }
                p.C[i] = new Color(o.r, o.g, o.b, c.a);
            }
        }

        static float HueDiff(float a, float b) { float d = Mathf.Abs(a - b) % 360f; return d > 180f ? 360f - d : d; }

        /// <summary>Palette colour a pixel (LCh) belongs to, or -1 (outline / eye / highlight / other colour); <paramref name="w"/> = how much (0..1).</summary>
        static int Nearest(Vector3 l, Vector3[] src, out float w)
        {
            w = 0f;
            if (l.x < 0.18f || l.x > 0.97f) return -1;                      // outlines, eyes, highlights
            bool grey = l.y < ColorMix.GreyChroma;
            int best = -1; float score = float.MaxValue, dh = 0f;
            for (int k = 0; k < 3; k++)
            {
                var s = src[k];
                bool sg = s.y < ColorMix.GreyChroma;
                if (grey != sg) continue;
                float h = grey ? 0f : HueDiff(l.z, s.z);
                float sc = h + 90f * Mathf.Abs(l.x - s.x);
                if (sc < score) { score = sc; best = k; dh = h; }
            }
            if (best < 0) return -1;
            if (grey) { float dl = Mathf.Abs(l.x - src[best].x); if (dl >= 0.15f) return -1; w = 1f - dl / 0.15f; }
            else { if (dh >= HueWindow) return -1; w = 1f - dh / HueWindow; }
            return best;
        }

        /// <summary>Bands of <see cref="Ramp"/>.</summary>
        public static int RampBands = 5;

        /// <summary>
        /// Lightness ramp of the coloured palette pixels of an icon: <see cref="RampBands"/> bands of equal pixel count, from dark
        /// to light, mean LCh each. Null when the icon has too few such pixels.
        /// </summary>
        static Vector3[] Ramp(Px p, Vector3[] pal)
        {
            var ls = new List<Vector3>();
            for (int i = 0; i < p.C.Length; i++)
            {
                var c = p.C[i];
                if (c.a < 0.5f) continue;
                var l = ColorMix.ToLch(new Color(c.r, c.g, c.b, 1f));
                if (l.y < ColorMix.GreyChroma) continue;
                int k = Nearest(l, pal, out float w);
                if (k >= 0 && w >= 0.5f) ls.Add(l);
            }
            int n = Math.Max(2, RampBands);
            if (ls.Count < n * 8) return null;
            ls.Sort((x, y) => x.x.CompareTo(y.x));
            var o = new Vector3[n];
            for (int b = 0; b < n; b++)
            {
                int i0 = ls.Count * b / n, i1 = Math.Max(i0 + 1, ls.Count * (b + 1) / n);
                float L = 0f, C = 0f, hx = 0f, hy = 0f;
                for (int i = i0; i < i1; i++)
                {
                    var l = ls[i]; float h = l.z * Mathf.Deg2Rad;
                    L += l.x; C += l.y; hx += Mathf.Cos(h) * l.y; hy += Mathf.Sin(h) * l.y;
                }
                int m = i1 - i0;
                o[b] = new Vector3(L / m, C / m, (Mathf.Atan2(hy, hx) * Mathf.Rad2Deg + 360f) % 360f);
            }
            return o;
        }

        static float HueDelta(float from, float to) { float d = (to - from) % 360f; if (d > 180f) d -= 360f; if (d < -180f) d += 360f; return d; }

        /// <summary>A coloured palette pixel goes to the same place on the target ramp (lightness offset kept, chroma ratio, hue rotation); grey ones as <see cref="Map"/>.</summary>
        static Color MapRamp(Color c, Vector3[] src, Vector3[] dst, Vector3[] a, Vector3[] b)
        {
            var l = ColorMix.ToLch(c);
            if (l.y < ColorMix.GreyChroma) return Map(c, src, dst);
            if (Nearest(l, src, out float w) < 0) return c;
            w = w * w * (3f - 2f * w);
            int n = a.Length, i = 0;
            float f = 0f;
            if (l.x >= a[n - 1].x) { i = n - 2; f = 1f; }
            else if (l.x > a[0].x)
            {
                while (i < n - 2 && l.x > a[i + 1].x) i++;
                float d = a[i + 1].x - a[i].x;
                f = d > 1e-4f ? Mathf.Clamp01((l.x - a[i].x) / d) : 0f;
            }
            float sl = Mathf.Lerp(a[i].x, a[i + 1].x, f), dl = Mathf.Lerp(b[i].x, b[i + 1].x, f);
            float r = Mathf.Lerp(b[i].y / Mathf.Max(0.02f, a[i].y), b[i + 1].y / Mathf.Max(0.02f, a[i + 1].y), f);
            float dh = Mathf.Lerp(HueDelta(a[i].z, b[i].z), HueDelta(a[i + 1].z, b[i + 1].z), f);
            var o = ColorMix.FromLch(new Vector3(Mathf.Clamp01(l.x + dl - sl), l.y * Mathf.Clamp(r, 0f, 3f), (l.z + dh + 720f) % 360f), 1f);
            return Color.Lerp(c, o, w);
        }

        static Color Map(Color c, Vector3[] src, Vector3[] dst)
        {
            var l = ColorMix.ToLch(c);
            int best = Nearest(l, src, out float w);
            if (best < 0) return c;
            bool grey = l.y < ColorMix.GreyChroma;
            w = w * w * (3f - 2f * w);
            var a = src[best]; var b = dst[best];
            float L = Mathf.Clamp01(l.x + (b.x - a.x));
            float C = grey ? b.y * Mathf.Clamp01(l.x / Mathf.Max(0.05f, a.x)) : l.y * Mathf.Clamp(b.y / Mathf.Max(0.02f, a.y), 0f, 3f);
            float H = grey ? b.z : (l.z + (b.z - a.z) + 720f) % 360f;
            var o = ColorMix.FromLch(new Vector3(L, C, H), 1f);
            return Color.Lerp(c, o, w);
        }

        /// <summary>Puts <paramref name="b"/> (scaled to <see cref="BadgeScale"/>) into the bottom-right corner of <paramref name="p"/>.</summary>
        static void Badge(Px p, Px b)
        {
            if (b == null) return;
            int bw = Mathf.Max(8, Mathf.RoundToInt(p.W * BadgeScale));
            int bh = Mathf.Max(8, Mathf.RoundToInt(bw * b.H / (float)b.W));
            int ox = p.W - bw, oy = 0;
            float fx = b.W / (float)bw, fy = b.H / (float)bh;
            for (int y = 0; y < bh && oy + y < p.H; y++)
                for (int x = 0; x < bw; x++)
                {
                    int dx = ox + x; if (dx < 0) continue;
                    float r = 0, g = 0, bl = 0, a = 0; int n = 0;
                    int sx0 = (int)(x * fx), sx1 = Math.Max(sx0 + 1, (int)((x + 1) * fx));
                    int sy0 = (int)(y * fy), sy1 = Math.Max(sy0 + 1, (int)((y + 1) * fy));
                    for (int sy = sy0; sy < sy1 && sy < b.H; sy++)
                        for (int sx = sx0; sx < sx1 && sx < b.W; sx++)
                        {
                            var c = b.C[sy * b.W + sx];
                            r += c.r * c.a; g += c.g * c.a; bl += c.b * c.a; a += c.a; n++;
                        }
                    if (n == 0 || a <= 0f) continue;
                    var s = new Color(r / a, g / a, bl / a, a / n);
                    int i = (oy + y) * p.W + dx;
                    var d = p.C[i];
                    float oa = s.a + d.a * (1f - s.a);
                    if (oa <= 0f) continue;
                    p.C[i] = new Color((s.r * s.a + d.r * d.a * (1f - s.a)) / oa, (s.g * s.a + d.g * d.a * (1f - s.a)) / oa, (s.b * s.a + d.b * d.a * (1f - s.a)) / oa, oa);
                }
        }

        static Sprite ToSprite(Px p, string name)
        {
            var t = new Texture2D(p.W, p.H, TextureFormat.RGBA32, false, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels(p.C);
            t.Apply(false, true);
            t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            var like = p.Like;
            var r = like.rect;
            var piv = like.pivot;
            var pivot = new Vector2(r.width > 0f ? piv.x / r.width : 0.5f, r.height > 0f ? piv.y / r.height : 0.5f);
            float ppu = like.pixelsPerUnit * p.W / Mathf.Max(1f, r.width);
            var s = Sprite.Create(t, new Rect(0, 0, p.W, p.H), pivot, ppu);
            s.name = name;
            s.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            _made[s.Pointer] = s;
            return s;
        }

        // ------------------------------------------------------------------ icons

        static bool Make(Job j)
        {
            var largo = j.Largo; var tpl = j.Template;
            var app = Default(largo);
            if (app == null) return false;
            if (!SameOrNone(largo.icon, j.Was)) { _how[largo.Pointer] = "kept (set by a mod)"; return true; }   // an OnCreated hook gave it one
            var pal = LargoBlend.PaletteOf(app);
            bool r1 = Related(tpl, j.One), r2 = Related(tpl, j.Two);
            Px basePx; LargoPalette basePal; string how; SlimeDefinition shown;
            if ((r1 || r2) && tpl.icon != null)
            {
                basePx = Get(tpl.icon); basePal = LargoBlend.PaletteOf(Default(tpl));
                shown = r1 ? j.One : j.Two;
                how = "template " + tpl.name;
            }
            else
            {
                var info = LargoBlend.InfoOf(largo);
                shown = info != null && string.Equals(info.Body, j.Two.name, StringComparison.OrdinalIgnoreCase) ? j.Two : j.One;
                basePx = Get(shown.icon); basePal = LargoBlend.PaletteOf(Default(shown));
                how = "parent " + shown.name;
            }
            if (basePx == null) return false;
            var other = Same(shown, j.One) ? j.Two : j.One;
            bool badge = Badges && other != null && other.icon != null && !(r1 && r2) && !other.name.Equals("Pink", StringComparison.OrdinalIgnoreCase);
            var badgePx = badge ? Get(other.icon) : null;

            var def = Clone(basePx);
            // wearing the other parent's colours: repaint to the colours of that parent's own icon (see Ramp)
            SlimeDefinition owner = null; Vector3[] from = null, to = null;
            if (VanillaSlimes.Dist(basePal, pal) > 0.02f)
                foreach (var d in new[] { j.One, j.Two })
                {
                    if (owner != null || d == null || d.icon == null) continue;
                    var da = Default(d);
                    if (da == null) continue;
                    var dp = LargoBlend.PaletteOf(da);
                    if (VanillaSlimes.Dist(dp, pal) > 0.01f) continue;
                    var op = Get(d.icon);
                    if (op == null) continue;
                    from = Ramp(basePx, Lch(basePal)); to = Ramp(op, Lch(dp));
                    if (from != null && to != null) owner = d;
                }
            if (owner != null) Repaint(def, Lch(basePal), Lch(pal), from, to);
            else Repaint(def, basePal, pal);
            if (badgePx != null) Badge(def, badgePx);
            var icon = ToSprite(def, "iconLargo" + largo.name);
            how += " repainted" + (owner != null ? " to " + owner.name + "'s icon colours" : "") + (badgePx != null ? " + " + other.name : "");
            Set(largo, app, icon, how);

            // radiant variants: the template's radiant icon (same slot) or the default one, repainted to the radiant palette
            var apps = largo.AppearancesDefault;
            if (apps != null)
                for (int i = 0; i < apps.Length; i++)
                {
                    var ra = apps[i];
                    if (ra == null || Same(ra, app) || ra._appearType == SlimeAppearance.AppearanceType.DEFAULT) continue;
                    Safe.Run("radiant icon " + ra.name, () =>
                    {
                        SlimeAppearance tr = null;
                        if (r1 || r2)
                        {
                            var ta = tpl.AppearancesDefault;
                            if (ta != null) for (int k = 0; k < ta.Length; k++) if (ta[k] != null && ta[k]._appearType == ra._appearType) tr = ta[k];
                        }
                        Px p; LargoPalette from;
                        if (tr != null && tr._icon != null && !Same(tr._icon, tpl.icon)) { var src = Get(tr._icon); p = src != null ? Clone(src) : null; from = LargoBlend.PaletteOf(tr); }
                        else { p = Clone(def); from = pal; }
                        if (p == null) return;
                        Repaint(p, from, LargoBlend.PaletteOf(ra));
                        if (tr != null && badgePx != null) Badge(p, badgePx);
                        ra._icon = ToSprite(p, "iconLargo" + largo.name + "Radiant" + i);
                    });
                }
            return true;
        }

        static void Set(SlimeDefinition largo, SlimeAppearance app, Sprite icon, string how)
        {
            largo.icon = icon;
            var a = app ?? Default(largo);
            if (a != null) a._icon = icon;
            _how[largo.Pointer] = how;
            PediaArt.Register(largo, icon);
            var info = LargoBlend.InfoOf(largo);
            if (info != null) info.Icon = how;
        }
    }
}
