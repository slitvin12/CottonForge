using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// SR1-style helper drone (self-contained; not part of SR2's own drone network): hovers at its home, looks for cargo
    /// with its <see cref="IDroneProgram"/>, flies there, carries it to the program's destination and delivers it.
    /// Has a battery that drains while flying and recharges at home. Flight is direct (it hovers over terrain but does not
    /// path-find around walls), so keep the work radius to open areas.
    /// <code>HelperDrone.Spawn(DronePrograms.SellPlorts(20f), stationTransform);</code>
    /// </summary>
    public sealed class HelperDrone : IActorLogic
    {
        public enum State { Idle, Fetch, Carry, Return, Charging }

        static readonly HashSet<IntPtr> _claimed = new();
        static readonly List<HelperDrone> _all = new();
        /// <summary>Live drones.</summary>
        public static IReadOnlyList<HelperDrone> All => _all;

        public IDroneProgram Program;
        /// <summary>Home transform (station); null = fixed <see cref="HomePoint"/>.</summary>
        public Transform Home { get; private set; }
        public float Speed = 7f, HoverHeight = 2.2f, ScanInterval = 1.5f, Range = 25f;
        /// <summary>Battery 0..1; drains <see cref="DrainPerSecond"/> while flying, charges <see cref="ChargePerSecond"/> at home.</summary>
        public float Battery = 1f, DrainPerSecond = 1f / 300f, ChargePerSecond = 1f / 40f;
        public GameObject Go { get; private set; }
        public State Current { get; private set; }
        public GameObject Cargo { get; private set; }
        public int Delivered { get; private set; }
        /// <summary>Raised after a delivery (drone, cargo, consumed).</summary>
        public event Action<HelperDrone, GameObject, bool> OnDelivered;

        Vector3 _homePos;
        float _scan, _stuck;
        bool _cargoKinematic;
        Vector3 _lastPos;

        public Vector3 HomePoint => (Home != null ? Home.position : _homePos) + Vector3.up * HoverHeight;

        /// <summary>
        /// Creates a drone at <paramref name="home"/> (or <paramref name="homePos"/>, default the player). <paramref name="visual"/>
        /// builds its look (default: small metal orb with a neon eye).
        /// </summary>
        public static HelperDrone Spawn(IDroneProgram program, Transform home, Func<GameObject> visual = null, Vector3? homePos = null)
        {
            if (program == null) return null;
            var d = new HelperDrone { Program = program, Home = home, _homePos = home != null ? home.position : homePos ?? World.PlayerPos };
            GameObject go = null;
            try { go = visual?.Invoke(); } catch (Exception e) { ForgeLog.Warn($"[drones] visual: {e.Message}"); }
            go ??= DefaultVisual();
            go.transform.position = d.HomePoint;
            ActorRuntime.Add(go, d);
            return d;
        }

        /// <summary>Default look: metal orb + neon eye, no colliders.</summary>
        public static GameObject DefaultVisual()
        {
            var root = new GameObject("ForgeHelperDrone");
            var body = MaterialPresets.Metal("ForgeDroneBody", PresetColors.Solid(new Color(0.75f, 0.78f, 0.82f)));
            var eye = MaterialPresets.Neon("ForgeDroneEye", PresetColors.Solid(new Color(0.2f, 0.9f, 1f)));
            Gfx.Sphere("Body", root.transform, body, Vector3.zero, 0.6f);
            Gfx.Sphere("Eye", root.transform, eye, new Vector3(0f, 0.05f, 0.24f), 0.22f);
            Gfx.Sphere("Rotor", root.transform, eye, new Vector3(0f, 0.36f, 0f), 0.12f);
            return root;
        }

        /// <summary>True when no drone has claimed <paramref name="cargo"/> and the player isn't holding it (use in programs).</summary>
        public static bool IsFree(GameObject cargo)
        {
            if (cargo == null || _claimed.Contains(cargo.Pointer)) return false;
            var vac = cargo.GetComponent<Vacuumable>();
            try { return vac == null || !(vac.IsHeld() || vac.IsCaptive()); } catch { return true; }
        }

        /// <summary>Removes the drone (drops its cargo).</summary>
        public void Dismiss()
        {
            var go = Go;
            if (go == null || !ActorRuntime.Remove(go, this)) Stop();
            if (go != null) Object.Destroy(go);
        }

        public void Start(GameObject go)
        {
            Go = go;
            _all.Add(this);
            Current = State.Idle;
            _lastPos = go.transform.position;
        }

        public void Stop()
        {
            DropCargo();
            _all.Remove(this);
        }

        public void Tick(float dt)
        {
            if (Go == null || Program == null) return;
            if (Cargo == null && (Current == State.Fetch || Current == State.Carry)) { DropCargo(); Current = State.Return; }
            switch (Current)
            {
                case State.Idle:
                    Hover(dt);
                    if ((_scan += dt) < ScanInterval) break;
                    _scan = 0f;
                    if (Battery < 0.15f) { Current = State.Charging; break; }
                    var c = Safe.Run("drone find", () => Program.FindCargo(this));
                    if (c != null && IsFree(c)) { Cargo = c; _claimed.Add(c.Pointer); Current = State.Fetch; _stuck = 0f; }
                    break;

                case State.Fetch:
                    if (!IsHeldByPlayer(Cargo) && FlyTo(Cargo.transform.position + Vector3.up * 1.1f, dt, 0.8f)) { Grab(); Current = State.Carry; }
                    else if (IsHeldByPlayer(Cargo) || Stuck(dt)) { DropCargo(); Current = State.Return; }
                    break;

                case State.Carry:
                    if (IsHeldByPlayer(Cargo)) { DropCargo(); Current = State.Return; break; }
                    HoldCargo();
                    var dest = Safe.Run("drone destination", () => Program.Destination(this, Cargo), HomePoint);
                    if (FlyTo(dest + Vector3.up * 1.4f, dt, 1f)) Deliver();
                    else if (Stuck(dt)) { DropCargo(); Current = State.Return; }
                    break;

                case State.Return:
                    if (FlyTo(HomePoint, dt, 0.5f)) Current = Battery < 0.5f ? State.Charging : State.Idle;
                    break;

                case State.Charging:
                    Hover(dt);
                    Battery = Mathf.Min(1f, Battery + ChargePerSecond * dt);
                    if (Battery >= 1f) Current = State.Idle;
                    break;
            }
        }

        public override string ToString() =>
            $"drone {Program?.Id} {Current} battery {Battery:P0} delivered {Delivered}{(Cargo != null ? " carrying " + Cargo.name : "")} at {Go?.transform.position}";

        // ------------------------------------------------------------------ internals

        static bool IsHeldByPlayer(GameObject go)
        {
            var vac = go != null ? go.GetComponent<Vacuumable>() : null;
            try { return vac != null && vac.IsHeld(); } catch { return false; }
        }

        void Hover(float dt)
        {
            var t = Go.transform;
            var p = Vector3.Lerp(t.position, HomePoint + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.15f), dt * 3f);
            t.position = p;
            t.Rotate(0f, 40f * dt, 0f);
        }

        /// <summary>Moves toward <paramref name="target"/>, keeps clear of the ground; true when within <paramref name="arrive"/>.</summary>
        bool FlyTo(Vector3 target, float dt, float arrive)
        {
            var t = Go.transform;
            var p = t.position;
            var to = target - p;
            float dist = to.magnitude;
            if (dist <= arrive) return true;
            float step = Speed * dt * (Battery > 0f ? 1f : 0.4f);
            p += to / dist * Mathf.Min(step, dist);
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var hit, 6f, World.GroundMask, QueryTriggerInteraction.Ignore) && p.y < hit.point.y + 0.9f)
                p.y = hit.point.y + 0.9f;
            t.position = p;
            var flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.01f) t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(flat), dt * 5f);
            Battery = Mathf.Max(0f, Battery - DrainPerSecond * dt);
            return (target - p).magnitude <= arrive;
        }

        bool Stuck(float dt)
        {
            var p = Go.transform.position;
            if ((p - _lastPos).sqrMagnitude < 0.0004f) _stuck += dt; else _stuck = 0f;
            _lastPos = p;
            return _stuck > 4f || (p - HomePoint).magnitude > Range * 2.5f;
        }

        void Grab()
        {
            var rb = Cargo.GetComponent<Rigidbody>();
            if (rb != null) { _cargoKinematic = rb.isKinematic; rb.isKinematic = true; }
            HoldCargo();
        }

        void HoldCargo()
        {
            if (Cargo == null) return;
            var at = Go.transform.position + Vector3.down * 0.9f;
            var rb = Cargo.GetComponent<Rigidbody>();
            if (rb != null) rb.position = at;
            Cargo.transform.position = at;
        }

        void Deliver()
        {
            var cargo = Cargo;
            DropCargo();
            bool consumed = cargo != null && Safe.Run("drone deliver", () => Program.Deliver(this, cargo), false);
            Delivered++;
            OnDelivered?.Invoke(this, cargo, consumed);
            Current = State.Return;
        }

        void DropCargo()
        {
            if (Cargo == null) return;
            _claimed.Remove(Cargo.Pointer);
            var rb = Cargo.GetComponent<Rigidbody>();
            if (rb != null && rb.isKinematic && !_cargoKinematic) { rb.isKinematic = false; rb.linearVelocity = Vector3.down; }
            Cargo = null;
            _cargoKinematic = false;
        }
    }

    /// <summary>Built-in <see cref="IDroneProgram"/>s and a delegate-based one (<see cref="Custom"/>).</summary>
    public static class DronePrograms
    {
        sealed class Program : IDroneProgram
        {
            public string Id { get; set; }
            public Func<HelperDrone, GameObject> Find;
            public Func<HelperDrone, GameObject, Vector3> Dest;
            public Func<HelperDrone, GameObject, bool> Give;
            public GameObject FindCargo(HelperDrone d) => Find(d);
            public Vector3 Destination(HelperDrone d, GameObject c) => Dest(d, c);
            public bool Deliver(HelperDrone d, GameObject c) => Give != null && Give(d, c);
            public override string ToString() => Id;
        }

        /// <summary>Program from delegates.</summary>
        public static IDroneProgram Custom(string id, Func<HelperDrone, GameObject> find, Func<HelperDrone, GameObject, Vector3> destination, Func<HelperDrone, GameObject, bool> deliver = null) =>
            new Program { Id = id, Find = find, Dest = destination, Give = deliver };

        /// <summary>Nearest free actor within <paramref name="radius"/> of the drone's home that passes <paramref name="filter"/> and isn't within <paramref name="exclude"/> m of <paramref name="excludePoint"/>.</summary>
        public static GameObject Nearest(HelperDrone d, float radius, Func<IdentifiableType, bool> filter, Vector3? excludePoint = null, float exclude = 2.5f)
        {
            var home = d.HomePoint;
            GameObject best = null; float bd = float.MaxValue;
            foreach (var a in World.ActorsNear(home, radius, filter))
            {
                var go = a.gameObject;
                if (!HelperDrone.IsFree(go)) continue;
                var p = go.transform.position;
                if (excludePoint.HasValue && (p - excludePoint.Value).sqrMagnitude < exclude * exclude) continue;
                float dd = (p - home).sqrMagnitude;
                if (dd < bd) { bd = dd; best = go; }
            }
            return best;
        }

        /// <summary>Collects matching actors and drops them at <paramref name="dropAt"/> (default: the drone's home).</summary>
        public static IDroneProgram Gather(Func<IdentifiableType, bool> filter, float radius = 20f, Func<HelperDrone, Vector3> dropAt = null, string id = "gather") =>
            new Program
            {
                Id = id,
                Find = d => Nearest(d, radius, filter, dropAt != null ? dropAt(d) : d.HomePoint - Vector3.up * d.HoverHeight),
                Dest = (d, c) => dropAt != null ? dropAt(d) : d.HomePoint - Vector3.up * d.HoverHeight,
            };

        /// <summary>Picks up plorts around its home and sells them at the market price (SR1 market-link style).</summary>
        public static IDroneProgram SellPlorts(float radius = 20f) =>
            new Program
            {
                Id = "sell",
                Find = d => Nearest(d, radius, DietTools.IsPlort),
                Dest = (d, c) => d.HomePoint - Vector3.up * (d.HoverHeight * 0.5f),
                Give = (d, c) =>
                {
                    var id = World.IdentOf(c);
                    if (id == null || !PlortMarket.Sell(id, 1, false)) return false;
                    Fx.Play("FX Despawn", c.transform.position, 0.5f);
                    World.Despawn(c, "ForgeDrone");
                    return true;
                }
            };

        /// <summary>Brings food that <paramref name="food"/> accepts to <paramref name="point"/> (e.g. a corral centre).</summary>
        public static IDroneProgram FeedCorral(Func<IdentifiableType, bool> food, Func<HelperDrone, Vector3> point, float radius = 20f) =>
            Gather(food, radius, point, "feed");
    }
}
