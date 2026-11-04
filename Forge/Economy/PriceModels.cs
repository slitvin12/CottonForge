using System;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Built-in <see cref="IPriceModel"/>s. Each one starts from <see cref="PriceContext.GamePrice"/> (SR2's saturated,
    /// noisy price) unless stated otherwise; <see cref="Chain"/> feeds the result of one model into the next:
    /// <code>PlortMarket.SetModel(myPlort, PriceModels.Chain(PriceModels.Volatile(0.3f, 4f), PriceModels.Glut(40, 0.4f)));</code>
    /// </summary>
    public static class PriceModels
    {
        sealed class Fn : IPriceModel
        {
            readonly Func<PriceContext, float> _f; readonly string _name;
            public Fn(string name, Func<PriceContext, float> f) { _name = name; _f = f; }
            public float Price(in PriceContext c) => _f(c);
            public override string ToString() => _name;
        }

        sealed class ChainModel : IPriceModel
        {
            readonly IPriceModel[] _m;
            public ChainModel(IPriceModel[] m) { _m = m ?? Array.Empty<IPriceModel>(); }
            public float Price(in PriceContext c)
            {
                float p = c.GamePrice;
                foreach (var m in _m)
                {
                    if (m == null) continue;
                    p = m.Price(new PriceContext(c.Plort, c.BaseValue, c.FullSaturation, c.Day, p, c.RecentlySold));
                }
                return p;
            }
            public override string ToString() => "chain(" + string.Join(", ", (object[])_m) + ")";
        }

        /// <summary>Model from a function.</summary>
        public static IPriceModel From(Func<PriceContext, float> price, string name = "custom") => new Fn(name, price);

        /// <summary>SR2's own price (useful as a placeholder or to override <see cref="PlortMarket.DefaultModel"/> for one plort).</summary>
        public static IPriceModel Vanilla() => new Fn("vanilla", c => c.GamePrice);

        /// <summary>Always <paramref name="price"/> (no saturation, no noise).</summary>
        public static IPriceModel Fixed(float price) => new Fn($"fixed {price:0}", _ => price);

        /// <summary>
        /// Smooth wave of ±<paramref name="amplitude"/> (0.25 = ±25%) with a period of <paramref name="periodDays"/> days plus
        /// a daily random wobble; the phase differs per plort (and per <paramref name="seed"/>).
        /// </summary>
        public static IPriceModel Volatile(float amplitude = 0.25f, float periodDays = 3f, int seed = 0) =>
            new Fn($"volatile ±{amplitude:P0}/{periodDays:0.#}d", c =>
            {
                int h = Hash(c.Plort != null ? c.Plort.name : "", seed);
                float phase = (h & 0xffff) / 65535f * Mathf.PI * 2f;
                float wave = Mathf.Sin(c.Day / Mathf.Max(0.1f, periodDays) * Mathf.PI * 2f + phase);
                float wobble = Noise(h, Mathf.FloorToInt(c.Day)) * 2f - 1f;
                return c.GamePrice * (1f + amplitude * (wave * 0.7f + wobble * 0.3f));
            });

        /// <summary>Grows (or shrinks, negative) by <paramref name="perDay"/> per day since <paramref name="startDay"/>, within [min, max] of the game price.</summary>
        public static IPriceModel Trend(float perDay, float startDay = 0f, float min = 0.25f, float max = 4f) =>
            new Fn($"trend {perDay:+0.##;-0.##}/d", c =>
                c.GamePrice * Mathf.Clamp(Mathf.Pow(1f + perDay, Mathf.Max(0f, c.Day - startDay)), min, max));

        /// <summary>
        /// Saturation response on top of SR2's: once more than <paramref name="threshold"/> plorts were sold recently the
        /// price falls proportionally, down to <paramref name="floor"/> of the incoming price.
        /// </summary>
        public static IPriceModel Glut(float threshold, float floor = 0.3f) =>
            new Fn($"glut >{threshold:0}", c =>
                c.RecentlySold <= threshold ? c.GamePrice : c.GamePrice * Mathf.Max(floor, threshold / c.RecentlySold));

        /// <summary>Unsold plorts become rare: +<paramref name="boost"/> while fewer than <paramref name="below"/> were sold recently.</summary>
        public static IPriceModel Scarcity(float boost = 0.5f, float below = 1f) =>
            new Fn($"scarcity +{boost:P0}", c => c.RecentlySold < below ? c.GamePrice * (1f + boost) : c.GamePrice);

        /// <summary>×<paramref name="multiplier"/> while any of <paramref name="kinds"/> is active (see <see cref="ForgeWeather"/>).</summary>
        public static IPriceModel Weather(WeatherKind kinds, float multiplier) =>
            new Fn($"weather {kinds} x{multiplier:0.##}", c => ForgeWeather.IsActive(kinds) ? c.GamePrice * multiplier : c.GamePrice);

        /// <summary>Ignores SR2's price and uses the base value with a simple linear saturation (classic SR1 feel).</summary>
        public static IPriceModel Classic(float minShare = 0.25f) =>
            new Fn("classic", c =>
            {
                float sat = c.FullSaturation > 0f ? Mathf.Clamp01(c.RecentlySold / c.FullSaturation) : 0f;
                return c.BaseValue * Mathf.Lerp(1f, minShare, sat);
            });

        /// <summary>Applies the models in order (output price of one = <see cref="PriceContext.GamePrice"/> of the next).</summary>
        public static IPriceModel Chain(params IPriceModel[] models) => new ChainModel(models);

        static int Hash(string s, int seed)
        {
            unchecked
            {
                int h = (int)2166136261 ^ seed;
                foreach (var ch in s) h = (h ^ ch) * 16777619;
                return h & 0x7fffffff;
            }
        }

        static float Noise(int h, int day)
        {
            unchecked
            {
                uint x = (uint)(h * 374761393 + day * 668265263);
                x = (x ^ (x >> 13)) * 1274126177u;
                return ((x ^ (x >> 16)) & 0xffffff) / 16777215f;
            }
        }
    }
}
