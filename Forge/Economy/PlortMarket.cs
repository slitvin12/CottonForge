using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Economy;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Plort Market Engine: registers plorts at the market, gives each plort a price model (<see cref="IPriceModel"/>,
    /// built-ins in <see cref="PriceModels"/>) on top of SR2's own saturation/noise, tracks sales (saturation memory that
    /// decays by half every in-game day) and runs market events (<see cref="Boom"/>/<see cref="Crash"/>, saved per game).
    /// Models act in SR2's daily target price (<c>PlortEconomyDirector.GetTargetValue</c>), so a change shows up on the
    /// next market update; <see cref="Refresh"/> asks for one right away.
    /// </summary>
    public static class PlortMarket
    {
        /// <summary>A temporary price multiplier ("Rock plort boom" x2 for 3 days).</summary>
        public sealed class MarketEvent
        {
            public string Name;
            /// <summary>Plort name, null = every plort.</summary>
            public string Plort;
            public float Multiplier;
            /// <summary>Last in-game day (inclusive) of the event.</summary>
            public int UntilDay;
            public override string ToString() => $"{Name}: {(Plort ?? "all plorts")} x{Multiplier:0.##} until day {UntilDay}";
        }

        /// <summary>A row of the live market (see <see cref="Entries"/>).</summary>
        public readonly struct Entry
        {
            public readonly IdentifiableType Plort;
            public readonly float BaseValue, CurrValue, PrevValue, FullSaturation, RecentlySold;
            public readonly IPriceModel Model;
            public Entry(IdentifiableType p, float b, float c, float pr, float fs, float rs, IPriceModel m)
            { Plort = p; BaseValue = b; CurrValue = c; PrevValue = pr; FullSaturation = fs; RecentlySold = rs; Model = m; }
            public override string ToString() =>
                $"{Plort?.name,-22} {CurrValue,5:0} (prev {PrevValue,4:0}, base {BaseValue,4:0}, sat {FullSaturation,3:0}, sold {RecentlySold,5:0.#}){(Model != null ? " model " + Model : "")}";
        }

        const string SaveEvents = "forge.market.events", SaveSold = "forge.market.sold.", SaveDay = "forge.market.day";
        static readonly Dictionary<string, IPriceModel> _models = new(StringComparer.Ordinal);
        static readonly Dictionary<string, float> _sold = new(StringComparer.Ordinal);
        static readonly List<MarketEvent> _events = new();
        static int _lastDay = -1;

        /// <summary>Model for plorts without their own (null = SR2 prices).</summary>
        public static IPriceModel DefaultModel;
        /// <summary>Lowest price a model may set.</summary>
        public static float MinPrice = 1f;
        /// <summary>Fraction of the remembered sales kept per in-game day.</summary>
        public static float SoldDecayPerDay = 0.5f;
        /// <summary>Raised after every sale: plort, count, price per plort.</summary>
        public static event Action<IdentifiableType, int, int> Sold;
        /// <summary>Raised when an event starts or ends.</summary>
        public static event Action<MarketEvent, bool> EventChanged;

        public static IReadOnlyList<MarketEvent> Events => _events;

        static PlortEconomyDirector Director => SceneContext.Instance != null ? SceneContext.Instance.PlortEconomyDirector : null;

        /// <summary>
        /// Makes <paramref name="plort"/> sellable (when it isn't already, via Cotton's <c>Library.MakeSellable</c>) and
        /// optionally sets its model. Call while registering content (before a save is loaded).
        /// </summary>
        public static void Register(IdentifiableType plort, float baseValue, float fullSaturation, IPriceModel model = null, bool hide = false)
        {
            if (plort == null) return;
            if (!Library.marketData.ContainsKey(plort) && !Library.IsSellable(plort) && !InMarket(plort))
                Library.MakeSellable(plort, baseValue, fullSaturation, hide);
            if (model != null) SetModel(plort, model);
        }

        /// <summary>Price model of one plort (null = <see cref="DefaultModel"/>).</summary>
        public static void SetModel(IdentifiableType plort, IPriceModel model)
        {
            if (plort == null) return;
            if (model == null) _models.Remove(plort.name); else _models[plort.name] = model;
        }

        public static IPriceModel ModelOf(IdentifiableType plort) =>
            plort != null && _models.TryGetValue(plort.name, out var m) ? m : DefaultModel;

        /// <summary>True when the running market lists <paramref name="plort"/>.</summary>
        public static bool InMarket(IdentifiableType plort)
        {
            var d = Director;
            try { return d != null && plort != null && d._currValueMap != null && d._currValueMap.ContainsKey(plort); } catch { return false; }
        }

        /// <summary>Current market price (-1 = not sold / no game).</summary>
        public static int Price(IdentifiableType plort)
        {
            var d = Director;
            if (d == null || plort == null) return -1;
            try { var n = d.GetCurrValue(plort); return n.hasValue ? n.value : -1; } catch { return -1; }
        }

        /// <summary>Sells <paramref name="count"/> plorts for the player at the current price (false = market closed / not sellable).</summary>
        public static bool Sell(IdentifiableType plort, int count = 1, bool notify = true)
        {
            var d = Director;
            if (d == null || plort == null || count <= 0) return false;
            try { return d.TrySell(plort, count, false, notify, null); }
            catch (Exception e) { ForgeLog.WarnOnce("market.sell", $"PlortMarket.Sell: {e.Message}"); return false; }
        }

        /// <summary>Plorts of this type sold recently (decays by <see cref="SoldDecayPerDay"/> every in-game day).</summary>
        public static float RecentlySold(IdentifiableType plort) => plort != null && _sold.TryGetValue(plort.name, out var v) ? v : 0f;

        /// <summary>Multiplies the price of <paramref name="plort"/> (null = all plorts) for <paramref name="days"/> in-game days.</summary>
        public static MarketEvent Boom(IdentifiableType plort, float multiplier, int days, string name = null)
        {
            var e = new MarketEvent { Name = name ?? (multiplier >= 1f ? "Boom" : "Crash"), Plort = plort?.name, Multiplier = Mathf.Max(0.01f, multiplier), UntilDay = Today() + Mathf.Max(0, days - 1) };
            _events.Add(e);
            Persist();
            EventChanged?.Invoke(e, true);
            Refresh();
            return e;
        }

        /// <summary>Price drop: <see cref="Boom"/> with <c>1 / factor</c>.</summary>
        public static MarketEvent Crash(IdentifiableType plort, float factor, int days, string name = null) =>
            Boom(plort, factor > 0f ? 1f / factor : 0.5f, days, name ?? "Crash");

        public static bool EndEvent(MarketEvent e)
        {
            if (e == null || !_events.Remove(e)) return false;
            Persist();
            EventChanged?.Invoke(e, false);
            Refresh();
            return true;
        }

        /// <summary>Product of the multipliers of active events for <paramref name="plort"/>.</summary>
        public static float EventMultiplier(IdentifiableType plort)
        {
            float m = 1f;
            foreach (var e in _events) if (e.Plort == null || (plort != null && e.Plort == plort.name)) m *= e.Multiplier;
            return m;
        }

        /// <summary>Asks SR2 to recompute prices on its next update (prices otherwise change at the market's own schedule).</summary>
        public static void Refresh()
        {
            var d = Director;
            if (d == null) return;
            try { d._nextUpdateTime = 0; } catch (Exception e) { ForgeLog.WarnOnce("market.refresh", $"PlortMarket.Refresh: {e.Message}"); }
        }

        /// <summary>The live market (empty outside a game).</summary>
        public static List<Entry> Entries()
        {
            var r = new List<Entry>();
            var d = Director;
            if (d == null || d._currValueMap == null) return r;
            foreach (var kv in d._currValueMap)
            {
                var v = kv.Value;
                if (kv.Key == null || v == null) continue;
                r.Add(new Entry(kv.Key, v.BaseValue, v.CurrValue, v.PrevValue, v.FullSaturation, RecentlySold(kv.Key), ModelOf(kv.Key)));
            }
            r.Sort((a, b) => b.CurrValue.CompareTo(a.CurrValue));
            return r;
        }

        static int Today() { try { return SceneContext.Instance.TimeDirector.CurrDay(); } catch { return 0; } }

        // ------------------------------------------------------------------ hooks

        internal static float Target(IdentifiableType id, float baseValue, float fullSaturation, float day, float game)
        {
            var m = ModelOf(id);
            float mult = EventMultiplier(id);
            if (m == null && mult == 1f) return game;
            float p = game;
            if (m != null)
            {
                var c = new PriceContext(id, baseValue, fullSaturation, day, game, RecentlySold(id));
                try { p = m.Price(c); }
                catch (Exception e) { ForgeLog.WarnOnce("market.model:" + id?.name, $"Price model of {id?.name} failed: {e}"); p = game; }
            }
            p *= mult;
            if (float.IsNaN(p) || float.IsInfinity(p)) p = game;
            return Mathf.Max(MinPrice, p);
        }

        internal static void OnSold(IdentifiableType id, int count, int price)
        {
            if (id == null || count <= 0) return;
            _sold[id.name] = RecentlySold(id) + count;
            Sold?.Invoke(id, count, price);
        }

        static void Decay()
        {
            int day = Today();
            if (_lastDay < 0) { _lastDay = day; return; }
            if (day <= _lastDay) return;
            float k = Mathf.Pow(Mathf.Clamp01(SoldDecayPerDay), day - _lastDay);
            var keys = new List<string>(_sold.Keys);
            foreach (var key in keys) { float v = _sold[key] * k; if (v < 0.05f) _sold.Remove(key); else _sold[key] = v; }
            _lastDay = day;
            bool changed = false;
            for (int i = _events.Count - 1; i >= 0; i--)
                if (_events[i].UntilDay < day) { var e = _events[i]; _events.RemoveAt(i); changed = true; EventChanged?.Invoke(e, false); }
            if (changed) { Persist(); Refresh(); }
        }

        // ------------------------------------------------------------------ save

        static void Persist()
        {
            var sb = new StringBuilder();
            foreach (var e in _events)
                sb.Append(Esc(e.Name)).Append('|').Append(Esc(e.Plort ?? "*")).Append('|')
                  .Append(e.Multiplier.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(e.UntilDay).Append(';');
            ForgeSave.Set(SaveEvents, sb.Length > 0 ? sb.ToString() : null);
        }

        static string Esc(string s) => (s ?? "").Replace("|", "/").Replace(";", ",");

        static void Load()
        {
            _events.Clear(); _sold.Clear(); _lastDay = -1;
            foreach (var part in (ForgeSave.Get(SaveEvents) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = part.Split('|');
                if (f.Length < 4) continue;
                if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var mult) || !int.TryParse(f[3], out var until)) continue;
                _events.Add(new MarketEvent { Name = f[0], Plort = f[1] == "*" ? null : f[1], Multiplier = mult, UntilDay = until });
            }
            foreach (var k in ForgeSave.Keys(SaveSold)) _sold[k.Substring(SaveSold.Length)] = ForgeSave.GetFloat(k);
            _lastDay = ForgeSave.GetInt(SaveDay, -1);
        }

        static void Store()
        {
            foreach (var k in ForgeSave.Keys(SaveSold)) ForgeSave.Remove(k);
            foreach (var kv in _sold) ForgeSave.SetFloat(SaveSold + kv.Key, kv.Value);
            ForgeSave.SetInt(SaveDay, _lastDay);
            Persist();
        }

        internal sealed class Module : IForgeModule
        {
            float _t;
            public string Id => "market";
            public void Init() { ForgeSave.Loaded += Load; ForgeSave.Saving += Store; }
            public void Tick(float dt)
            {
                _t += dt;
                if (_t < 1f || !World.InGame) return;
                _t = 0f;
                Decay();
            }
        }
    }

    [HarmonyPatch(typeof(PlortEconomyDirector), nameof(PlortEconomyDirector.GetTargetValue))]
    static class PlortMarketTargetPatch
    {
        static void Postfix(IdentifiableType id, float baseValue, float fullSaturation, float day, ref float __result)
        {
            if (!ForgeModules.IsRunning("market")) return;
            try { __result = PlortMarket.Target(id, baseValue, fullSaturation, day, __result); }
            catch (Exception e) { ForgeLog.WarnOnce("market.target", $"PlortMarket target price: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(PlortEconomyDirector), nameof(PlortEconomyDirector.RegisterSold))]
    static class PlortMarketSoldPatch
    {
        static void Postfix(IdentifiableType id, int count, int price)
        {
            if (!ForgeModules.IsRunning("market")) return;
            Safe.Run("PlortMarket sold", () => PlortMarket.OnSold(id, count, price));
        }
    }
}
