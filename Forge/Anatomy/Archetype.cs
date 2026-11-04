using System;
using System.Collections.Generic;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Base class of archetypes with a settings object: <c>new QuantumArchetype(new QuantumArchetype.Config { EchoLifetime = 6 })</c>.
    /// The catalog instance (<see cref="Archetypes.Get"/>) uses default settings, which can also be edited in place.
    /// </summary>
    public abstract class Archetype<TConfig> : IArchetype where TConfig : class, new()
    {
        /// <summary>Tuning shared by every slime using this instance.</summary>
        public TConfig Settings { get; }
        protected Archetype(TConfig settings) { Settings = settings ?? new TConfig(); }
        public abstract string Id { get; }
        public abstract Origin Origin { get; }
        public abstract string Summary { get; }
        public abstract IArchetypeRuntime Create(SlimeActor slime, float intensity);
        public override string ToString() => $"{Id} ({Origin}): {Summary}";
    }

    /// <summary>
    /// Base of a per-slime archetype instance: cooldown helpers, owned temporary objects (destroyed on <see cref="Stop"/>),
    /// a material swapper and the slime wrapper. Override <see cref="Tick"/> and, when needed, <see cref="OnStart"/>,
    /// <see cref="OnStop"/>, <see cref="OnSignal"/>.
    /// </summary>
    public abstract class ArchetypeRuntime : IArchetypeRuntime
    {
        readonly Dictionary<string, float> _ready = new();
        readonly List<GameObject> _owned = new();
        MaterialSwap _swap;

        protected ArchetypeRuntime(IArchetype archetype, SlimeActor slime, float intensity)
        { Archetype = archetype; Slime = slime; Intensity = intensity; }

        public IArchetype Archetype { get; }
        public SlimeActor Slime { get; }
        public float Intensity { get; set; }
        protected GameObject Go => Slime.Go;
        protected Transform T => Slime.Go.transform;
        protected Rigidbody Rb => Slime.Rb;
        /// <summary>Material swapper of this runtime (restored on stop).</summary>
        protected MaterialSwap Swap => _swap ??= new MaterialSwap();

        /// <summary>True once per <paramref name="seconds"/> for <paramref name="key"/> (the first call arms the timer and returns false).</summary>
        protected bool Ready(string key, float seconds)
        {
            float now = Time.time;
            if (!_ready.TryGetValue(key, out var at)) { _ready[key] = now + seconds; return false; }
            if (now < at) return false;
            _ready[key] = now + seconds;
            return true;
        }

        /// <summary>True when <paramref name="key"/>'s cooldown is over (without re-arming).</summary>
        protected bool CooledDown(string key) => !_ready.TryGetValue(key, out var at) || Time.time >= at;
        /// <summary>Starts a cooldown.</summary>
        protected void Cooldown(string key, float seconds) => _ready[key] = Time.time + seconds;

        /// <summary>Remembers a temporary object so it is destroyed with the runtime.</summary>
        protected GameObject Own(GameObject go) { if (go != null) _owned.Add(go); return go; }
        protected void Release(GameObject go) { if (go == null) return; _owned.Remove(go); UnityEngine.Object.Destroy(go); }

        public void Start(GameObject go) => OnStart();
        public void Stop()
        {
            try { OnStop(); }
            finally
            {
                foreach (var g in _owned) if (g != null) UnityEngine.Object.Destroy(g);
                _owned.Clear();
                _swap?.RestoreAll();
            }
        }

        protected virtual void OnStart() { }
        protected virtual void OnStop() { }
        public abstract void Tick(float dt);
        public virtual void OnSignal(in SlimeSignal s) { }
    }

    /// <summary>
    /// Catalog of archetypes by id. Built-ins: SR1 <c>quantum, dervish, tangle, mosaic, glitch, rad, quicksilver</c>;
    /// SR2 <c>ringtail, batty, flutter, yolky</c>. Register your own with <see cref="Register"/>.
    /// </summary>
    public static class Archetypes
    {
        static readonly Dictionary<string, IArchetype> _byId = new(StringComparer.OrdinalIgnoreCase);
        static bool _builtins;

        public static void Register(IArchetype a)
        {
            if (a == null) return;
            EnsureBuiltins();
            if (_byId.ContainsKey(a.Id)) ForgeLog.Debug($"Archetype '{a.Id}' replaced by {a.GetType().FullName}");
            _byId[a.Id] = a;
        }

        public static IArchetype Get(string id) { EnsureBuiltins(); return id != null && _byId.TryGetValue(id, out var a) ? a : null; }
        public static T Get<T>() where T : class, IArchetype { EnsureBuiltins(); foreach (var a in _byId.Values) if (a is T t) return t; return null; }
        public static IEnumerable<IArchetype> All { get { EnsureBuiltins(); return _byId.Values; } }

        static void EnsureBuiltins()
        {
            if (_builtins) return;
            _builtins = true;
            foreach (var a in new IArchetype[]
            {
                new QuantumArchetype(), new DervishArchetype(), new TangleArchetype(), new MosaicArchetype(), new GlitchArchetype(),
                new RadArchetype(), new QuicksilverArchetype(), new RingtailArchetype(), new BattyArchetype(), new FlutterArchetype(), new YolkyArchetype()
            }) _byId[a.Id] = a;
        }
    }
}
