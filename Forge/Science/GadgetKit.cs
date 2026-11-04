using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Gadgets API: clones stock gadget definitions into new ones (prefab kept in an inactive holder, so the template
    /// never appears in the world) and binds managed logic (<see cref="IGadgetLogic"/>) to every placed gadget of a
    /// definition. Built-in logics: <see cref="ExtractorLogic"/>, <see cref="PlortCollectorLogic"/>,
    /// <see cref="TeleporterLogic"/>, <see cref="DroneStationLogic"/>.
    /// <code>var def = GadgetKit.Clone("TeleporterPink", "MyBeacon");
    /// GadgetKit.Logic(def, go => new TeleporterLogic("beacon"));</code>
    /// Adding the new definition to the fabricator/blueprints is not automated (use the game's registries or Cotton).
    /// </summary>
    public static class GadgetKit
    {
        static readonly Dictionary<string, List<Func<GameObject, IGadgetLogic>>> _logic = new(StringComparer.Ordinal);
        static Transform _holder;

        /// <summary>Gadget logics started so far (diagnostics).</summary>
        public static int Started;

        /// <summary>Inactive, never-unloaded parent of cloned prefabs.</summary>
        public static Transform Holder
        {
            get
            {
                if (_holder != null) return _holder;
                var go = new GameObject("ForgeGadgetPrefabs");
                go.SetActive(false);
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _holder = go.transform;
                return _holder;
            }
        }

        /// <summary>Stock or modded gadget definition by name, or null.</summary>
        public static GadgetDefinition Find(string name) => string.IsNullOrEmpty(name) ? null : Library.Get<GadgetDefinition>(name);

        /// <summary>
        /// New gadget definition <paramref name="newName"/> copied from <paramref name="template"/> (a stock definition name
        /// such as "TeleporterPink"), with its own prefab (edit it in <paramref name="editPrefab"/>).
        /// </summary>
        public static GadgetDefinition Clone(string template, string newName, Action<GameObject> editPrefab = null)
        {
            var src = Find(template);
            if (src == null || src.prefab == null) { ForgeLog.Warn($"[gadgets] no gadget definition '{template}' for {newName}"); return null; }
            var def = Object.Instantiate(src);
            def.name = newName;
            def.hideFlags = HideFlags.DontUnloadUnusedAsset;
            def.referenceId = "GadgetDefinition." + newName;
            def.SetupForSaving(def.referenceId);
            var prefab = Object.Instantiate(src.prefab, Holder);
            prefab.name = newName;
            var g = prefab.GetComponent<Gadget>();
            if (g != null) g.identType = def;
            def.prefab = prefab;
            if (editPrefab != null) Safe.Run("gadget prefab " + newName, () => editPrefab(prefab));
            return def;
        }

        /// <summary>Runs a new logic from <paramref name="factory"/> on every placed gadget of <paramref name="def"/>.</summary>
        public static void Logic(GadgetDefinition def, Func<GameObject, IGadgetLogic> factory) { if (def != null) Logic(def.name, factory); }

        /// <summary>Same by definition name (works for stock gadgets too).</summary>
        public static void Logic(string defName, Func<GameObject, IGadgetLogic> factory)
        {
            if (string.IsNullOrEmpty(defName) || factory == null) return;
            if (!_logic.TryGetValue(defName, out var l)) _logic[defName] = l = new List<Func<GameObject, IGadgetLogic>>();
            l.Add(factory);
            // gadgets already standing in the loaded world
            if (World.InGame)
                foreach (var gd in Object.FindObjectsOfType<Gadget>())
                    if (gd != null && gd.identType != null && gd.identType.name == defName) Attach(gd.gameObject, factory);
        }

        public static bool HasLogic(string defName) => defName != null && _logic.ContainsKey(defName);

        /// <summary>Places a gadget in the current scene (Cotton's <c>SpawnGadget</c>: model + actor registry).</summary>
        public static GameObject Spawn(GadgetDefinition def, Vector3 pos, Quaternion? rot = null) =>
            def == null ? null : Safe.Run("spawn gadget " + def.name, () => def.SpawnGadget(pos, rot ?? Quaternion.identity));

        /// <summary>Placed (loaded) gadgets of a definition.</summary>
        public static List<GameObject> Placed(string defName)
        {
            var r = new List<GameObject>();
            foreach (var gd in Object.FindObjectsOfType<Gadget>())
                if (gd != null && gd.identType != null && gd.identType.name == defName) r.Add(gd.gameObject);
            return r;
        }

        /// <summary>Save key of one placed gadget (definition + position rounded to 0.1 m), for <see cref="ForgeSave"/>.</summary>
        public static string Key(GameObject gadget, string field)
        {
            var id = World.IdentOf(gadget);
            var p = gadget != null ? gadget.transform.position : Vector3.zero;
            string R(float v) => Mathf.RoundToInt(v * 10f).ToString(CultureInfo.InvariantCulture);
            return $"forge.gadget.{id?.name ?? "?"}.{R(p.x)}_{R(p.y)}_{R(p.z)}.{field}";
        }

        static void Attach(GameObject go, Func<GameObject, IGadgetLogic> f)
        {
            IGadgetLogic logic = null;
            try { logic = f(go); } catch (Exception e) { ForgeLog.WarnOnce("gadget:" + go.name, $"[gadgets] logic for {go.name}: {e}"); }
            if (logic == null) return;
            ActorRuntime.Add(go, logic);
            Started++;
        }

        internal static void OnStart(Gadget g)
        {
            if (_logic.Count == 0 || g == null || g.identType == null) return;
            // the inactive template under Holder never starts; only real placed gadgets get here
            if (!_logic.TryGetValue(g.identType.name, out var l)) return;
            foreach (var f in l) Attach(g.gameObject, f);
        }
    }

    [HarmonyPatch(typeof(Gadget), nameof(Gadget.Start))]
    static class GadgetKitStartPatch
    {
        static void Postfix(Gadget __instance) => Safe.Run("GadgetKit start", () => GadgetKit.OnStart(__instance));
    }

    /// <summary>Base of gadget logics: the gadget object, its position, a per-gadget save key and a timer helper.</summary>
    public abstract class GadgetLogic : IGadgetLogic
    {
        protected GameObject Go { get; private set; }
        protected Vector3 Pos => Go != null ? Go.transform.position : Vector3.zero;
        protected string Key(string field) => GadgetKit.Key(Go, field);
        float _acc;

        public void Start(GameObject go) { Go = go; OnStart(); }
        public void Stop() => OnStop();
        public abstract void Tick(float dt);
        protected virtual void OnStart() { }
        protected virtual void OnStop() { }

        /// <summary>True once every <paramref name="interval"/> seconds (accumulates <paramref name="dt"/>).</summary>
        protected bool Every(float dt, float interval)
        {
            _acc += dt;
            if (_acc < interval) return false;
            _acc = 0f;
            return true;
        }

        /// <summary>In-game hours since day 0 (day * 24 + hour).</summary>
        protected static double GameHours()
        {
            try { var t = SceneContext.Instance.TimeDirector; return t.CurrDay() * 24.0 + t.CurrHour(); } catch { return 0; }
        }
    }

    /// <summary>
    /// Extractor: produces items from a weighted table every <see cref="CycleHours"/> in-game hours for
    /// <see cref="Cycles"/> cycles (-1 = forever). Progress is saved per gadget, so time spent away (or with the game
    /// closed while the save advanced) is caught up when the gadget loads again (up to <see cref="MaxCatchUp"/> items).
    /// </summary>
    public class ExtractorLogic : GadgetLogic
    {
        public readonly List<(IdentifiableType item, float weight)> Table = new();
        public float CycleHours = 3f;
        public int PerCycle = 1;
        public int Cycles = -1;
        public int MaxCatchUp = 6;
        public Vector3 OutputOffset = new(0f, 1.4f, 0f);
        public string OutputFx = "FX Slime Spawn";

        double _last; int _done;

        public ExtractorLogic Add(IdentifiableType item, float weight = 1f) { if (item != null) Table.Add((item, weight)); return this; }

        protected override void OnStart()
        {
            _done = ForgeSave.GetInt(Key("cycles"));
            double now = GameHours();
            _last = double.TryParse(ForgeSave.Get(Key("last")), NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : now;
            if (_last > now) _last = now;
            int owed = (int)((now - _last) / Math.Max(0.01, CycleHours));
            if (owed > 0) { Produce(Math.Min(owed * PerCycle, MaxCatchUp)); _done += owed; _last += owed * CycleHours; Store(); }
        }

        public override void Tick(float dt)
        {
            if (!Every(dt, 1f) || Finished) return;
            double now = GameHours();
            if (now - _last < CycleHours) return;
            _last = now;
            _done++;
            Produce(PerCycle);
            Store();
        }

        /// <summary>No more cycles left.</summary>
        public bool Finished => Cycles >= 0 && _done >= Cycles;

        void Store() { ForgeSave.SetInt(Key("cycles"), _done); ForgeSave.Set(Key("last"), _last.ToString("R", CultureInfo.InvariantCulture)); }

        IdentifiableType Pick()
        {
            float sum = 0f;
            foreach (var t in Table) sum += Mathf.Max(0f, t.weight);
            float r = World.Rand(0f, sum);
            foreach (var t in Table) { r -= Mathf.Max(0f, t.weight); if (r <= 0f) return t.item; }
            return Table.Count > 0 ? Table[Table.Count - 1].item : null;
        }

        void Produce(int n)
        {
            var at = Go.transform.TransformPoint(OutputOffset);
            for (int i = 0; i < n; i++)
            {
                var it = Pick();
                var a = it != null ? World.Spawn(it, at + UnityEngine.Random.insideUnitSphere * 0.3f) : null;
                var rb = a != null ? a.GetComponent<Rigidbody>() : null;
                if (rb != null) rb.linearVelocity = Vector3.up * 4f + UnityEngine.Random.insideUnitSphere * 1.5f;
            }
            if (n > 0 && !string.IsNullOrEmpty(OutputFx)) Fx.Play(OutputFx, at, 0.6f);
        }
    }

    /// <summary>Plort collector: every <see cref="Interval"/> s pulls plorts within <see cref="Radius"/> and sells them (or just gathers them at the gadget).</summary>
    public class PlortCollectorLogic : GadgetLogic
    {
        public float Radius = 12f, Interval = 2f;
        public bool AutoSell = true;
        public int Sold { get; private set; }

        public override void Tick(float dt)
        {
            if (!Every(dt, Interval)) return;
            foreach (var a in World.ActorsNear(Pos, Radius, DietTools.IsPlort))
            {
                var go = a.gameObject;
                var vac = go.GetComponent<Vacuumable>();
                try { if (vac != null && (vac.IsHeld() || vac.IsCaptive())) continue; } catch { }
                if (AutoSell)
                {
                    if (PlortMarket.Sell(a.identType, 1, false)) { Sold++; Fx.Play("FX Despawn", go.transform.position, 0.5f); World.Despawn(go, "ForgeCollector"); }
                }
                else
                {
                    var rb = go.GetComponent<Rigidbody>();
                    var to = Pos + Vector3.up - go.transform.position;
                    if (rb != null && to.magnitude > 1.5f) rb.linearVelocity = to.normalized * Mathf.Min(8f, to.magnitude * 2f);
                }
            }
        }
    }

    /// <summary>
    /// Teleporter pairs by channel: stepping onto one moves the player to another teleporter of the same channel (the
    /// next one in placement order). Same-region only (uses <see cref="World.TeleportPlayer"/>).
    /// </summary>
    public class TeleporterLogic : GadgetLogic
    {
        static readonly Dictionary<string, List<TeleporterLogic>> _channels = new(StringComparer.OrdinalIgnoreCase);
        static float _globalCooldown;

        public readonly string Channel;
        public float TriggerRadius = 1.3f;
        public Vector3 ExitOffset = new(0f, 0.5f, 1.6f);
        bool _armed = true;

        public TeleporterLogic(string channel) { Channel = channel ?? "default"; }

        public static IReadOnlyList<TeleporterLogic> On(string channel) =>
            _channels.TryGetValue(channel ?? "default", out var l) ? l : (IReadOnlyList<TeleporterLogic>)Array.Empty<TeleporterLogic>();

        protected override void OnStart()
        {
            if (!_channels.TryGetValue(Channel, out var l)) _channels[Channel] = l = new List<TeleporterLogic>();
            l.Add(this);
        }

        protected override void OnStop() { if (_channels.TryGetValue(Channel, out var l)) l.Remove(this); }

        public override void Tick(float dt)
        {
            if (Go == null || !World.InGame) return;
            float d = Vector3.Distance(World.PlayerPos, Pos);
            if (d > TriggerRadius * 1.6f) { _armed = true; return; }
            if (!_armed || d > TriggerRadius || Time.time < _globalCooldown) return;
            var l = _channels[Channel];
            if (l.Count < 2) return;
            var to = l[(l.IndexOf(this) + 1) % l.Count];
            if (to == this || to.Go == null) return;
            _armed = false;
            to._armed = false;
            _globalCooldown = Time.time + 1.5f;
            Fx.Play("FX Despawn", World.PlayerPos, 1f);
            World.TeleportPlayer(to.Go.transform.TransformPoint(to.ExitOffset));
        }
    }

    /// <summary>Drone station: keeps one <see cref="HelperDrone"/> with a program flying from this gadget.</summary>
    public class DroneStationLogic : GadgetLogic
    {
        readonly Func<IDroneProgram> _program;
        public HelperDrone Drone { get; private set; }

        public DroneStationLogic(Func<IDroneProgram> program) { _program = program; }

        protected override void OnStart() => Drone = HelperDrone.Spawn(_program?.Invoke() ?? DronePrograms.SellPlorts(), Go.transform, null);
        protected override void OnStop() { Drone?.Dismiss(); Drone = null; }
        public override void Tick(float dt) { }
    }
}
