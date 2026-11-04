using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace CottonLibrary.Forge
{
    // The public contracts of Forge's engine layer. Every subsystem (anatomy, weather, science, economy, visuals, player)
    // is a static facade over a module that implements IForgeModule; mods plug in by implementing the interfaces below
    // and registering instances. Base implementations live next to each facade.

    /// <summary>Where a mechanic comes from.</summary>
    public enum Origin { SR1, SR2, Mod }

    /// <summary>
    /// A subsystem driven by <see cref="ForgeModules"/>: <see cref="Init"/> once (main menu), <see cref="Tick"/> every frame
    /// while enabled. Disable modules by id in the <c>DisabledModules</c> setting.
    /// </summary>
    public interface IForgeModule
    {
        string Id { get; }
        void Init();
        void Tick(float dt);
    }

    /// <summary>
    /// Managed logic bound to one GameObject and driven by <see cref="ActorRuntime"/> (no IL2CPP type injection needed).
    /// <see cref="Start"/> runs on the first tick after attaching, <see cref="Stop"/> when detached or the object is destroyed.
    /// </summary>
    public interface IActorLogic
    {
        void Start(GameObject go);
        void Tick(float dt);
        void Stop();
    }

    // ------------------------------------------------------------------ anatomy

    /// <summary>What happened to a slime (forwarded to its archetype runtimes).</summary>
    public enum SignalKind { Ate, Watered, WeatherTick, Lightning, Spawned }

    /// <summary>An event delivered to <see cref="IArchetypeRuntime.OnSignal"/>.</summary>
    public readonly struct SlimeSignal
    {
        public readonly SignalKind Kind;
        /// <summary>Food for <see cref="SignalKind.Ate"/>.</summary>
        public readonly IdentifiableType Food;
        /// <summary>Active weather for <see cref="SignalKind.WeatherTick"/>.</summary>
        public readonly WeatherKind Weather;
        /// <summary>Strike position for <see cref="SignalKind.Lightning"/>.</summary>
        public readonly Vector3 Position;
        /// <summary>Weather intensity / seconds since the last weather tick.</summary>
        public readonly float Value;

        public SlimeSignal(SignalKind kind, IdentifiableType food = null, WeatherKind weather = WeatherKind.None, Vector3 position = default, float value = 0f)
        { Kind = kind; Food = food; Weather = weather; Position = position; Value = value; }
    }

    /// <summary>
    /// A reusable behaviour module ("phases like Quantum", "petrifies in light like Ringtail"). Assigned to slime
    /// definitions through <see cref="AnatomySpec.Archetype(IArchetype, float)"/>; largos inherit the archetypes of both bases.
    /// </summary>
    public interface IArchetype
    {
        string Id { get; }
        Origin Origin { get; }
        string Summary { get; }
        IArchetypeRuntime Create(SlimeActor slime, float intensity);
    }

    /// <summary>The per-slime instance of an archetype.</summary>
    public interface IArchetypeRuntime : IActorLogic
    {
        IArchetype Archetype { get; }
        /// <summary>1 = full strength; largos get a reduced share of each base (see <see cref="LargoEngine.LargoIntensity"/>).</summary>
        float Intensity { get; set; }
        void OnSignal(in SlimeSignal s);
    }

    /// <summary>
    /// One step of the largo pipeline: combines the anatomy of the base slimes into the largo's anatomy. The default
    /// mergers handle layers, parts, archetypes and voices; add your own with <see cref="LargoEngine.AddMerger"/>.
    /// </summary>
    public interface ILargoMerger
    {
        string Id { get; }
        void Merge(SlimeDefinition largo, IReadOnlyList<AnatomySpec> bases, AnatomySpec result);
    }

    // ------------------------------------------------------------------ environment

    /// <summary>Weather phenomena (flags: a thunderstorm is usually Rain | Thunder | Wind).</summary>
    [Flags]
    public enum WeatherKind
    {
        None = 0, Rain = 1, Thunder = 2, Pollen = 4, Wind = 8, SlimeRain = 16, Snow = 32, Fog = 64, Heat = 128, Other = 256,
        Any = Rain | Thunder | Pollen | Wind | SlimeRain | Snow | Fog | Heat | Other
    }

    /// <summary>Maps SR2 weather pattern/state names to <see cref="WeatherKind"/> (register with <see cref="ForgeWeather.AddClassifier"/>).</summary>
    public interface IWeatherClassifier
    {
        WeatherKind Classify(string pattern, string state);
    }

    /// <summary>Reaction of slimes to weather; ticked about once per second for every exposed (outdoor) matching slime.</summary>
    public interface IWeatherReaction
    {
        WeatherKind Kinds { get; }
        void React(SlimeActor slime, WeatherKind active, float dt);
    }

    // ------------------------------------------------------------------ science

    /// <summary>Logic started on every placed gadget of a definition (see <see cref="GadgetKit.Logic(string, Func{GameObject, IGadgetLogic})"/>).</summary>
    public interface IGadgetLogic : IActorLogic { }

    /// <summary>
    /// What a <see cref="HelperDrone"/> does: find cargo near its home, then deliver it. Built-ins in <see cref="DronePrograms"/>.
    /// </summary>
    public interface IDroneProgram
    {
        string Id { get; }
        /// <summary>Next actor to pick up (null = nothing to do).</summary>
        GameObject FindCargo(HelperDrone drone);
        /// <summary>Where to fly with the cargo.</summary>
        Vector3 Destination(HelperDrone drone, GameObject cargo);
        /// <summary>Called at the destination; return true when the cargo was consumed (sold, stored, fed).</summary>
        bool Deliver(HelperDrone drone, GameObject cargo);
    }

    // ------------------------------------------------------------------ economy

    /// <summary>Inputs of a price model (prices are in newbucks per plort).</summary>
    public readonly struct PriceContext
    {
        public readonly IdentifiableType Plort;
        public readonly float BaseValue, FullSaturation, Day;
        /// <summary>The price SR2 would set (saturation and noise included).</summary>
        public readonly float GamePrice;
        /// <summary>Plorts of this type sold recently (decays by half every in-game day).</summary>
        public readonly float RecentlySold;

        public PriceContext(IdentifiableType plort, float baseValue, float fullSaturation, float day, float gamePrice, float recentlySold)
        { Plort = plort; BaseValue = baseValue; FullSaturation = fullSaturation; Day = day; GamePrice = gamePrice; RecentlySold = recentlySold; }
    }

    /// <summary>Daily target price of a plort (see <see cref="PriceModels"/>; combine with <see cref="PriceModels.Chain"/>).</summary>
    public interface IPriceModel
    {
        float Price(in PriceContext c);
    }

    // ------------------------------------------------------------------ visuals

    /// <summary>Colours handed to a material preset.</summary>
    public struct PresetColors
    {
        public Color Top, Middle, Bottom, Glow;
        public PresetColors(Color top, Color middle, Color bottom, Color? glow = null) { Top = top; Middle = middle; Bottom = bottom; Glow = glow ?? middle; }
        public static PresetColors Solid(Color c) => new(c, c, c, c);
    }

    /// <summary>A high-level material recipe for SR2's shaders ("jelly", "glass", "neon"...). See <see cref="MaterialPresets"/>.</summary>
    public interface IMaterialPreset
    {
        string Id { get; }
        Material Build(string name, PresetColors colors);
    }

    // ------------------------------------------------------------------ player

    /// <summary>What was shot (for <see cref="IFireMode.OnShot"/>).</summary>
    public sealed class ShotContext
    {
        public GameObject Projectile;
        public IdentifiableType Ammo;
        public Vector3 Origin, Direction;
        /// <summary>Shoots one more item of the same ammo (when the slot still has it). Null when it can't.</summary>
        public Func<GameObject> ShootAgain;
    }

    /// <summary>Alternative vacpack fire mode (cycled with the fire-mode key; see <see cref="FireModes"/>).</summary>
    public interface IFireMode
    {
        string Id { get; }
        string Label { get; }
        bool Accepts(IdentifiableType ammo);
        void OnShot(ShotContext shot);
    }

    /// <summary>Screen position of a HUD widget.</summary>
    public enum HudAnchor { TopLeft, TopCenter, TopRight, MiddleLeft, MiddleRight, BottomLeft, BottomCenter, BottomRight }

    /// <summary>A modular HUD element (see <see cref="ForgeHud"/>, built-ins <see cref="HudBar"/>, <see cref="HudCounter"/>, <see cref="HudText"/>).</summary>
    public interface IHudWidget
    {
        string Id { get; }
        HudAnchor Anchor { get; }
        bool Visible { get; }
        /// <summary>Creates the widget's UI under <paramref name="parent"/> (called again after the HUD is rebuilt).</summary>
        void Build(RectTransform parent, HudStyle style);
        void Refresh(float dt);
    }
}
