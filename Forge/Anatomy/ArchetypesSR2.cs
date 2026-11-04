using System;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace CottonLibrary.Forge
{
    // SR2 slime mechanics as reusable archetypes.

    /// <summary>Ringtail: turns to stone in direct sunlight (stops moving, stone look) and wakes up in shade or at night.</summary>
    public sealed class RingtailArchetype : Archetype<RingtailArchetype.Config>
    {
        public sealed class Config
        {
            /// <summary>Seconds of sunlight before petrifying (divided by intensity).</summary>
            public float Delay = 2f;
            public bool StopMovement = true;
            public Func<Material> StoneMaterial = () => MaterialPresets.Stone("ForgeStone", PresetColors.Solid(new Color(0.55f, 0.53f, 0.5f)));
            public string Fx;
        }

        Material _stone; bool _tried;
        internal Material StoneMat { get { if (!_tried) { _tried = true; _stone = Safe.Run("stone material", () => Settings.StoneMaterial?.Invoke()); } return _stone; } }

        public RingtailArchetype(Config c = null) : base(c) { }
        public override string Id => "ringtail";
        public override Origin Origin => Origin.SR2;
        public override string Summary => "petrifies in direct sunlight, wakes up in shade / at night";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i);

        /// <summary>True while the slime is stone.</summary>
        public static bool IsStone(GameObject slime) => ActorRuntime.Get<Runtime>(slime)?.Stone == true;

        sealed class Runtime : ArchetypeRuntime
        {
            readonly RingtailArchetype A;
            float _lit, _acc; RigidbodyConstraints _old;
            public bool Stone { get; private set; }
            public Runtime(RingtailArchetype a, SlimeActor s, float i) : base(a, s, i) { A = a; }

            public override void Tick(float dt)
            {
                var c = A.Settings;
                if (Slime.Held) { if (Stone) Wake(); _lit = 0f; return; }
                _acc += dt;
                if (!Ready("light", 0.25f)) return;
                float step = _acc; _acc = 0f;
                bool lit = ForgeWeather.InSunlight(Slime.Center);
                _lit = lit ? _lit + step : Mathf.Max(0f, _lit - step * 2f);
                if (!Stone && lit && _lit >= c.Delay / Mathf.Max(0.1f, Intensity) && Slime.Grounded) Petrify();
                else if (Stone && !lit) Wake();
            }

            void Petrify()
            {
                Stone = true;
                if (A.StoneMat != null) Swap.SetBody(Go, A.StoneMat);
                if (A.Settings.StopMovement)
                {
                    Movement.TakeOver(Go);
                    if (Rb != null)
                    {
                        _old = Rb.constraints;
                        if (!Rb.isKinematic) Rb.linearVelocity = Vector3.zero;
                        Rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
                    }
                }
                Slime.SetEmotion(SlimeEmotions.Emotion.AGITATION, 0f);
                if (!string.IsNullOrEmpty(A.Settings.Fx)) Fx.Play(A.Settings.Fx, Slime.Center, Slime.Scale);
            }

            void Wake()
            {
                Stone = false;
                Swap.RestoreAll();
                if (A.Settings.StopMovement)
                {
                    if (Go != null) Movement.Release(Go);
                    if (Rb != null) Rb.constraints = _old;
                }
            }

            protected override void OnStop() { if (Stone) Wake(); }
        }
    }

    /// <summary>Batty: energetic in the dark (extra hops), sluggish in sunlight.</summary>
    public sealed class BattyArchetype : Archetype<BattyArchetype.Config>
    {
        public sealed class Config
        {
            public float HopInterval = 1.6f;
            /// <summary>Velocity change of an extra hop (m/s).</summary>
            public float HopSpeed = 3f;
            public float DarkBoost = 1.6f;
            /// <summary>Horizontal velocity kept per 0.1 s in sunlight (1 = no slowdown).</summary>
            public float DayDrag = 0.93f;
        }

        public BattyArchetype(Config c = null) : base(c) { }
        public override string Id => "batty";
        public override Origin Origin => Origin.SR2;
        public override string Summary => "boosted in darkness (extra hops), sluggish in sunlight";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i);

        sealed class Runtime : ArchetypeRuntime
        {
            readonly BattyArchetype A;
            public Runtime(BattyArchetype a, SlimeActor s, float i) : base(a, s, i) { A = a; }

            public override void Tick(float dt)
            {
                var c = A.Settings;
                if (!Slime.Free || Rb == null || !Ready("batty", 0.1f)) return;
                if (ForgeWeather.InDarkness(Slime.Center))
                {
                    if (!Slime.Grounded || !Ready("hop", c.HopInterval / Mathf.Max(0.1f, Intensity))) return;
                    var f = T.forward; f.y = 0f;
                    if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
                    f = Quaternion.Euler(0f, World.Rand(-60f, 60f), 0f) * f.normalized;
                    Rb.AddForce((f * 0.8f + Vector3.up) * (c.HopSpeed * c.DarkBoost * Intensity), ForceMode.VelocityChange);
                }
                else if (ForgeWeather.InSunlight(Slime.Center))
                {
                    var v = Rb.linearVelocity;
                    float k = Mathf.Lerp(1f, c.DayDrag, Mathf.Clamp01(Intensity));
                    Rb.linearVelocity = new Vector3(v.x * k, v.y, v.z * k);
                }
            }
        }
    }

    /// <summary>Flutter: after eating nectar it hovers and bobs in the air for a while.</summary>
    public sealed class FlutterArchetype : Archetype<FlutterArchetype.Config>
    {
        public sealed class Config
        {
            public float HoverTime = 12f, Height = 2.2f, MaxHeight = 4f, BobAmplitude = 0.6f, BobSpeed = 1.6f;
            /// <summary>What counts as nectar (default: nectar group or "Nectar" in the name).</summary>
            public Func<IdentifiableType, bool> IsNectar = t => t != null &&
                ((t.name != null && t.name.IndexOf("Nectar", StringComparison.OrdinalIgnoreCase) >= 0) || (Library.nectar != null && Idents.InGroup(Library.nectar, t)));
            public string Fx;
        }

        public FlutterArchetype(Config c = null) : base(c) { }
        public override string Id => "flutter";
        public override Origin Origin => Origin.SR2;
        public override string Summary => "eating nectar makes it hover and bob in the air";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i);

        /// <summary>Makes a slime with the flutter archetype hover now (e.g. from your own trigger).</summary>
        public static void Hover(GameObject slime, float seconds) { var r = ActorRuntime.Get<Runtime>(slime); if (r != null) r.Until = Time.time + seconds; }

        sealed class Runtime : ArchetypeRuntime
        {
            readonly FlutterArchetype A;
            public float Until = -1f;
            public Runtime(FlutterArchetype a, SlimeActor s, float i) : base(a, s, i) { A = a; }

            public override void Tick(float dt)
            {
                var c = A.Settings;
                if (Time.time >= Until || !Slime.Free || Rb == null) return;
                float h = World.GroundBelow(T.position + Vector3.up * 0.2f, out var hit, 12f) ? T.position.y - hit.point.y : c.MaxHeight;
                float target = c.Height + Mathf.Sin(Time.time * c.BobSpeed) * c.BobAmplitude;
                float k = h > c.MaxHeight ? 0.5f : h < target ? 1.25f : 0.9f;
                Rb.AddForce(-Physics.gravity * k, ForceMode.Acceleration);
                var v = Rb.linearVelocity;
                Rb.linearVelocity = new Vector3(v.x * 0.98f, v.y * 0.96f, v.z * 0.98f);
            }

            public override void OnSignal(in SlimeSignal s)
            {
                if (s.Kind != SignalKind.Ate || !(A.Settings.IsNectar?.Invoke(s.Food) ?? false)) return;
                Until = Time.time + A.Settings.HoverTime * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(Intensity));
                if (!string.IsNullOrEmpty(A.Settings.Fx)) Fx.Play(A.Settings.Fx, Slime.Center, Slime.Scale);
            }
        }
    }

    /// <summary>Yolky: nests among chickens (coops) and lays its produce after a while without eating.</summary>
    public sealed class YolkyArchetype : Archetype<YolkyArchetype.Config>
    {
        public sealed class Config
        {
            public float NestRadius = 4f, NestTime = 90f, StillSpeed = 0.6f;
            public int MinHens = 2, Amount = 1;
            /// <summary>What it lays (null = its first produce, usually the plort).</summary>
            public IdentifiableType Produce;
            public string Fx = "FX Slime Spawn";
        }

        public YolkyArchetype(Config c = null) : base(c) { }
        public override string Id => "yolky";
        public override Origin Origin => Origin.SR2;
        public override string Summary => "nests among chickens (coops) and lays its produce";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i);

        sealed class Runtime : ArchetypeRuntime
        {
            readonly YolkyArchetype A;
            float _progress; int _hens;
            public Runtime(YolkyArchetype a, SlimeActor s, float i) : base(a, s, i) { A = a; }
            public float Progress01 => _progress / Mathf.Max(1f, A.Settings.NestTime);

            public override void Tick(float dt)
            {
                var c = A.Settings;
                if (Ready("hens", 2f)) _hens = World.ActorsNear(T.position, c.NestRadius * Slime.Scale, DietTools.IsMeat).Count;
                if (!Slime.Free || _hens < c.MinHens || Slime.Speed > c.StillSpeed) return;
                _progress += dt * Intensity * Mathf.Min(2f, _hens / (float)Mathf.Max(1, c.MinHens));
                if (_progress < c.NestTime) return;
                _progress = 0f;
                var id = c.Produce ?? DietTools.Produce(Slime.Def);
                if (id == null) return;
                for (int i = 0; i < c.Amount; i++)
                    World.Spawn(id, T.position + Vector3.up * (0.4f * Slime.Scale) + UnityEngine.Random.insideUnitSphere * 0.3f);
                if (!string.IsNullOrEmpty(c.Fx)) Fx.Play(c.Fx, T.position + Vector3.up * 0.4f, 0.6f);
            }
        }
    }
}
