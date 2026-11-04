using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Player.PlayerItems;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CottonLibrary.Forge
{
    /// <summary>The player's vacpack: ammo slots, adding/taking items and a shot event (every expelled item).</summary>
    public static class VacPack
    {
        public static AmmoSlotManager Ammo { get { try { return SceneContext.Instance?.PlayerState?.Ammo; } catch { return null; } } }
        public static VacuumItem Vacuum { get { try { return SceneContext.Instance?.PlayerState?.VacuumItem; } catch { return null; } } }

        /// <summary>Raised after every shot (with or without a fire mode).</summary>
        public static event Action<ShotContext> Shot;

        public static int SlotCount { get { var a = Ammo; return a?.Slots != null ? a.Slots.Length : 0; } }
        public static int SelectedIndex { get { var a = Ammo; return a != null ? a._selectedAmmoIdx : -1; } }
        public static IdentifiableType Selected { get { var a = Ammo; try { return a?.GetSelectedId(); } catch { return null; } } }
        public static int SelectedCount { get { var a = Ammo; try { return a != null ? a.GetSelectedSlotCount() : 0; } catch { return 0; } } }

        /// <summary>Contents of slot <paramref name="i"/> (null id = empty).</summary>
        public static (IdentifiableType id, int count, int max) Slot(int i)
        {
            var a = Ammo;
            if (a == null || i < 0 || i >= SlotCount) return (null, 0, 0);
            try { return (a.GetSlotName(i), a.GetSlotCount(i), a.GetSlotMaxCount(i)); } catch { return (null, 0, 0); }
        }

        /// <summary>Total of <paramref name="id"/> over all slots.</summary>
        public static int CountOf(IdentifiableType id)
        {
            int n = 0;
            for (int i = 0; i < SlotCount; i++) { var s = Slot(i); if (s.id != null && id != null && s.id.name == id.name) n += s.count; }
            return n;
        }

        /// <summary>Adds up to <paramref name="count"/> items; returns how many fit.</summary>
        public static int Add(IdentifiableType id, int count = 1)
        {
            var a = Ammo;
            if (a == null || id == null) return 0;
            int added = 0;
            try { for (; added < count; added++) if (!a.MaybeAddToAnySlot(new AmmoSlot.AmmoMetadata(id), false)) break; }
            catch (Exception e) { ForgeLog.WarnOnce("vac.add", $"VacPack.Add: {e.Message}"); }
            return added;
        }

        /// <summary>Removes <paramref name="count"/> items when that many are carried.</summary>
        public static bool Take(IdentifiableType id, int count = 1)
        {
            var a = Ammo;
            if (a == null || id == null || CountOf(id) < count) return false;
            try { a.Decrement(id, count); return true; } catch { return false; }
        }

        /// <summary>Removes <paramref name="count"/> items from slot <paramref name="index"/>.</summary>
        public static bool TakeAt(int index, int count = 1)
        {
            var a = Ammo;
            if (a == null || Slot(index).count < count) return false;
            try { a.Decrement(index, count); return true; } catch { return false; }
        }

        internal static void RaiseShot(ShotContext s) { if (Shot != null) try { Shot(s); } catch (Exception e) { ForgeLog.WarnOnce("vac.shot", $"VacPack.Shot handler: {e}"); } }
    }

    /// <summary>
    /// Alternate fire modes: mods register <see cref="IFireMode"/>s, the player cycles them with the fire-mode key
    /// (<c>FireModeKey</c> setting, default B); the active mode reacts to every shot it accepts (more projectiles, other
    /// velocity, effects). "Normal" = no mode. Built-ins (opt-in): <see cref="Burst"/>, <see cref="Spread"/>,
    /// <see cref="Power"/>, <see cref="Lob"/>, <see cref="Pulse"/>; <see cref="RegisterBuiltins"/> adds all of them.
    /// </summary>
    public static class FireModes
    {
        sealed class Mode : IFireMode
        {
            public string Id { get; set; }
            public string Label { get; set; }
            public Func<IdentifiableType, bool> Filter;
            public Action<ShotContext> Run;
            public bool Accepts(IdentifiableType ammo) => Filter == null || Filter(ammo);
            public void OnShot(ShotContext s) => Run(s);
        }

        static readonly List<IFireMode> _modes = new();
        static int _index = -1;
        static Key? _key;
        static string _keyName;
        internal static bool Reentrant;

        public static IReadOnlyList<IFireMode> All => _modes;
        /// <summary>Active mode (null = normal shooting).</summary>
        public static IFireMode Current => _index >= 0 && _index < _modes.Count ? _modes[_index] : null;
        /// <summary>Raised when the active mode changes (null = normal).</summary>
        public static event Action<IFireMode> Changed;

        public static void Register(IFireMode mode)
        {
            if (mode == null) return;
            _modes.RemoveAll(m => string.Equals(m.Id, mode.Id, StringComparison.OrdinalIgnoreCase));
            _modes.Add(mode);
        }

        /// <summary>Mode from delegates.</summary>
        public static IFireMode Create(string id, string label, Action<ShotContext> onShot, Func<IdentifiableType, bool> accepts = null) =>
            new Mode { Id = id, Label = label, Run = onShot, Filter = accepts };

        /// <summary>Selects a mode by id (null / "normal" = normal).</summary>
        public static bool Select(string id)
        {
            int i = string.IsNullOrEmpty(id) || id.Equals("normal", StringComparison.OrdinalIgnoreCase) ? -1
                : _modes.FindIndex(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
            if (i < 0 && !(string.IsNullOrEmpty(id) || id.Equals("normal", StringComparison.OrdinalIgnoreCase))) return false;
            Set(i);
            return true;
        }

        /// <summary>Normal → first mode → ... → normal.</summary>
        public static void Cycle() { if (_modes.Count > 0) Set(_index + 1 >= _modes.Count ? -1 : _index + 1); }

        static void Set(int i)
        {
            if (i == _index) return;
            _index = i;
            var c = Current;
            ForgeHud.Toast("Fire mode: " + (c != null ? c.Label : "Normal"), 1.5f);
            Changed?.Invoke(c);
        }

        /// <summary>Registers every built-in mode.</summary>
        public static void RegisterBuiltins() { Register(Burst()); Register(Spread()); Register(Power()); Register(Lob()); Register(Pulse()); }

        // ------------------------------------------------------------------ built-ins

        static Rigidbody Rb(GameObject go) => go != null ? go.GetComponent<Rigidbody>() : null;

        /// <summary>
        /// Fires <paramref name="extra"/> more items of the same ammo, one every <paramref name="interval"/> s, then rests for
        /// <paramref name="cooldown"/> s: shots fired while the trigger is held during a burst or its cooldown stay normal, so
        /// holding the trigger gives one burst per ~(extra × interval + cooldown) s instead of a triple stream.
        /// </summary>
        public static IFireMode Burst(int extra = 2, float interval = 0.14f, float cooldown = 0.6f)
        {
            extra = Mathf.Clamp(extra, 1, 6);
            float readyAt = 0f;
            return Create("burst", $"Burst x{extra + 1}", s =>
            {
                if (Time.unscaledTime < readyAt) return;
                readyAt = Time.unscaledTime + extra * interval + cooldown;
                for (int k = 1; k <= extra; k++)
                    Scheduler.Seconds(interval * k, () =>
                    {
                        if (Time.timeScale <= 0f) return;   // paused
                        var go = s.ShootAgain?.Invoke();
                        if (go != null) IgnorePlayer(go, 0.35f);
                    }, "burst");
            });
        }

        /// <summary>
        /// Fires a fan: <paramref name="extra"/> more items spread over ±<paramref name="angle"/> degrees. The pellets start
        /// side by side ahead of the muzzle and ignore the player and each other for a moment, so they don't jam in the
        /// player's collider when standing still.
        /// </summary>
        public static IFireMode Spread(int extra = 2, float angle = 12f) => Create("spread", "Spread", s =>
        {
            var pellets = new List<GameObject> { s.Projectile };
            var dir = s.Direction.sqrMagnitude > 1e-4f ? s.Direction.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, dir);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            for (int k = 0; k < extra; k++)
            {
                var go = s.ShootAgain?.Invoke();
                var rb = Rb(go);
                if (rb == null) break;
                float a = extra == 1 ? angle : Mathf.Lerp(-angle, angle, k / (float)(extra - 1));
                if (Mathf.Abs(a) < 0.5f) a = angle * 0.5f;
                rb.linearVelocity = Quaternion.AngleAxis(a, Vector3.up) * rb.linearVelocity;
                MoveClear(go, rb, s.Origin, s.Origin + dir * 0.45f + side * (Mathf.Sign(a) * 0.4f));
                pellets.Add(go);
            }
            foreach (var p in pellets) IgnorePlayer(p, 0.5f, pellets);
        });

        /// <summary>
        /// Faster shot: ×<paramref name="mult"/>, at most <paramref name="maxSpeed"/> m/s (never slower than the normal shot).
        /// Range grows with the square of the speed, so keep the multiplier small.
        /// </summary>
        public static IFireMode Power(float mult = 1.3f, float maxSpeed = 55f) => Create("power", "Power shot", s =>
        {
            var rb = Rb(s.Projectile);
            if (rb == null) return;
            var v = rb.linearVelocity;
            float speed = v.magnitude;
            if (speed < 0.01f) return;
            float target = Mathf.Max(speed, Mathf.Min(speed * mult, maxSpeed));
            rb.linearVelocity = v * (target / speed);
        });

        /// <summary>High arc throw (slower, upward).</summary>
        public static IFireMode Lob(float up = 7f, float keep = 0.55f) => Create("lob", "Lob", s =>
        {
            var rb = Rb(s.Projectile);
            if (rb != null) rb.linearVelocity = rb.linearVelocity * keep + Vector3.up * up;
        });

        /// <summary>
        /// Shot plus a knock-back cone in front of the player (<paramref name="radius"/>, <paramref name="force"/>), and
        /// <paramref name="waveDelay"/> s later the projectile releases a shock wave that throws actors around it away
        /// (<paramref name="waveRadius"/>, <paramref name="waveForce"/>).
        /// </summary>
        public static IFireMode Pulse(float radius = 4f, float force = 10f, float waveDelay = 0.4f, float waveRadius = 5f, float waveForce = 12f) => Create("pulse", "Pulse", s =>
        {
            var center = s.Origin + s.Direction * (radius * 0.6f);
            Push(center, radius, force, s.Origin, s.Projectile);
            Fx.Play("FX Despawn", center, 0.8f);
            var proj = s.Projectile;
            Scheduler.Seconds(waveDelay, () =>
            {
                if (proj == null || Time.timeScale <= 0f) return;
                var p = proj.transform.position;
                Push(p, waveRadius, waveForce, p, proj);
                Fx.Play("FX Despawn", p, 1.3f);
            }, "pulse wave");
        });

        /// <summary>Adds velocity away from <paramref name="from"/> to free actors within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public static int Push(Vector3 center, float radius, float force, Vector3 from, GameObject skip = null)
        {
            int n = 0;
            foreach (var a in World.ActorsNear(center, radius))
            {
                if (a == null || a.gameObject == skip) continue;
                var rb = Rb(a.gameObject);
                if (rb == null || rb.isKinematic) continue;
                var d = a.transform.position - from;
                if (d.sqrMagnitude < 0.01f) d = Vector3.up;
                d.y = Mathf.Max(d.y, 0.3f);
                rb.linearVelocity += d.normalized * force;
                n++;
            }
            return n;
        }

        /// <summary>Moves a projectile to <paramref name="to"/> unless a wall is in between (then it stays at <paramref name="from"/>).</summary>
        static void MoveClear(GameObject go, Rigidbody rb, Vector3 from, Vector3 to)
        {
            if (Physics.Linecast(from, to, World.GroundMask, QueryTriggerInteraction.Ignore)) return;
            go.transform.position = to;
            rb.position = to;
        }

        static Collider[] _playerCols = Array.Empty<Collider>();
        static IntPtr _playerColsOf;

        static Collider[] PlayerColliders()
        {
            var p = World.Player;
            if (p == null) return Array.Empty<Collider>();
            if (_playerColsOf != p.Pointer || Array.Exists(_playerCols, c => c == null))
            {
                var list = new List<Collider>();
                foreach (var c in p.GetComponentsInChildren<Collider>(true)) if (c != null && !c.isTrigger) list.Add(c);
                _playerCols = list.ToArray();
                _playerColsOf = p.Pointer;
            }
            return _playerCols;
        }

        /// <summary>
        /// For <paramref name="seconds"/> the projectile doesn't collide with the player (nor with <paramref name="siblings"/>,
        /// e.g. the other pellets of a spread shot).
        /// </summary>
        public static void IgnorePlayer(GameObject projectile, float seconds, IList<GameObject> siblings = null)
        {
            if (projectile == null) return;
            var mine = projectile.GetComponentsInChildren<Collider>();
            var pairs = new List<(Collider, Collider)>();
            void Pair(Collider a, Collider b)
            {
                if (a == null || b == null || a.isTrigger || b.isTrigger) return;
                try { Physics.IgnoreCollision(a, b, true); pairs.Add((a, b)); } catch { }
            }
            var player = PlayerColliders();
            foreach (var c in mine)
            {
                foreach (var pc in player) Pair(c, pc);
                if (siblings == null) continue;
                foreach (var sib in siblings)
                {
                    if (sib == null || sib == projectile || sib.Pointer.ToInt64() < projectile.Pointer.ToInt64()) continue;   // each pair once
                    foreach (var oc in sib.GetComponentsInChildren<Collider>()) Pair(c, oc);
                }
            }
            if (pairs.Count == 0) return;
            Scheduler.Seconds(seconds, () =>
            {
                foreach (var (a, b) in pairs)
                    if (a != null && b != null) try { Physics.IgnoreCollision(a, b, false); } catch { }
            }, "fire mode collisions");
        }

        // ------------------------------------------------------------------ hook

        internal static void OnExpel(VacuumItem vac, AmmoSlot.AmmoMetadata md, GameObject projectile)
        {
            if (Reentrant || projectile == null) return;
            var cam = Camera.main;
            var shot = new ShotContext
            {
                Projectile = projectile,
                Ammo = md?.Id,
                Origin = projectile.transform.position,
                Direction = cam != null ? cam.transform.forward : vac.transform.forward,
            };
            var ammo = shot.Ammo;
            shot.ShootAgain = () =>
            {
                var a = VacPack.Ammo;
                if (a == null || vac == null || ammo == null) return null;
                var sel = a.GetSelectedId();
                if (sel == null || sel.name != ammo.name || a.GetSelectedSlotCount() <= 0) return null;
                var m = a.PopFromSelectedSlot();
                if (m == null) return null;
                Reentrant = true;
                try { return vac.Expel(m, false, 0f, false); }
                finally { Reentrant = false; }
            };
            VacPack.RaiseShot(shot);
            var mode = Current;
            if (mode != null && ammo != null && mode.Accepts(ammo))
                try { mode.OnShot(shot); } catch (Exception e) { ForgeLog.WarnOnce("firemode:" + mode.Id, $"Fire mode {mode.Id}: {e}"); }
        }

        static bool KeyPressed()
        {
            string name = ForgeConfig.FireModeKey?.Value;
            if (name != _keyName)
            {
                _keyName = name;
                _key = !string.IsNullOrWhiteSpace(name) && Enum.TryParse<Key>(name.Trim(), true, out var k) ? k : null;
                if (!string.IsNullOrWhiteSpace(name) && _key == null) ForgeLog.Warn($"FireModeKey '{name}' is not a UnityEngine.InputSystem.Key name");
            }
            if (_key == null) return false;
            var kb = Keyboard.current;
            return kb != null && kb[_key.Value].wasPressedThisFrame;
        }

        internal sealed class Module : IForgeModule
        {
            public string Id => "vacpack";
            public void Init() { }
            public void Tick(float dt)
            {
                if (_modes.Count == 0 || !World.InGame || Time.timeScale <= 0f) return;
                if (KeyPressed()) Cycle();
            }
        }
    }

    [HarmonyPatch(typeof(VacuumItem), nameof(VacuumItem.Expel), new[] { typeof(AmmoSlot.AmmoMetadata), typeof(bool), typeof(float), typeof(bool) })]
    static class FireModesExpelPatch
    {
        static void Postfix(VacuumItem __instance, AmmoSlot.AmmoMetadata metadata, GameObject __result)
        {
            if (FireModes.Reentrant || !ForgeModules.IsRunning("vacpack")) return;
            Safe.Run("FireModes expel", () => FireModes.OnExpel(__instance, metadata, __result));
        }
    }

    /// <summary>
    /// Extra ammo slots kept by Forge (SR2's own slot UI is not extended): stash the selected vacpack slot into a virtual
    /// slot, bring it back, or shoot straight from it. Counts are saved per game and shown as HUD counters.
    /// <code>VirtualAmmo.Define("plorts", "Plort bag", 200, DietTools.IsPlort);
    /// VirtualAmmo.Stash("plorts");</code>
    /// </summary>
    public static class VirtualAmmo
    {
        public sealed class SlotDef
        {
            public string Id, Label;
            public int Capacity;
            public Func<IdentifiableType, bool> Accepts;
            public IdentifiableType Item;
            public int Count;
            public bool ShowOnHud = true;
            public override string ToString() => $"{Id} '{Label}': {(Item != null ? Item.name : "empty")} {Count}/{Capacity}";
        }

        static readonly Dictionary<string, SlotDef> _slots = new(StringComparer.OrdinalIgnoreCase);
        static bool _hooked;

        public static IEnumerable<SlotDef> All => _slots.Values;
        public static SlotDef Get(string id) => id != null && _slots.TryGetValue(id, out var s) ? s : null;

        /// <summary>Defines a virtual slot (one item type at a time, up to <paramref name="capacity"/>).</summary>
        public static SlotDef Define(string id, string label, int capacity, Func<IdentifiableType, bool> accepts = null, bool showOnHud = true)
        {
            Hook();
            var s = new SlotDef { Id = id, Label = label ?? id, Capacity = Math.Max(1, capacity), Accepts = accepts, ShowOnHud = showOnHud };
            _slots[id] = s;
            if (showOnHud)
                ForgeHud.Add(new HudCounter("ammo." + id, HudAnchor.MiddleRight, () => s.Count > 0 ? $"{s.Label}: {Idents.DisplayName(s.Item)} {s.Count}/{s.Capacity}" : null));
            return s;
        }

        /// <summary>Moves the selected vacpack slot into virtual slot <paramref name="id"/>; returns the moved amount.</summary>
        public static int Stash(string id)
        {
            var s = Get(id);
            var item = VacPack.Selected;
            int have = VacPack.SelectedCount;
            if (s == null || item == null || have <= 0) return 0;
            if (s.Accepts != null && !s.Accepts(item)) { ForgeHud.Toast($"{s.Label} can't hold {Idents.DisplayName(item)}", 2f); return 0; }
            if (s.Item != null && s.Count > 0 && s.Item.name != item.name) { ForgeHud.Toast($"{s.Label} already holds {Idents.DisplayName(s.Item)}", 2f); return 0; }
            int n = Math.Min(have, s.Capacity - s.Count);
            if (n <= 0 || !VacPack.TakeAt(VacPack.SelectedIndex, n)) return 0;
            s.Item = item; s.Count += n;
            return n;
        }

        /// <summary>Moves items back into the vacpack (as many as fit).</summary>
        public static int Unstash(string id, int max = int.MaxValue)
        {
            var s = Get(id);
            if (s == null || s.Item == null || s.Count <= 0) return 0;
            int n = VacPack.Add(s.Item, Math.Min(max, s.Count));
            s.Count -= n;
            if (s.Count <= 0) { s.Count = 0; s.Item = null; }
            return n;
        }

        /// <summary>Shoots one item from the virtual slot in front of the camera.</summary>
        public static GameObject Shoot(string id, float speed = 22f)
        {
            var s = Get(id);
            var cam = Camera.main;
            if (s == null || s.Item == null || s.Count <= 0 || cam == null) return null;
            var go = World.Spawn(s.Item, cam.transform.position + cam.transform.forward * 1.5f);
            if (go == null) return null;
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.linearVelocity = cam.transform.forward * speed;
            if (--s.Count <= 0) { s.Count = 0; s.Item = null; }
            return go;
        }

        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            ForgeSave.Loaded += () =>
            {
                foreach (var s in _slots.Values)
                {
                    s.Item = null; s.Count = 0;
                    var v = ForgeSave.Get("forge.ammo." + s.Id);
                    int c = v != null ? v.LastIndexOf(':') : -1;
                    if (c <= 0 || !int.TryParse(v.Substring(c + 1), out var n)) continue;
                    s.Item = Idents.Find(v.Substring(0, c));
                    s.Count = s.Item != null ? Math.Min(n, s.Capacity) : 0;
                }
            };
            ForgeSave.Saving += () =>
            {
                foreach (var s in _slots.Values)
                    ForgeSave.Set("forge.ammo." + s.Id, s.Item != null && s.Count > 0 ? s.Item.name + ":" + s.Count : null);
            };
        }
    }
}
