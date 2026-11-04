using System;
using System.Collections.Generic;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Registry and driver of <see cref="IForgeModule"/>s. The built-in engine modules (actor runtime, weather, market,
    /// HUD, vacpack, material animation) register here; mods can add their own. A module whose id is listed in the
    /// <c>DisabledModules</c> setting (comma separated) is neither initialised nor ticked.
    /// </summary>
    public static class ForgeModules
    {
        sealed class Entry { public IForgeModule M; public bool On; public int Errors; }

        static readonly List<Entry> _all = new();
        static readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
        static bool _inited;

        /// <summary>Registered modules.</summary>
        public static IEnumerable<IForgeModule> All { get { foreach (var e in _all) yield return e.M; } }

        /// <summary>Adds a module (initialised right away when Forge is already running).</summary>
        public static void Register(IForgeModule module)
        {
            if (module == null) return;
            foreach (var e in _all)
                if (string.Equals(e.M.Id, module.Id, StringComparison.OrdinalIgnoreCase)) { ForgeLog.Warn($"Module '{module.Id}' is already registered"); return; }
            var n = new Entry { M = module };
            _all.Add(n);
            if (_inited) Start(n);
        }

        /// <summary>True when the module exists, is not disabled and initialised without error.</summary>
        public static bool IsRunning(string id)
        {
            foreach (var e in _all) if (string.Equals(e.M.Id, id, StringComparison.OrdinalIgnoreCase)) return e.On;
            return false;
        }

        /// <summary>The first registered module of type <typeparamref name="T"/>.</summary>
        public static T Get<T>() where T : class, IForgeModule
        {
            foreach (var e in _all) if (e.M is T t) return t;
            return null;
        }

        /// <summary>"id: on/off/disabled (errors)" lines.</summary>
        public static List<string> Describe()
        {
            var l = new List<string>();
            foreach (var e in _all)
                l.Add($"{e.M.Id,-12} {(_disabled.Contains(e.M.Id) ? "disabled" : e.On ? "on" : "off")}{(e.Errors > 0 ? $" ({e.Errors} errors)" : "")}  {e.M.GetType().FullName}");
            return l;
        }

        internal static void InitAll()
        {
            if (_inited) return;
            _inited = true;
            foreach (var s in (ForgeConfig.DisabledModules?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _disabled.Add(s);
            Register(new ActorRuntime.Module());
            Register(new ForgeWeather.Module());
            Register(new PlortMarket.Module());
            Register(new FireModes.Module());
            Register(new ForgeHud.Module());
            Register(new MaterialAnimator.Module());
            foreach (var e in _all) if (!e.On) Start(e);
        }

        static void Start(Entry e)
        {
            if (_disabled.Contains(e.M.Id)) return;
            try { e.M.Init(); e.On = true; }
            catch (Exception x) { e.Errors++; ForgeLog.Error($"Module {e.M.Id} failed to start: {x}"); }
        }

        internal static void Tick()
        {
            float dt = Time.deltaTime;
            foreach (var e in _all)
            {
                if (!e.On) continue;
                try { e.M.Tick(dt); }
                catch (Exception x)
                {
                    e.Errors++;
                    ForgeLog.WarnOnce("module:" + e.M.Id, $"Module {e.M.Id} tick failed: {x}");
                    if (e.Errors >= 50) { e.On = false; ForgeLog.Error($"Module {e.M.Id} switched off after {e.Errors} errors"); }
                }
            }
        }
    }
}
