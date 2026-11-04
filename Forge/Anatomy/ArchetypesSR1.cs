using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace CottonLibrary.Forge
{
    // SR1 slime mechanics as reusable archetypes. Numbers are starting points; every Config field can be tuned per instance.

    /// <summary>Quantum: when agitated leaves echoes (ghost copies) and teleports to them; phases through corral walls.</summary>
    public sealed class QuantumArchetype : Archetype<QuantumArchetype.Config>
    {
        public sealed class Config
        {
            public float Agitation = 0.45f;
            public float EchoInterval = 5f, EchoLifetime = 7f, EchoMinDist = 2.5f, EchoMaxDist = 7f;
            public int MaxEchoes = 3;
            /// <summary>Chance to jump to an echo when it collapses.</summary>
            public float TeleportChance = 0.6f;
            public bool PhaseWalls = true;
            public Func<Material> EchoMaterial = () => Mat.Clone("ForgeQuantumEcho", "RadiantAura_strong", "RadiantAura_light", "SphereLightAdditiveGradientRadiant");
            public string FxOut = "FX Despawn", FxIn = "FX Slime Spawn";
        }

        Material _echo; bool _echoTried;
        Material EchoMat { get { if (!_echoTried) { _echoTried = true; _echo = Safe.Run("quantum echo material", () => Settings.EchoMaterial?.Invoke()); } return _echo; } }

        public QuantumArchetype(Config c = null) : base(c) { }
        public override string Id => "quantum";
        public override Origin Origin => Origin.SR1;
        public override string Summary => "agitated: leaves echoes and teleports to them, phases through corral walls";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i);

        sealed class Runtime : ArchetypeRuntime
        {
            readonly QuantumArchetype A;
            readonly List<(GameObject go, float until)> _echoes = new();
            public Runtime(QuantumArchetype a, SlimeActor s, float i) : base(a, s, i) { A = a; }

            public override void Tick(float dt)
            {
                var c = A.Settings;
                bool agitated = Slime.Agitation >= c.Agitation;
                if (c.PhaseWalls) CorralWalls.Update(Go, agitated && Slime.Free);
                for (int i = _echoes.Count - 1; i >= 0; i--)
                    if (_echoes[i].go == null || Time.time > _echoes[i].until) Collapse(i, true);
                if (!agitated || !Slime.Free) return;
                if (_echoes.Count < c.MaxEchoes && Ready("echo", c.EchoInterval / Mathf.Max(0.1f, Intensity))) SpawnEcho();
            }

            void SpawnEcho()
            {
                var c = A.Settings;
                if (!World.GroundPoint(T.position, c.EchoMinDist, c.EchoMaxDist, out var p)) return;
                float s = Slime.Scale;
                var mat = A.EchoMat;
                var g = mat != null ? Gfx.Sphere("ForgeQuantumEcho", null, mat, p + Vector3.up * (0.5f * s), s) : new GameObject("ForgeQuantumEcho");
                if (mat == null) g.transform.position = p + Vector3.up * (0.5f * s);
                _echoes.Add((Own(g), Time.time + c.EchoLifetime));
            }

            void Collapse(int i, bool mayJump)
            {
                var (g, _) = _echoes[i];
                _echoes.RemoveAt(i);
                if (g == null) return;
                var pos = g.transform.position - Vector3.up * (0.5f * Slime.Scale);
                Release(g);
                var c = A.Settings;
                if (mayJump && Slime.Free && World.Chance(c.TeleportChance))
                {
                    World.Blink(Go, pos, c.FxOut, c.FxIn, Slime.Scale);
                    Slime.PreventCapture(0.4f);
                }
            }

            public override void OnSignal(in SlimeSignal s)
            {
                if (s.Kind != SignalKind.Watered) return;
                for (int i = _echoes.Count - 1; i >= 0; i--) Collapse(i, false);
                Slime.Emote(SlimeEmotions.Emotion.AGITATION, -0.3f);
            }

            protected override void OnStop()
            {
                if (Go != null) CorralWalls.Solid(Go, true);
                CorralWalls.Forget(Go);
            }
        }
    }

    /// <summary>Dervish: spins up a whirlwind that pulls and lifts nearby actors (agitation or wind).</summary>
    public sealed class DervishArchetype : Archetype<DervishArchetype.Config>
    {
        public sealed class Config
        {
            public float Agitation = 0.6f, Radius = 6f, Duration = 4f, Cooldown = 14f;
            /// <summary>Accelerations (m/s²) towards the centre, around it and upwards at the centre.</summary>
            public float Pull = 9f, Swirl = 14f, Lift = 6f;
            /// <summary>Own spin (rad/s).</summary>
            public float Spin = 18f;
            /// <summary>Strong wind (weather) starts whirlwinds too.</summary>
            public bool WindTriggers = true;
            public string Fx;
        }

        public DervishArchetype(Config c = null) : base(c) { }
        public override string Id => "dervish";
        public override Origin Origin => Origin.SR1;
        public override string Summary => "agitated or windy: whirlwind pulling and lifting nearby actors";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i);

        sealed class Runtime : ArchetypeRuntime
        {
            readonly DervishArchetype A;
            float _until = -1f, _windUntil = -1f;
            public Runtime(DervishArchetype a, SlimeActor s, float i) : base(a, s, i) { A = a; }

            public bool Active => Time.time < _until;

            public override void Tick(float dt)
            {
                var c = A.Settings;
                if (Active) { if (Slime.Free) Whirl(); return; }
                bool windy = c.WindTriggers && Time.time < _windUntil;
                if (!Slime.Free || !CooledDown("whirl") || (Slime.Agitation < c.Agitation && !windy)) return;
                _until = Time.time + c.Duration * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(Intensity));
                Cooldown("whirl", c.Cooldown);
                if (!string.IsNullOrEmpty(c.Fx)) Fx.Play(c.Fx, Slime.Center, Slime.Scale);
            }

            void Whirl()
            {
                var c = A.Settings;
                var center = Slime.Center;
                float r = c.Radius * Slime.Scale * Mathf.Sqrt(Mathf.Max(0.1f, Intensity));
                foreach (var actor in World.ActorsNear(center, r))
                {
                    if (actor == null || actor.gameObject == Go) continue;
                    var rb = actor.GetComponent<Rigidbody>();
                    if (rb == null || rb.isKinematic) continue;
                    var to = center - rb.position; to.y = 0f;
                    float d = to.magnitude;
                    if (d < 0.1f) continue;
                    var inward = to / d;
                    var tangent = Vector3.Cross(Vector3.up, inward);
                    float k = (1f - d / r) * Intensity;
                    rb.AddForce((inward * c.Pull + tangent * c.Swirl + Vector3.up * c.Lift) * k, ForceMode.Acceleration);
                }
                if (Rb != null && !Rb.isKinematic) Rb.angularVelocity = new Vector3(0f, c.Spin, 0f);
            }

            public override void OnSignal(in SlimeSignal s)
            {
                if (s.Kind == SignalKind.WeatherTick && (s.Weather & WeatherKind.Wind) != 0) _windUntil = Time.time + 3f;
                if (s.Kind == SignalKind.Watered) _until = -1f;
            }
        }
    }

    /// <summary>Tangle: shoots a vine at food it can eat and pulls it in.</summary>
    public sealed class TangleArchetype : Archetype<TangleArchetype.Config>
    {
        public sealed class Config
        {
            public float Radius = 7f, Interval = 3f, PullForce = 18f, MaxGrabTime = 4f;
            /// <summary>Only grabs when at least this hungry (0..1).</summary>
            public float Hunger = 0.3f;
            /// <summary>Food filter; default: the slime's diet.</summary>
            public Func<SlimeDefinition, IdentifiableType, bool> Edible = DietTools.Eats;
            public Func<Material> VineMaterial = () => Mat.Clone("ForgeTangleVine", "DefaultHDParticleMaterial");
            public Color VineColor = new(0.35f, 0.85f, 0.25f, 1f);
            public float VineWidth = 0.06f;
        }

        Material _vine; bool _vineTried;
        internal Material VineMat { get { if (!_vineTried) { _vineTried = true; _vine = Safe.Run("tangle vine material", () => Settings.VineMaterial?.Invoke()); } return _vine; } }

        public TangleArchetype(Config c = null) : base(c) { }
        public override string Id => "tangle";
        public override Origin Origin => Origin.SR1;
        public override string Summary => "hungry: vines grab edible food in range and pull it in";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i);

        sealed class Runtime : ArchetypeRuntime
        {
            readonly TangleArchetype A;
            GameObject _target; float _until; LineRenderer _line;
            public Runtime(TangleArchetype a, SlimeActor s, float i) : base(a, s, i) { A = a; }

            public override void Tick(float dt)
            {
                var c = A.Settings;
                if (_target != null || _line != null && _line.enabled)
                {
                    if (_target == null || !Slime.Free || Time.time > _until) { Drop(); return; }
                    Pull();
                    return;
                }
                if (!Slime.Free || Slime.Hunger < c.Hunger || !Ready("grab", c.Interval / Mathf.Max(0.1f, Intensity))) return;
                var center = Slime.Center;
                float best = float.MaxValue;
                foreach (var a in World.ActorsNear(center, c.Radius * Slime.Scale, t => c.Edible == null || c.Edible(Slime.Def, t)))
                {
                    if (a == null || a.gameObject == Go) continue;
                    var v = a.GetComponent<Vacuumable>();
                    if (v != null && v.IsHeld()) continue;
                    float d = (a.transform.position - center).sqrMagnitude;
                    if (d < best) { best = d; _target = a.gameObject; }
                }
                if (_target != null) _until = Time.time + c.MaxGrabTime;
            }

            void Pull()
            {
                var c = A.Settings;
                var rb = _target.GetComponent<Rigidbody>();
                var center = Slime.Center;
                var to = center - _target.transform.position;
                if (rb == null || to.magnitude < 0.9f * Slime.Scale) { Drop(); return; }
                if (!rb.isKinematic) rb.AddForce(to.normalized * (c.PullForce * Intensity) + Vector3.up * 2f, ForceMode.Acceleration);
                var line = Line();
                if (line == null) return;
                line.enabled = true;
                line.SetPosition(0, center);
                line.SetPosition(1, _target.transform.position);
            }

            LineRenderer Line()
            {
                if (_line != null) return _line;
                var mat = A.VineMat;
                if (mat == null) return null;
                var g = Own(new GameObject("ForgeTangleVine"));
                _line = g.AddComponent<LineRenderer>();
                _line.positionCount = 2;
                _line.widthMultiplier = A.Settings.VineWidth * Slime.Scale;
                _line.sharedMaterial = mat;
                _line.startColor = _line.endColor = A.Settings.VineColor;
                _line.useWorldSpace = true;
                return _line;
            }

            void Drop() { _target = null; if (_line != null) _line.enabled = false; }
        }
    }

    /// <summary>Mosaic: in sunlight sparkles with glints (optionally hot ones that hurt) and calms down.</summary>
    public sealed class MosaicArchetype : Archetype<MosaicArchetype.Config>
    {
        public sealed class Config
        {
            public float Interval = 2.5f, Lifetime = 1.6f, Size = 0.35f;
            public int MaxGlints = 4;
            public bool NeedsSun = true;
            /// <summary>Damage of a glint to the player within <see cref="GlintRadius"/> (0 = harmless).</summary>
            public int GlintDamage;
            public float GlintRadius = 1.2f;
            /// <summary>Agitation removed per second in sunlight.</summary>
            public float CalmInSun = 0.02f;
            public Func<Material> GlintMaterial = () => Mat.Clone("ForgeMosaicGlint", "fxDiamondGlint", "Gold Slime Sparkle");
        }

        Material _glint; bool _tried;
        internal Material GlintMat { get { if (!_tried) { _tried = true; _glint = Safe.Run("mosaic glint material", () => Settings.GlintMaterial?.Invoke()); } return _glint; } }

        public MosaicArchetype(Config c = null) : base(c) { }
        public override string Id => "mosaic";
        public override Origin Origin => Origin.SR1;
        public override string Summary => "sunlight: sparkling glints on the body (optionally hot), calmer";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i);

        sealed class Runtime : ArchetypeRuntime
        {
            readonly MosaicArchetype A;
            readonly List<(GameObject go, float born)> _glints = new();
            public Runtime(MosaicArchetype a, SlimeActor s, float i) : base(a, s, i) { A = a; }

            public override void Tick(float dt)
            {
                var c = A.Settings;
                for (int i = _glints.Count - 1; i >= 0; i--)
                {
                    var (g, born) = _glints[i];
                    float t = (Time.time - born) / c.Lifetime;
                    if (g == null || t >= 1f) { Release(g); _glints.RemoveAt(i); continue; }
                    g.transform.localScale = Vector3.one * (c.Size * Mathf.Sin(Mathf.PI * t));
                    if (c.GlintDamage > 0) Aura.Damage(g, g.transform.position, c.GlintRadius, c.GlintDamage, 1f);
                }
                bool sun = !c.NeedsSun || ForgeWeather.InSunlight(Slime.Center);
                if (!sun || Slime.Held) return;
                if (c.CalmInSun > 0f) Slime.Emote(SlimeEmotions.Emotion.AGITATION, -c.CalmInSun * dt * Intensity);
                var mat = A.GlintMat;
                if (mat == null || _glints.Count >= c.MaxGlints || !Ready("glint", c.Interval / Mathf.Max(0.1f, Intensity))) return;
                var dir = UnityEngine.Random.onUnitSphere; dir.y = Mathf.Abs(dir.y);
                var g2 = Gfx.Billboard("ForgeMosaicGlint", T, mat, Vector3.up * 0.5f + dir * 0.5f, 0.01f, 90f);
                _glints.Add((Own(g2), Time.time));
            }
        }
    }

    /// <summary>
    /// Glitch: flickers between looks, disguises itself as a nearby slime and infects slimes it touches (they flicker for
    /// a while and may pass it on for <see cref="Config.MaxGenerations"/> steps). Water cures infected slimes.
    /// </summary>
    public sealed class GlitchArchetype : Archetype<GlitchArchetype.Config>
    {
        public sealed class Config
        {
            public float InfectRadius = 1.6f, InfectChancePerSecond = 0.15f, InfectionTime = 25f;
            public int MaxGenerations = 2;
            public float FlickerInterval = 1.2f, FlickerTime = 0.18f;
            public bool Disguise = true;
            public float DisguiseInterval = 9f, DisguiseTime = 3f;
            public Func<Material> FlickerMaterial = () => MaterialPresets.Glitch("ForgeGlitchFlicker", PresetColors.Solid(new Color(0.2f, 1f, 0.9f)));
        }

        Material _flicker; bool _tried;
        internal Material FlickerMat { get { if (!_tried) { _tried = true; _flicker = Safe.Run("glitch material", () => Settings.FlickerMaterial?.Invoke()); } return _flicker; } }

        public GlitchArchetype(Config c = null) : base(c) { }
        public override string Id => "glitch";
        public override Origin Origin => Origin.SR1;
        public override string Summary => "flickers, disguises as nearby slimes, infects slimes on contact (water cures)";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i, 0, -1f);

        /// <summary>Infected slimes nearby (all generations).</summary>
        public static int Infected { get; private set; }

        sealed class Runtime : ArchetypeRuntime
        {
            readonly GlitchArchetype A;
            readonly int _gen; float _until, _swapEnd = -1f;
            public Runtime(GlitchArchetype a, SlimeActor s, float i, int generation, float until) : base(a, s, i)
            { A = a; _gen = generation; _until = until; }

            protected override void OnStart() { if (_gen > 0) Infected++; }

            public override void Tick(float dt)
            {
                var c = A.Settings;
                if (_until > 0f && Time.time > _until) { ActorRuntime.Remove(Go, this); return; }
                if (_swapEnd > 0f && Time.time > _swapEnd) { Swap.RestoreAll(); _swapEnd = -1f; }
                if (_swapEnd < 0f && !Slime.Held)
                {
                    if (_gen == 0 && c.Disguise && Ready("disguise", c.DisguiseInterval) && DisguiseMat() is Material m) { Swap.SetBody(Go, m); _swapEnd = Time.time + c.DisguiseTime; }
                    else if (A.FlickerMat != null && Ready("flicker", c.FlickerInterval * World.Rand(0.6f, 1.4f))) { Swap.SetBody(Go, A.FlickerMat); _swapEnd = Time.time + c.FlickerTime; }
                }
                if (_gen >= c.MaxGenerations || !Ready("spread", 1f)) return;
                foreach (var a in World.ActorsNear(Slime.Center, c.InfectRadius * Slime.Scale, DietTools.IsSlime))
                {
                    var other = a != null ? a.gameObject : null;
                    if (other == null || other == Go || ActorRuntime.Get<Runtime>(other) != null) continue;
                    if (!World.Chance(c.InfectChancePerSecond * Intensity)) continue;
                    ActorRuntime.Add(other, new Runtime(A, new SlimeActor(other), Intensity * 0.8f, _gen + 1, Time.time + c.InfectionTime));
                }
            }

            Material DisguiseMat()
            {
                foreach (var a in World.ActorsNear(Slime.Center, 10f * Slime.Scale, DietTools.IsSlime))
                {
                    if (a == null || a.gameObject == Go || ActorRuntime.Get<Runtime>(a.gameObject) != null) continue;
                    var r = MeshKit.BodyRenderer(a.gameObject);
                    if (r != null && r.sharedMaterial != null) return r.sharedMaterial;
                }
                return null;
            }

            public override void OnSignal(in SlimeSignal s)
            {
                if (s.Kind != SignalKind.Watered) return;
                if (_gen > 0) ActorRuntime.Remove(Go, this);
                else { Swap.RestoreAll(); _swapEnd = -1f; Cooldown("flicker", 4f); }
            }

            protected override void OnStop() { if (_gen > 0) Infected--; }
        }
    }

    /// <summary>Rad: irradiates the player nearby through SR2's rad absorber (rad meter, rad damage), optional aura sphere.</summary>
    public sealed class RadArchetype : Archetype<RadArchetype.Config>
    {
        public sealed class Config
        {
            public float Radius = 4f, RadsPerSecond = 6f;
            public int FallbackDamage = 1;
            /// <summary>Optional aura sphere material (null = no sphere).</summary>
            public Func<Material> AuraMaterial;
            public float AuraScale = 2.2f;
        }

        public RadArchetype(Config c = null) : base(c) { }
        public override string Id => "rad";
        public override Origin Origin => Origin.SR1;
        public override string Summary => "irradiates the player within its aura (SR2 rad meter and damage)";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i);

        sealed class Runtime : ArchetypeRuntime
        {
            readonly RadArchetype A;
            public Runtime(RadArchetype a, SlimeActor s, float i) : base(a, s, i) { A = a; }

            protected override void OnStart()
            {
                var mat = Safe.Run("rad aura material", () => A.Settings.AuraMaterial?.Invoke());
                if (mat != null) Own(Gfx.Sphere("ForgeRadAura", T, mat, Vector3.up * 0.5f, A.Settings.AuraScale));
            }

            public override void Tick(float dt)
            {
                var c = A.Settings;
                Aura.Irradiate(Go, Slime.Center, c.Radius * Slime.Scale * Mathf.Sqrt(Mathf.Max(0.1f, Intensity)), c.RadsPerSecond * Intensity, dt, c.FallbackDamage);
            }
        }
    }

    /// <summary>Quicksilver: electric sprints (towards food or away), shocks the player on contact; storms charge it, water discharges it.</summary>
    public sealed class QuicksilverArchetype : Archetype<QuicksilverArchetype.Config>
    {
        public sealed class Config
        {
            public float Agitation = 0.5f, SprintSpeed = 14f, SprintTime = 1.2f, Cooldown = 6f;
            public float ShockRadius = 1.8f, ShockInterval = 1f;
            public int ShockDamage = 8;
            /// <summary>Speed multiplier during thunderstorms / after a lightning strike nearby.</summary>
            public float StormBoost = 1.5f;
            public bool SeekFood = true;
            public float SeekRadius = 12f;
            public string SparkFx = "FX Hyper Slime Contact", ChargeFx = "FX HyperSlimeCharge";
        }

        public QuicksilverArchetype(Config c = null) : base(c) { }
        public override string Id => "quicksilver";
        public override Origin Origin => Origin.SR1;
        public override string Summary => "electric sprints, shocks on contact; thunder charges, water discharges";
        public override IArchetypeRuntime Create(SlimeActor s, float i) => new Runtime(this, s, i);

        sealed class Runtime : ArchetypeRuntime
        {
            readonly QuicksilverArchetype A;
            float _until = -1f, _stormUntil = -1f; Vector3 _dir;
            public Runtime(QuicksilverArchetype a, SlimeActor s, float i) : base(a, s, i) { A = a; }

            bool Sprinting => Time.time < _until;
            float Boost => Time.time < _stormUntil ? A.Settings.StormBoost : 1f;

            public override void Tick(float dt)
            {
                var c = A.Settings;
                if (Sprinting)
                {
                    if (!Slime.Free || Rb == null) { _until = -1f; return; }
                    var v = _dir * (c.SprintSpeed * Boost * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(Intensity)));
                    v.y = Rb.linearVelocity.y;
                    Rb.linearVelocity = v;
                    if (!string.IsNullOrEmpty(c.SparkFx) && Ready("spark", 0.15f)) Fx.Play(c.SparkFx, Slime.Center, 0.5f * Slime.Scale);
                    if (c.ShockDamage > 0) Aura.Damage(Go, Slime.Center, c.ShockRadius * Slime.Scale, c.ShockDamage, c.ShockInterval);
                    return;
                }
                if (!Slime.Free || !Slime.Grounded || !CooledDown("sprint")) return;
                bool hungry = c.SeekFood && Slime.Hunger > 0.5f;
                if (Slime.Agitation < c.Agitation && !hungry) return;
                Vector3 dir = default;
                if (hungry)
                {
                    float best = float.MaxValue;
                    foreach (var a in World.ActorsNear(Slime.Center, c.SeekRadius, t => DietTools.Eats(Slime.Def, t)))
                    {
                        if (a == null) continue;
                        var d = a.transform.position - T.position;
                        if (d.sqrMagnitude < best) { best = d.sqrMagnitude; dir = d; }
                    }
                }
                if (dir == default) dir = Quaternion.Euler(0f, World.Rand(0f, 360f), 0f) * Vector3.forward;
                dir.y = 0f;
                _dir = dir.normalized;
                _until = Time.time + c.SprintTime;
                Cooldown("sprint", c.Cooldown / Mathf.Max(0.25f, Intensity));
                Slime.PreventCapture(c.SprintTime);
                if (!string.IsNullOrEmpty(c.ChargeFx)) Fx.Play(c.ChargeFx, Slime.Center, 0.6f * Slime.Scale);
            }

            public override void OnSignal(in SlimeSignal s)
            {
                var c = A.Settings;
                switch (s.Kind)
                {
                    case SignalKind.Watered:
                        if (Sprinting && !string.IsNullOrEmpty(c.ChargeFx)) Fx.Play(c.ChargeFx, Slime.Center, 0.8f * Slime.Scale);
                        _until = -1f; _stormUntil = -1f; Cooldown("sprint", c.Cooldown * 1.5f);
                        break;
                    case SignalKind.WeatherTick when (s.Weather & WeatherKind.Thunder) != 0:
                        _stormUntil = Mathf.Max(_stormUntil, Time.time + 3f);
                        break;
                    case SignalKind.Lightning when (s.Position - T.position).sqrMagnitude < 15f * 15f:
                        _stormUntil = Time.time + 20f; Cooldown("sprint", 0f);
                        break;
                }
            }
        }
    }
}
