using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Lets single slimes pass through corral walls (ghost / phasing slimes): collisions between the actor's solid
    /// colliders and every wall collider of the corrals within 20 m (standard and upgraded walls of each
    /// <c>WallUpgrader</c>) are ignored, and restored on <see cref="Solid"/> — but only once the slime is no longer inside a
    /// wall, so it never gets stuck. Call <see cref="Update"/> regularly (e.g. every 0.3 s) while passing through.
    /// </summary>
    public static class CorralWalls
    {
        static readonly List<Collider> _walls = new();
        static readonly HashSet<IntPtr> _wallSet = new();
        static float _wallsAt = -999f;

        sealed class State { public GameObject Go; public Collider[] Cols; public readonly List<Collider> Ignored = new(); public readonly HashSet<IntPtr> Set = new(); }
        static readonly Dictionary<IntPtr, State> _states = new();

        /// <summary>Wall colliders of all loaded corrals (refreshed at most every 10 s).</summary>
        public static IReadOnlyList<Collider> Walls { get { Refresh(false); return _walls; } }

        /// <summary>Re-scans the corrals (after building or upgrading one).</summary>
        public static void Refresh(bool force)
        {
            if (!force && Time.time - _wallsAt < 10f) return;
            _wallsAt = Time.time;
            _walls.Clear(); _wallSet.Clear();
            var dead = new List<IntPtr>();
            foreach (var kv in _states) if (kv.Value.Go == null) dead.Add(kv.Key);
            foreach (var k in dead) _states.Remove(k);
            foreach (var w in Object.FindObjectsOfType<WallUpgrader>())
            {
                if (w == null) continue;
                foreach (var root in new[] { w.StandardWalls, w.UpgradeWalls })
                {
                    if (root == null) continue;
                    foreach (var c in root.GetComponentsInChildren<Collider>(true))
                        if (c != null && !c.isTrigger && _wallSet.Add(c.Pointer)) _walls.Add(c);
                }
            }
        }

        /// <summary>Slimes currently passing through walls.</summary>
        public static int Passing { get { int n = 0; foreach (var s in _states.Values) if (s.Go != null && s.Ignored.Count > 0) n++; return n; } }

        /// <summary>True while walls are ignored for this slime.</summary>
        public static bool IsPassing(GameObject slime) => slime != null && _states.TryGetValue(slime.Pointer, out var s) && s.Ignored.Count > 0;

        /// <summary>Starts (or continues: picks up walls that came in range) passing through walls.</summary>
        public static void PassThrough(GameObject slime)
        {
            if (slime == null) return;
            Refresh(false);
            if (!_states.TryGetValue(slime.Pointer, out var s))
            {
                var l = new List<Collider>();
                foreach (var c in slime.GetComponentsInChildren<Collider>()) if (c != null && !c.isTrigger) l.Add(c);
                s = new State { Go = slime, Cols = l.ToArray() };
                _states[slime.Pointer] = s;
            }
            var pos = slime.transform.position;
            foreach (var w in _walls)
            {
                if (w == null || s.Set.Contains(w.Pointer) || w.bounds.SqrDistance(pos) > 400f) continue;
                foreach (var c in s.Cols) if (c != null) Physics.IgnoreCollision(c, w, true);
                s.Ignored.Add(w); s.Set.Add(w.Pointer);
            }
        }

        /// <summary>True when the slime overlaps one of the walls it currently ignores.</summary>
        public static bool InsideWall(GameObject slime)
        {
            if (slime == null || !_states.TryGetValue(slime.Pointer, out var s) || s.Ignored.Count == 0) return false;
            float r = 0.45f * slime.transform.lossyScale.x;
            foreach (var c in Physics.OverlapSphere(slime.transform.position + Vector3.up * r, r, ~0, QueryTriggerInteraction.Ignore))
                if (c != null && s.Set.Contains(c.Pointer)) return true;
            return false;
        }

        /// <summary>Makes the walls solid again for this slime unless it is inside one (returns false then: call again later).</summary>
        public static bool Solid(GameObject slime, bool force = false)
        {
            if (slime == null || !_states.TryGetValue(slime.Pointer, out var s)) return true;
            if (!force && InsideWall(slime)) return false;
            foreach (var w in s.Ignored)
                if (w != null) foreach (var c in s.Cols) if (c != null) Physics.IgnoreCollision(c, w, false);
            _states.Remove(slime.Pointer);
            return true;
        }

        /// <summary>
        /// One call per tick: <paramref name="pass"/> true = pass through (and pick up new walls), false = become solid as
        /// soon as possible. Returns whether the slime is passing through now.
        /// </summary>
        public static bool Update(GameObject slime, bool pass)
        {
            if (pass) PassThrough(slime); else Solid(slime);
            return IsPassing(slime);
        }

        /// <summary>Forgets a destroyed slime (call from OnDestroy; collisions die with the colliders anyway).</summary>
        public static void Forget(GameObject slime) { if ((object)slime != null) _states.Remove(slime.Pointer); }
    }
}
