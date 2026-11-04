using System;
using System.Collections.Generic;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Hosts managed <see cref="IActorLogic"/> objects on GameObjects: archetype runtimes, gadget logic, helper drones.
    /// No MonoBehaviour injection, one Update for everything, objects far from the player tick less often
    /// (<see cref="FarDistance"/>, <see cref="FarInterval"/>), logic of destroyed objects is stopped and dropped.
    /// A logic that throws 10 times is removed.
    /// </summary>
    public static class ActorRuntime
    {
        sealed class Slot { public IActorLogic L; public int Errors; }
        sealed class Host { public GameObject Go; public IntPtr Key; public readonly List<Slot> Slots = new(); public readonly List<Slot> Pending = new(); public float Acc; }

        static readonly Dictionary<IntPtr, Host> _hosts = new();
        static readonly List<Host> _list = new();

        /// <summary>Beyond this distance from the player logic ticks every <see cref="FarInterval"/> seconds (with the summed dt).</summary>
        public static float FarDistance = 70f;
        /// <summary>Tick interval of far objects.</summary>
        public static float FarInterval = 0.5f;
        /// <summary>Objects with logic.</summary>
        public static int Objects => _list.Count;
        /// <summary>All logic instances.</summary>
        public static int Count { get { int n = 0; foreach (var h in _list) n += h.Slots.Count + h.Pending.Count; return n; } }

        /// <summary>Attaches <paramref name="logic"/>; it starts on the next frame.</summary>
        public static T Add<T>(GameObject go, T logic) where T : class, IActorLogic
        {
            if (go == null || logic == null) return logic;
            if (_hosts.TryGetValue(go.Pointer, out var h) && h.Go == null) { foreach (var s in h.Slots) StopOne(s.L); _list.Remove(h); h = null; }   // address reused
            if (h == null) { h = new Host { Go = go, Key = go.Pointer }; _hosts[go.Pointer] = h; _list.Add(h); }
            h.Pending.Add(new Slot { L = logic });
            return logic;
        }

        /// <summary>First logic of type <typeparamref name="T"/> on the object (or null).</summary>
        public static T Get<T>(GameObject go) where T : class
        {
            if (go == null || !_hosts.TryGetValue(go.Pointer, out var h)) return null;
            foreach (var s in h.Slots) if (s.L is T t) return t;
            foreach (var s in h.Pending) if (s.L is T t) return t;
            return null;
        }

        /// <summary>All logic on the object.</summary>
        /// <summary>Live objects that currently have logics (a copy; safe to modify the runtime while iterating).</summary>
        public static List<GameObject> Hosts()
        {
            var res = new List<GameObject>(_list.Count);
            foreach (var h in _list) if (h.Go != null) res.Add(h.Go);
            return res;
        }

        public static IEnumerable<IActorLogic> On(GameObject go)
        {
            if (go == null || !_hosts.TryGetValue(go.Pointer, out var h)) yield break;
            foreach (var s in h.Slots) yield return s.L;
            foreach (var s in h.Pending) yield return s.L;
        }

        /// <summary>Stops and removes one logic.</summary>
        public static bool Remove(GameObject go, IActorLogic logic)
        {
            if (go == null || !_hosts.TryGetValue(go.Pointer, out var h)) return false;
            for (int i = 0; i < h.Slots.Count; i++)
                if (h.Slots[i].L == logic) { StopOne(h.Slots[i].L); h.Slots.RemoveAt(i); return true; }
            return h.Pending.RemoveAll(s => s.L == logic) > 0;
        }

        /// <summary>Stops and removes all logic of the object.</summary>
        public static void Clear(GameObject go)
        {
            if (go == null || !_hosts.TryGetValue(go.Pointer, out var h)) return;
            Drop(h);
        }

        /// <summary>Delivers a signal to the archetype runtimes on the object.</summary>
        public static void Signal(GameObject go, in SlimeSignal s)
        {
            if (go == null || _hosts.Count == 0 || !_hosts.TryGetValue(go.Pointer, out var h)) return;
            foreach (var slot in h.Slots)
                if (slot.L is IArchetypeRuntime r)
                    try { r.OnSignal(s); } catch (Exception e) { ForgeLog.WarnOnce("signal:" + r.Archetype?.Id, $"{r.Archetype?.Id}.OnSignal failed: {e}"); }
        }

        static void Drop(Host h)
        {
            foreach (var s in h.Slots) StopOne(s.L);
            h.Slots.Clear(); h.Pending.Clear();
            _hosts.Remove(h.Key);
            _list.Remove(h);
        }

        static void StopOne(IActorLogic l)
        {
            try { l.Stop(); } catch (Exception e) { ForgeLog.WarnOnce("stop:" + l.GetType().Name, $"{l.GetType().Name}.Stop failed: {e.Message}"); }
        }

        static void Tick(float dt)
        {
            Vector3 player = World.InGame ? World.PlayerPos : Vector3.zero;
            bool havePlayer = World.InGame;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var h = _list[i];
                if (h.Go == null)
                {
                    foreach (var s in h.Slots) StopOne(s.L);
                    _hosts.Remove(h.Key); _list.RemoveAt(i);
                    continue;
                }
                if (!h.Go.activeInHierarchy) continue;
                if (h.Pending.Count > 0)
                {
                    var start = h.Pending.ToArray();
                    h.Pending.Clear();
                    foreach (var s in start)
                    {
                        try { s.L.Start(h.Go); h.Slots.Add(s); }
                        catch (Exception e) { ForgeLog.WarnOnce("start:" + s.L.GetType().Name, $"{s.L.GetType().Name}.Start failed: {e}"); }
                    }
                }
                float step = dt;
                if (havePlayer && (h.Go.transform.position - player).sqrMagnitude > FarDistance * FarDistance)
                {
                    h.Acc += dt;
                    if (h.Acc < FarInterval) continue;
                    step = h.Acc;
                }
                h.Acc = 0f;
                for (int k = h.Slots.Count - 1; k >= 0 && k < h.Slots.Count; k--)
                {
                    var s = h.Slots[k];
                    try { s.L.Tick(step); }
                    catch (Exception e)
                    {
                        ForgeLog.WarnOnce("tick:" + s.L.GetType().Name, $"{s.L.GetType().Name}.Tick failed: {e}");
                        if (++s.Errors >= 10) { StopOne(s.L); h.Slots.RemoveAt(k); ForgeLog.Warn($"{s.L.GetType().Name} removed from {h.Go.name} after 10 errors"); }
                    }
                }
            }
        }

        internal sealed class Module : IForgeModule
        {
            public string Id => "runtime";
            public void Init() { }
            public void Tick(float dt) { if (_list.Count > 0) ActorRuntime.Tick(dt); }
        }
    }
}
