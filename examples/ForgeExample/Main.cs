using System;
using System.Linq;
using CottonLibrary;
using MelonLoader;
using CottonLibrary.Forge;
using UnityEngine;

[assembly: MelonInfo(typeof(ForgeExample.ForgeExampleMod), "ForgeExample", "0.1.0", "slitvin12")]
[assembly: MelonGame("MonomiPark", "SlimeRancher2")]
[assembly: MelonAdditionalDependencies("CottonLibrary")]

namespace ForgeExample
{
    /// <summary>
    /// Example content mod: one new slime ("Sunny") with plort, largos, wild spawns, custom behaviour,
    /// config entries and a dev command. Texts are in Assets/localized.csv (en + ru).
    /// </summary>
    public class ForgeExampleMod : ForgeMod<ForgeExampleMod>
    {
        MelonPreferences_Entry<bool> _sunnyOn;
        MelonPreferences_Entry<float> _sunnyShare;

        public override void OnInitializeMelon()
        {
            base.OnInitializeMelon();
            var cat = MelonPreferences.CreateCategory("ForgeExample");
            _sunnyOn = cat.CreateEntry("SunnyEnabled", true, description: "Add the Sunny slime (needs a restart)");
            _sunnyShare = cat.CreateEntry("SunnySpawnShare", 0.05f, description: "Share of Sunny slimes among wild spawns in Rainbow Fields (0..1)");

            // UserData\CottonLibrary\cmd.txt: "sunny 3" (DevConsole = true in the [CottonLibrary] config)
            DevConsole.Register("sunny", "sunny [n] - spawn n Sunny slimes in front of the camera", a =>
            {
                var def = GetSlime("Sunny")?.Def;
                if (def == null || !World.InGame) { DevConsole.Print("Sunny is not available (disabled or not in game)"); return; }
                int n = (int)DevConsole.F(a, 1, 1);
                for (int i = 0; i < n; i++) World.Spawn(def, World.InFrontOfCamera(3f + i));
                DevConsole.Print($"spawned {n} Sunny");
            });
        }

        protected override void DeclareContent()
        {
            // name "Sunny" -> refs SlimeDefinition.Sunny / IdentifiableType.SunnyPlort,
            // texts l.sunny_slime, l.sunny_plort, m.*.sunny_slime; icons iconSlimeSunny.png / iconPlortSunny.png
            Slime("Sunny")
                .Colors("#FFE45C", "#FFC21A", "#E08A00")
                .Eats(FoodGroups.Fruits)
                .Favorite("Pogo")
                .Toys("BeachBall", "Ball")
                .Plort(25, 10)
                .Largo()
                .Spawn(Library.SpawnLocations.RainbowFields, _sunnyShare)
                .Enabled(_sunnyOn)
                .Customize(b => b.Body.Flt("_GlossPower", 0.6f))
                .Behaviour<SunnyHop>()
                // golden sparkles around every Sunny (and its largos): a copy of SR2's gold slime emitter
                .OnSpawn(go => Gfx.Sparkles("SunnySparkles", go.transform, new Vector3(0f, 0.5f, 0f), null,
                    new Color(1f, 0.9f, 0.4f), new Color(1f, 0.7f, 0.2f), rate: 4f, life: 1.2f, size0: 0.05f, size1: 0.12f, radius: 0.6f, max: 20));
        }

        protected override void OnContentCreated()
        {
            var b = GetSlime("Sunny");
            // gameplay events without Harmony: Sunny jumps when sprayed with water and logs what it eats
            if (b?.Def != null)
            {
                SlimeEvents.OnWater(b.Def, go =>
                {
                    var rb = go.GetComponent<Rigidbody>();
                    if (rb != null && World.Grounded(go.transform, go.transform.lossyScale.x)) rb.AddForce(Vector3.up * 4f, ForceMode.VelocityChange);
                });
                SlimeEvents.OnEat(b.Def, (go, food) => LoggerInstance.Msg($"Sunny ate {food?.name}"), largos: false);
            }
            if (b?.Def != null) LoggerInstance.Msg($"Sunny ready, favorite: {b.Def.Diet?.FavoriteIdents?.FirstOrDefault()?.name ?? "none"}");
            if (b?.Def != null) UseEngine(b);
        }

        // Engine layer (Forge/Architecture, docs/ARCHITECTURE.md): archetypes and layers that largos inherit, weather
        // reactions, a price model for the plort and a HUD counter.
        static void UseEngine(SlimeBuilder b)
        {
            Anatomy.For(b.Def)
                .Archetype("flutter", 0.6f)   // hovers for a while after eating nectar (SR2 Flutter behaviour)
                .Layer("SunnyGlaze", () => MaterialPresets.Glass("SunnyGlaze", new PresetColors(new Color(1f, 0.95f, 0.6f), new Color(1f, 0.85f, 0.3f), new Color(1f, 0.7f, 0.1f))), 0.03f);
            // Sunny jumps for joy in the rain (outdoors only; ticked about once per second)
            ForgeWeather.React(b.Def, WeatherKind.Rain, (s, kind, dt) =>
            {
                if (s.Rb != null && !s.Rb.isKinematic && World.Chance(0.15f * dt)) s.Rb.AddForce(Vector3.up * 5f, ForceMode.VelocityChange);
            });
            if (b.PlortType != null)
            {
                // SR2 price -> slow wave of +-20 % -> +25 % while it rains -> extra drop after 40 sold
                PlortMarket.SetModel(b.PlortType, PriceModels.Chain(PriceModels.Volatile(0.2f, 4f), PriceModels.Weather(WeatherKind.Rain, 1.25f), PriceModels.Glut(40f, 0.4f)));
                var plort = b.PlortType;
                ForgeHud.Add(new HudCounter("sunny.price", HudAnchor.TopRight, () => { int p = PlortMarket.Price(plort); return p > 0 ? $"Sunny plort: {p}" : null; }));
            }
        }

        protected override void OnFirstGameLoaded()
        {
            var b = GetSlime("Sunny");
            if (b?.Def != null) LoggerInstance.Msg($"Sunny largos: {b.Largos.Count()}");
        }
    }

    /// <summary>Sunny slimes hop around happily during the day.</summary>
    public class SunnyHop : MonoBehaviour
    {
        public SunnyHop(IntPtr ptr) : base(ptr) { }

        Rigidbody _rb;
        float _next;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _next = Time.time + World.Rand(3f, 8f);
        }

        void Update()
        {
            if (_rb == null || Time.time < _next) return;
            _next = Time.time + World.Rand(4f, 10f);
            if (_rb.isKinematic || !World.IsDay() || !World.Grounded(transform, transform.localScale.x)) return;
            float a = World.Rand(0f, Mathf.PI * 2f);
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            _rb.AddForce(dir * 2f + Vector3.up * 6f, ForceMode.VelocityChange);
        }
    }
}
