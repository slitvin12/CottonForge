using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.VFX.Weather;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// SR2 weather for mods: which phenomena are active (<see cref="Active"/>, <see cref="Changed"/>), wind
    /// (<see cref="WindIntensity"/>, <see cref="WindDirection"/>), lightning (<see cref="LightningWarning"/>,
    /// <see cref="Lightning"/>) and slime reactions. Once per <see cref="PollInterval"/> every outdoor (exposed) slime gets a
    /// <see cref="SignalKind.WeatherTick"/> signal (archetypes) and matching <see cref="IWeatherReaction"/>s run; slimes
    /// near a lightning strike get <see cref="SignalKind.Lightning"/>. Sunlight/darkness helpers for light-sensitive slimes.
    /// </summary>
    public static class ForgeWeather
    {
        sealed class Reaction { public IWeatherReaction R; public SlimeDefinition Def; public bool Largos; }
        sealed class Lambda : IWeatherReaction
        {
            public WeatherKind Kinds { get; set; }
            public Action<SlimeActor, WeatherKind, float> Run;
            public void React(SlimeActor s, WeatherKind k, float dt) => Run(s, k, dt);
        }
        sealed class Keywords : IWeatherClassifier
        {
            public readonly List<(string key, WeatherKind kind)> Map = new();
            public WeatherKind Classify(string pattern, string state)
            {
                var s = ((pattern ?? "") + " " + (state ?? "")).ToLowerInvariant();
                foreach (var (k, kind) in Map) if (s.Contains(k)) return kind;
                return WeatherKind.None;
            }
        }

        static readonly List<IWeatherClassifier> _classifiers = new();
        static readonly Keywords _user = new(), _builtin = new();
        static readonly List<Reaction> _reactions = new();
        static readonly List<LightningStrike> _strikes = new();
        static readonly List<string> _states = new();
        static WeatherDirector[] _directors = Array.Empty<WeatherDirector>();
        static GlobalWindControl _wind;
        static float _nextPoll, _nextFind;

        /// <summary>Seconds between weather polls / reaction ticks.</summary>
        public static float PollInterval = 1f;
        /// <summary>Wind intensity (0..1) above which <see cref="WeatherKind.Wind"/> counts as active.</summary>
        public static float WindThreshold = 0.65f;
        /// <summary>Slimes within this distance of a strike get the lightning signal.</summary>
        public static float LightningSignalRadius = 30f;
        /// <summary>Rain, snow, fog and storms hide the sun for <see cref="InSunlight"/>.</summary>
        public static bool CloudsBlockSun = true;

        /// <summary>Phenomena active in the loaded zones.</summary>
        public static WeatherKind Active { get; private set; }
        /// <summary>"zone: state (kinds)" of the last poll.</summary>
        public static IReadOnlyList<string> States => _states;
        public static float WindIntensity { get; private set; }
        public static Vector2 WindDirection { get; private set; }
        /// <summary>Wind as a horizontal world vector scaled by intensity.</summary>
        public static Vector3 Wind => new Vector3(WindDirection.x, 0f, WindDirection.y) * WindIntensity;
        /// <summary>
        /// Exposed (outdoor) slimes that got the weather tick at the last poll; -1 = not counted (no active weather, or nothing
        /// listens: no <see cref="IWeatherReaction"/> and no actor logics).
        /// </summary>
        public static int Exposed { get; private set; } = -1;
        /// <summary>Weather directors whose exposed-actor read or reactions failed at the last poll (they are retried every poll).</summary>
        public static int FailedDirectors { get; private set; }

        /// <summary>(now, before) when the set of active phenomena changes.</summary>
        public static event Action<WeatherKind, WeatherKind> Changed;
        /// <summary>(position, seconds until the strike) when a lightning strike is announced.</summary>
        public static event Action<Vector3, float> LightningWarning;
        /// <summary>(position, blast radius) when lightning strikes.</summary>
        public static event Action<Vector3, float> Lightning;

        public static bool IsActive(WeatherKind kinds) => (Active & kinds) != 0;

        static ForgeWeather()
        {
            foreach (var (k, kind) in new (string, WeatherKind)[]
            {
                ("pollen", WeatherKind.Pollen), ("slimerain", WeatherKind.SlimeRain), ("slime rain", WeatherKind.SlimeRain), ("slime_rain", WeatherKind.SlimeRain),
                ("thunder", WeatherKind.Thunder | WeatherKind.Rain), ("lightning", WeatherKind.Thunder | WeatherKind.Rain),
                ("storm", WeatherKind.Thunder | WeatherKind.Rain | WeatherKind.Wind),
                ("rain", WeatherKind.Rain), ("drizzle", WeatherKind.Rain), ("shower", WeatherKind.Rain),
                ("tornado", WeatherKind.Wind), ("wind", WeatherKind.Wind), ("gust", WeatherKind.Wind), ("breeze", WeatherKind.Wind),
                ("snow", WeatherKind.Snow), ("blizzard", WeatherKind.Snow), ("hail", WeatherKind.Snow),
                ("fog", WeatherKind.Fog), ("mist", WeatherKind.Fog), ("heat", WeatherKind.Heat), ("drought", WeatherKind.Heat)
            }) _builtin.Map.Add((k, kind));
            _classifiers.Add(_user);
            _classifiers.Add(_builtin);
        }

        /// <summary>Adds a classifier in front of the built-in keyword table.</summary>
        public static void AddClassifier(IWeatherClassifier c) { if (c != null) _classifiers.Insert(0, c); }

        /// <summary>Weather states whose name contains <paramref name="nameContains"/> count as <paramref name="kind"/> (checked before the built-ins).</summary>
        public static void Map(string nameContains, WeatherKind kind) { if (!string.IsNullOrEmpty(nameContains)) _user.Map.Insert(0, (nameContains.ToLowerInvariant(), kind)); }

        /// <summary>Kinds of a weather state name.</summary>
        public static WeatherKind Classify(string pattern, string state)
        {
            foreach (var c in _classifiers)
            {
                var k = c.Classify(pattern, state);
                if (k != WeatherKind.None) return k;
            }
            return WeatherKind.Other;
        }

        /// <summary>Runs <paramref name="reaction"/> for exposed slimes of <paramref name="def"/> (null = all slimes).</summary>
        public static void React(IWeatherReaction reaction, SlimeDefinition def = null, bool largos = true)
        {
            if (reaction != null) _reactions.Add(new Reaction { R = reaction, Def = def, Largos = largos });
        }

        /// <summary><paramref name="a"/>(slime, active kinds, seconds) about once per second for exposed slimes of <paramref name="def"/> while one of <paramref name="kinds"/> is active.</summary>
        public static void React(SlimeDefinition def, WeatherKind kinds, Action<SlimeActor, WeatherKind, float> a, bool largos = true)
        {
            if (a != null) React(new Lambda { Kinds = kinds, Run = a }, def, largos);
        }

        // ------------------------------------------------------------------ light

        /// <summary>Something solid above the position (roof, cave, tree canopy with colliders).</summary>
        public static bool Covered(Vector3 pos) =>
            Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.up, 60f, World.GroundMask, QueryTriggerInteraction.Ignore);

        /// <summary>Daytime, open sky above and (with <see cref="CloudsBlockSun"/>) no rain, snow, fog or storm.</summary>
        public static bool InSunlight(Vector3 pos) =>
            World.IsDay() && !(CloudsBlockSun && IsActive(WeatherKind.Rain | WeatherKind.Thunder | WeatherKind.Snow | WeatherKind.Fog)) && !Covered(pos);

        /// <summary>Night, or under a roof / in a cave.</summary>
        public static bool InDarkness(Vector3 pos) => !World.IsDay() || Covered(pos);

        // ------------------------------------------------------------------ polling

        internal static void TrackStrike(LightningStrike s)
        {
            if (s == null) return;
            _strikes.Add(s);
            float warn = 0f;
            try { warn = s.GetWarningTime(); } catch { }
            Raise(LightningWarning, s.transform.position, warn, "LightningWarning");
        }

        static void Raise(Action<Vector3, float> ev, Vector3 p, float v, string name)
        {
            if (ev == null) return;
            foreach (Action<Vector3, float> a in ev.GetInvocationList())
                try { a(p, v); } catch (Exception e) { ForgeLog.WarnOnce("weather:" + name + a.Method.Name, $"ForgeWeather.{name} handler failed: {e}"); }
        }

        static void TickStrikes()
        {
            for (int i = _strikes.Count - 1; i >= 0; i--)
            {
                var s = _strikes[i];
                if (s == null) { _strikes.RemoveAt(i); continue; }
                bool struck;
                try { struck = s._struck; } catch { _strikes.RemoveAt(i); continue; }
                if (!struck) continue;
                _strikes.RemoveAt(i);
                var p = s.transform.position;
                float r = 4f;
                try { r = s.BlastRadius; } catch { }
                Raise(Lightning, p, r, "Lightning");
                var sig = new SlimeSignal(SignalKind.Lightning, position: p, value: r);
                foreach (var a in World.ActorsNear(p, LightningSignalRadius, DietTools.IsSlime))
                    if (a != null) ActorRuntime.Signal(a.gameObject, sig);
            }
        }

        static void Poll()
        {
            if (Time.unscaledTime > _nextFind)
            {
                _nextFind = Time.unscaledTime + 10f;
                try { _directors = UnityEngine.Object.FindObjectsOfType<WeatherDirector>(); } catch { _directors = Array.Empty<WeatherDirector>(); }
                try { _wind = UnityEngine.Object.FindObjectOfType<GlobalWindControl>(); } catch { _wind = null; }
            }
            var before = Active;
            WeatherKind all = WeatherKind.None;
            _states.Clear();
            if (_wind != null)
            {
                try { WindIntensity = _wind.GetCurrentWindIntensity(); WindDirection = _wind.GetCurrentWindDirection(); } catch { }
                if (WindIntensity >= WindThreshold) all |= WeatherKind.Wind;
            }
            // Pass 1: states of every director. Active / Changed are updated before any reaction runs, so a failing
            // director or reaction can never freeze them (and reactions already see the new Active).
            var dirs = _directors;
            if (_kinds.Length < dirs.Length) _kinds = new WeatherKind[dirs.Length];
            for (int i = 0; i < dirs.Length; i++)
            {
                _kinds[i] = WeatherKind.None;
                var d = dirs[i];
                if (d == null) continue;
                var kinds = Kinds(d, out _);
                if (WindIntensity >= WindThreshold) kinds |= WeatherKind.Wind;
                _kinds[i] = kinds;
                all |= kinds;
            }
            Active = all;
            if (all != before && Changed != null)
                foreach (Action<WeatherKind, WeatherKind> a in Changed.GetInvocationList())
                    try { a(all, before); } catch (Exception e) { ForgeLog.WarnOnce("weather:changed" + a.Method.Name, $"ForgeWeather.Changed handler failed: {e}"); }

            // Pass 2: exposed slimes per director, each director isolated.
            bool react = _reactions.Count > 0 || ActorRuntime.Objects > 0;
            int exposed = -1, failed = 0;
            for (int i = 0; react && i < dirs.Length; i++)
            {
                var d = dirs[i];
                if (d == null || _kinds[i] == WeatherKind.None) continue;
                try { int n = ReactAll(d, _kinds[i]); exposed = Math.Max(exposed, 0) + n; }
                catch (Exception e) { failed++; ForgeLog.WarnOnce("weather:director", "ForgeWeather: weather director failed (retried every poll): " + e); }
            }
            Exposed = exposed;
            FailedDirectors = failed;
        }

        static WeatherKind[] _kinds = Array.Empty<WeatherKind>();
        static readonly List<IdentifiableActor> _exposedBuf = new();
        static readonly HashSet<IntPtr> _exposedSeen = new();

        static void AddExposed(IdentifiableActor a)
        {
            if (a == null || !_exposedSeen.Add(a.Pointer)) return;
            _exposedBuf.Add(a);
        }

        /// <summary>
        /// Exposed actors of a director copied into <see cref="_exposedBuf"/> before anything reacts. Reads the director's
        /// exposure caches by index (<c>_exposedActorsCache[*].ExposedActors</c>); only when they are empty falls back to the lazy
        /// <c>GetAffectedActors()</c>, whose enumerator can throw "Collection was modified" - then the actors read so far are kept.
        /// </summary>
        static void SnapshotExposed(WeatherDirector d)
        {
            _exposedBuf.Clear();
            _exposedSeen.Clear();
            try
            {
                var cache = d._exposedActorsCache;
                if (cache != null)
                {
                    var caches = new List<WeatherDirector.WeatherExposureCache>();
                    foreach (var kv in cache) if (kv.Value != null) caches.Add(kv.Value);
                    foreach (var c in caches)
                    {
                        var list = c.ExposedActors;
                        if (list == null) continue;
                        for (int i = 0; i < list.Count; i++) AddExposed(list[i]);
                    }
                }
            }
            catch (Exception x) { ForgeLog.WarnOnce("weather:cache", "ForgeWeather: exposure cache read failed: " + x.Message); }
            if (_exposedBuf.Count > 0) return;
            try
            {
                var e = d.GetAffectedActors();
                if (e == null) return;
                var en = e.GetEnumerator();
                var mv = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                for (int guard = 0; guard < 4096 && mv.MoveNext(); guard++) AddExposed(en.Current);
            }
            catch (Exception x) { ForgeLog.WarnOnce("weather:exposed", $"ForgeWeather: GetAffectedActors failed after {_exposedBuf.Count} actors (kept): {x.Message}"); }
        }

        static WeatherKind Kinds(WeatherDirector d, out string zone)
        {
            zone = "?";
            WeatherKind k = WeatherKind.None;
            try
            {
                zone = d.Zone != null ? d.Zone.name : d.gameObject.scene.name;
                var list = d._runningStates;
                if (list == null) return k;
                for (int i = 0; i < list.Count; i++)
                {
                    var st = list[i];
                    if (st == null) continue;
                    string name = null, asset = null;
                    try { name = st.GetName(); } catch { }
                    try { asset = st.TryCast<WeatherStateDefinition>()?.name; } catch { }
                    var kind = Classify(asset, name);
                    k |= kind;
                    _states.Add($"{zone}: {asset ?? name} ({kind})");
                }
            }
            catch (Exception e) { ForgeLog.WarnOnce("weather:states", "ForgeWeather: reading weather states failed: " + e.Message); }
            return k;
        }

        static int ReactAll(WeatherDirector d, WeatherKind kinds)
        {
            int n = 0;
            SnapshotExposed(d);
            if (_exposedBuf.Count == 0) return 0;
            var snapshot = _exposedBuf.ToArray();   // reactions may spawn / despawn actors and touch the game's lists
            var sig = new SlimeSignal(SignalKind.WeatherTick, weather: kinds, value: PollInterval);
            foreach (var a in snapshot)
            {
                if (a == null || a.gameObject == null) continue;
                var def = a.identType != null ? a.identType.TryCast<SlimeDefinition>() : null;
                if (def == null) continue;
                n++;
                var go = a.gameObject;
                ActorRuntime.Signal(go, sig);
                SlimeActor actor = null;
                foreach (var r in _reactions)
                {
                    if ((r.R.Kinds & kinds) == 0 || !SlimeEvents.Matches(def, r.Def, r.Largos)) continue;
                    actor ??= new SlimeActor(go);
                    try { r.R.React(actor, kinds, PollInterval); }
                    catch (Exception x) { ForgeLog.WarnOnce("weather:react" + r.R.GetType().Name, $"Weather reaction {r.R.GetType().Name} failed: {x}"); }
                }
            }
            return n;
        }

        internal sealed class Module : IForgeModule
        {
            public string Id => "weather";
            public void Init() { }
            public void Tick(float dt)
            {
                if (!World.InGame) return;
                if (_strikes.Count > 0) TickStrikes();
                if (Time.time < _nextPoll) return;
                _nextPoll = Time.time + PollInterval;
                Poll();
            }
        }
    }

    [HarmonyPatch(typeof(LightningStrike), nameof(LightningStrike.Start))]
    static class LightningStrikeStartPatch
    {
        static void Postfix(LightningStrike __instance) => Safe.Run("lightning", () => ForgeWeather.TrackStrike(__instance));
    }
}
