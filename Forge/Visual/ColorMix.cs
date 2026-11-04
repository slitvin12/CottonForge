using System;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Perceptual colour maths (OKLab / OKLCh, Björn Ottosson). An RGB 50/50 mix of pink and green is a grey-brown; the
    /// <see cref="Mix"/> of this class walks the hue circle and keeps the saturation of the parents, so largo colours stay
    /// clean. Inputs are sRGB colours as stored in slime materials and palettes (HDR values are clamped to 0..1).
    /// </summary>
    public static class ColorMix
    {
        /// <summary>Chroma below which a colour counts as grey (its hue is meaningless).</summary>
        public const float GreyChroma = 0.035f;

        static float ToLinear(float c) { c = Mathf.Clamp01(c); return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f); }
        static float ToSrgb(float c) { c = Mathf.Max(0f, c); return c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f; }

        /// <summary>sRGB colour to OKLab (x = L 0..1, y = a, z = b).</summary>
        public static Vector3 ToOklab(Color c)
        {
            float r = ToLinear(c.r), g = ToLinear(c.g), b = ToLinear(c.b);
            float l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
            float m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
            float s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
            return new Vector3(
                0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }

        static Vector3 LabToLinear(Vector3 lab)
        {
            float l = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
            float m = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
            float s = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;
            l = l * l * l; m = m * m * m; s = s * s * s;
            return new Vector3(
                4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
        }

        static bool InGamut(Vector3 rgb) => rgb.x >= -0.0005f && rgb.x <= 1.0005f && rgb.y >= -0.0005f && rgb.y <= 1.0005f && rgb.z >= -0.0005f && rgb.z <= 1.0005f;

        /// <summary>OKLab to sRGB. Out-of-gamut colours lose chroma (same lightness and hue) instead of being clipped per channel.</summary>
        public static Color FromOklab(Vector3 lab, float alpha = 1f)
        {
            lab.x = Mathf.Clamp01(lab.x);
            var rgb = LabToLinear(lab);
            if (!InGamut(rgb))
            {
                float lo = 0f, hi = 1f;
                for (int i = 0; i < 14; i++)
                {
                    float k = (lo + hi) * 0.5f;
                    if (InGamut(LabToLinear(new Vector3(lab.x, lab.y * k, lab.z * k)))) lo = k; else hi = k;
                }
                rgb = LabToLinear(new Vector3(lab.x, lab.y * lo, lab.z * lo));
            }
            return new Color(Mathf.Clamp01(ToSrgb(rgb.x)), Mathf.Clamp01(ToSrgb(rgb.y)), Mathf.Clamp01(ToSrgb(rgb.z)), alpha);
        }

        /// <summary>sRGB colour to OKLCh (x = lightness 0..1, y = chroma, z = hue in degrees 0..360).</summary>
        public static Vector3 ToLch(Color c)
        {
            var lab = ToOklab(c);
            float h = Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg;
            if (h < 0f) h += 360f;
            return new Vector3(lab.x, Mathf.Sqrt(lab.y * lab.y + lab.z * lab.z), h);
        }

        /// <summary>OKLCh (lightness, chroma, hue in degrees) to sRGB.</summary>
        public static Color FromLch(Vector3 lch, float alpha = 1f)
        {
            float h = lch.z * Mathf.Deg2Rad;
            return FromOklab(new Vector3(lch.x, lch.y * Mathf.Cos(h), lch.y * Mathf.Sin(h)), alpha);
        }

        /// <summary>Perceived lightness 0..1.</summary>
        public static float Lightness(Color c) => ToOklab(c).x;
        /// <summary>Colourfulness (OKLCh chroma, ~0 grey .. ~0.3 very saturated).</summary>
        public static float Chroma(Color c) => ToLch(c).y;
        /// <summary>True when the colour is (nearly) grey.</summary>
        public static bool IsGrey(Color c) => Chroma(c) < GreyChroma;

        /// <summary>Hue distance in degrees (0..180); 0 when either colour is grey.</summary>
        public static float HueDistance(Color a, Color b)
        {
            var x = ToLch(a); var y = ToLch(b);
            if (x.y < GreyChroma || y.y < GreyChroma) return 0f;
            float d = Mathf.Abs(x.z - y.z) % 360f;
            return d > 180f ? 360f - d : d;
        }

        /// <summary>
        /// Perceptual mix: <paramref name="t"/> = 0 gives <paramref name="a"/>, 1 gives <paramref name="b"/>. Lightness and chroma are
        /// interpolated, the hue goes along the shorter arc; a grey parent only tints (its hue is ignored). No muddy middle.
        /// </summary>
        public static Color Mix(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            var x = ToLch(a); var y = ToLch(b);
            float alpha = Mathf.Lerp(a.a, b.a, t);
            bool gx = x.y < GreyChroma, gy = y.y < GreyChroma;
            if (gx && gy) return MixLab(a, b, t);
            float L = Mathf.Lerp(x.x, y.x, t);
            float C = Mathf.Lerp(x.y, y.y, t);
            float h;
            if (gx) h = y.z;
            else if (gy) h = x.z;
            else
            {
                float d = Mathf.DeltaAngle(x.z, y.z);
                h = x.z + d * t;
                // walking far around the circle passes through other hues: keep the result at least as colourful as the duller parent
                C = Mathf.Max(C, Mathf.Min(x.y, y.y));
            }
            if (h < 0f) h += 360f;
            return FromLch(new Vector3(L, C, h % 360f), alpha);
        }

        /// <summary>Straight OKLab mix (better than RGB, but complementary colours meet in a grey middle).</summary>
        public static Color MixLab(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return FromOklab(Vector3.Lerp(ToOklab(a), ToOklab(b), t), Mathf.Lerp(a.a, b.a, t));
        }

        /// <summary>Same hue and chroma, lightness shifted by <paramref name="dL"/> (-1..1).</summary>
        public static Color Lighten(Color c, float dL) { var l = ToLch(c); l.x = Mathf.Clamp01(l.x + dL); return FromLch(l, c.a); }

        /// <summary>Chroma multiplied by <paramref name="k"/> (0 = grey, &gt;1 more saturated, limited by the sRGB gamut).</summary>
        public static Color Saturate(Color c, float k) { var l = ToLch(c); l.y *= Mathf.Max(0f, k); return FromLch(l, c.a); }

        /// <summary>"#RRGGBB".</summary>
        public static string Hex(Color c) =>
            "#" + ((int)Mathf.Round(Mathf.Clamp01(c.r) * 255f)).ToString("X2") + ((int)Mathf.Round(Mathf.Clamp01(c.g) * 255f)).ToString("X2") + ((int)Mathf.Round(Mathf.Clamp01(c.b) * 255f)).ToString("X2");
    }
}
