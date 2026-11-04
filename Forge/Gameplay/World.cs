using System;
using System.Collections.Generic;
using CottonLibrary;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Damage;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>Small world helpers needed by almost every slime behaviour (player, time, ground, random).</summary>
    public static partial class World
    {
        // SR2 puts slimes on layer 15 and plorts (and other resources) on layer 11; the rest is learned from the
        // prefabs of vacuumable identifiable types once the game has loaded them (see LearnActorLayers).
        const int KnownActorLayers = (1 << 11) | (1 << 15);
        const int NonGroundLayers = (1 << 2) | (1 << 6) | (1 << 7) | (1 << 8);
        static int _actorMask = KnownActorLayers;
        static int _groundMask = Physics.DefaultRaycastLayers & ~(NonGroundLayers | KnownActorLayers);
        static bool _layersLearned;
        static float _learnAt;

        /// <summary>
        /// Layers that count as ground: everything except ignore-raycast (2), eat triggers (6), tarr-edible (7), player (8)
        /// and the actor layers (slimes 15, plorts / resources 11, plus layers learned from actor prefabs; never Default).
        /// </summary>
        public static int GroundMask { get { EnsureActorLayers(); return _groundMask; } }

        /// <summary>Layers of actors (slimes, plorts, food, chickens, crafts, toys); learned from the game's prefabs at runtime.</summary>
        public static int ActorMask { get { EnsureActorLayers(); return _actorMask; } }

        /// <summary>Learned actor layers as "11 (name), 15 (name)" (diagnostics).</summary>
        public static string ActorLayerNames()
        {
            EnsureActorLayers();
            var parts = new List<string>();
            for (int i = 0; i < 32; i++)
                if ((_actorMask & (1 << i)) != 0) { var n = LayerMask.LayerToName(i); parts.Add(string.IsNullOrEmpty(n) ? i.ToString() : $"{i} ({n})"); }
            return string.Join(", ", parts) + (_layersLearned ? "" : " [not learned yet]");
        }

        static void EnsureActorLayers()
        {
            if (_layersLearned || Time.realtimeSinceStartup < _learnAt) return;
            _learnAt = Time.realtimeSinceStartup + 5f;
            if (!InGame) return;
            try { LearnActorLayers(); }
            catch (Exception e) { ForgeLog.WarnOnce("world.layers", "actor layer scan failed: " + e.Message); }
        }

        /// <summary>
        /// Collects the root layers and solid-collider layers of every vacuumable identifiable type's prefab (slimes, plorts,
        /// food, chickens, crafts ...). Default (0) is never added; the root layers are also removed from <see cref="GroundMask"/>.
        /// Runs automatically once in game; call again after registering prefabs on unusual layers.
        /// </summary>
        public static void LearnActorLayers()
        {
            int actor = KnownActorLayers, roots = KnownActorLayers, types = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<IdentifiableType>())
            {
                if (t == null) continue;
                var pf = t.prefab;
                if (pf == null || pf.GetComponent<Il2CppMonomiPark.SlimeRancher.Vacuumable>() == null) continue;
                types++;
                if (pf.layer != 0) { actor |= 1 << pf.layer; roots |= 1 << pf.layer; }
                foreach (var c in pf.GetComponentsInChildren<Collider>(true))
                    if (c != null && !c.isTrigger && c.gameObject.layer != 0) actor |= 1 << c.gameObject.layer;
            }
            if (types == 0) return;   // types not loaded yet, retry later
            actor &= ~NonGroundLayers;
            roots &= ~NonGroundLayers;
            _actorMask = actor;
            _groundMask = Physics.DefaultRaycastLayers & ~(NonGroundLayers | roots);
            if (!_layersLearned) { _layersLearned = true; ForgeLog.Msg($"actor layers from {types} prefabs: {ActorLayerNames()}"); }
        }
        public static readonly System.Random Rng = new();

        /// <summary>True in a loaded game (not in the main menu).</summary>
        public static bool InGame => SceneContext.Instance != null && SceneContext.Instance.Player != null;
        /// <summary>Player GameObject or null (main menu).</summary>
        public static GameObject Player => SceneContext.Instance != null ? SceneContext.Instance.Player : null;
        /// <summary>Player position (feet), or <see cref="Vector3.zero"/> without a player.</summary>
        public static Vector3 PlayerPos { get { var p = Player; return p != null ? p.transform.position : Vector3.zero; } }
        /// <summary>Distance from the player's chest to <paramref name="pos"/>; <see cref="float.MaxValue"/> without a player.</summary>
        public static float DistToPlayer(Vector3 pos)
        {
            var p = Player;
            return p == null ? float.MaxValue : Vector3.Distance(p.transform.position + Vector3.up * 0.9f, pos);
        }

        public static float Rand(float a, float b) => a + (float)Rng.NextDouble() * (b - a);
        public static int Rand(int minInclusive, int maxExclusive) => Rng.Next(minInclusive, maxExclusive);
        /// <summary>True with probability <paramref name="p"/> (0..1).</summary>
        public static bool Chance(float p) => Rng.NextDouble() < p;
        /// <summary>Random element or default for an empty list.</summary>
        public static T Pick<T>(IList<T> list) => list == null || list.Count == 0 ? default : list[Rng.Next(list.Count)];

        /// <summary>Current hour 0..24, or 12 when time is unavailable.</summary>
        public static float Hour
        {
            get { try { return (float)SceneContext.Instance.TimeDirector.CurrHour(); } catch { return 12f; } }
        }
        /// <summary>Day = 6:00..18:30 (SR2 sunrise / sunset).</summary>
        public static bool IsDay() { float h = Hour; return h >= 6f && h < 18.5f; }

        public static float Agitation(SlimeEmotions e) { try { return e != null ? e.Get(SlimeEmotions.Emotion.AGITATION) : 0f; } catch { return 0f; } }
        public static float Hunger(SlimeEmotions e) { try { return e != null ? e.Get(SlimeEmotions.Emotion.HUNGER) : 1f; } catch { return 1f; } }
        public static float Fear(SlimeEmotions e) { try { return e != null ? e.Get(SlimeEmotions.Emotion.FEAR) : 0f; } catch { return 0f; } }

        /// <summary>Ground below the object within ~0.75 * scale.</summary>
        public static bool Grounded(Transform t, float scale = 1f) =>
            Physics.Raycast(t.position + Vector3.up * (0.35f * scale), Vector3.down, 0.75f * scale, GroundMask, QueryTriggerInteraction.Ignore);

        /// <summary>Ground point straight below <paramref name="from"/> (up to <paramref name="maxDist"/>).</summary>
        public static bool GroundBelow(Vector3 from, out RaycastHit hit, float maxDist = 20f) =>
            Physics.Raycast(from, Vector3.down, out hit, maxDist, GroundMask, QueryTriggerInteraction.Ignore);

        /// <summary>
        /// Random flat ground point at <paramref name="minR"/>..<paramref name="maxR"/> around a position that is visible from it
        /// (no wall in between). Returns false (and <paramref name="point"/> = <paramref name="around"/>) after 8 failed tries.
        /// </summary>
        public static bool GroundPoint(Vector3 around, float minR, float maxR, out Vector3 point)
        {
            for (int i = 0; i < 8; i++)
            {
                float ang = Rand(0f, Mathf.PI * 2f), r = Rand(minR, maxR);
                var probe = around + new Vector3(Mathf.Cos(ang) * r, 2.5f, Mathf.Sin(ang) * r);
                if (Physics.Linecast(around + Vector3.up * 0.6f, probe, GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(probe, Vector3.down, out var hit, 7f, GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.6f) continue;
                point = hit.point;
                return true;
            }
            point = around;
            return false;
        }

        /// <summary>Point on the ground <paramref name="dist"/> m in front of the camera (dev tools, tests).</summary>
        public static Vector3 InFrontOfCamera(float dist = 4f, float lift = 0.6f)
        {
            var cam = Camera.main;
            Transform b = cam != null ? cam.transform : Player != null ? Player.transform : null;
            if (b == null) return Vector3.zero;
            var fwd = b.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            var pos = b.position + fwd * dist;
            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out var hit, 30f, GroundMask, QueryTriggerInteraction.Ignore)) pos = hit.point;
            return pos + Vector3.up * lift;
        }

        /// <summary>Spawns an actor (slime, food, plort ...) of the given type. Returns null when the type has no prefab.</summary>
        public static GameObject Spawn(IdentifiableType type, Vector3 pos, Quaternion? rot = null)
        {
            if (type == null || type.prefab == null) return null;
            return type.SpawnActor(pos, rot ?? Quaternion.identity);
        }

        /// <summary>Identifiable type of an actor GameObject (or of its parents).</summary>
        public static IdentifiableType IdentOf(GameObject go)
        {
            if (go == null) return null;
            var a = go.GetComponentInParent<IdentifiableActor>();
            return a != null ? a.identType : null;
        }

        /// <summary>Live actors in a sphere (one entry per actor), optionally filtered by type.</summary>
        public static List<IdentifiableActor> ActorsNear(Vector3 pos, float radius, Func<IdentifiableType, bool> filter = null)
        {
            var res = new List<IdentifiableActor>();
            var seen = new HashSet<IntPtr>();
            foreach (var c in Physics.OverlapSphere(pos, radius, ActorMask, QueryTriggerInteraction.Ignore))
            {
                if (c == null) continue;
                var a = c.GetComponentInParent<IdentifiableActor>();
                if (a == null || !seen.Add(a.Pointer)) continue;
                if (filter != null && !filter(a.identType)) continue;
                res.Add(a);
            }
            return res;
        }

        /// <summary>Removes an actor the way the game does (keeps the save model consistent).</summary>
        public static void Despawn(GameObject actor, string why = "Cotton")
        {
            if (actor != null) Destroyer.DestroyActor(actor, why, true);
        }
    }

    /// <summary>Damage helpers.</summary>
    public static class Dmg
    {
        static readonly Dictionary<string, DamageSourceDefinition> _src = new();

        /// <summary>First existing <c>DamageSourceDefinition</c> with one of the names (cached), e.g. "SlimeAttack", "Fire".</summary>
        public static DamageSourceDefinition Source(params string[] names)
        {
            foreach (var n in names)
            {
                if (_src.TryGetValue(n, out var d) && d != null) return d;
                foreach (var s in Resources.FindObjectsOfTypeAll<DamageSourceDefinition>())
                    if (s != null && s.name == n) { _src[n] = s; return s; }
            }
            return null;
        }

        /// <summary>Names of every damage source (for picking one).</summary>
        public static List<string> SourceNames()
        {
            var l = new List<string>();
            foreach (var s in Resources.FindObjectsOfTypeAll<DamageSourceDefinition>()) if (s != null) l.Add(s.name);
            l.Sort(StringComparer.Ordinal);
            return l;
        }

        /// <summary>Damages the player. <paramref name="sources"/> = candidate damage source names (the first found is used).</summary>
        public static bool Player(int amount, GameObject from, params string[] sources)
        {
            if (amount <= 0) return false;
            var p = World.Player;
            if (p == null) return false;
            var d = p.GetComponent<PlayerDamageable>() ?? p.GetComponentInChildren<PlayerDamageable>();
            if (d == null) return false;
            try { return d.Damage(new Damage { Amount = amount, DamageSource = Source(sources), SourceObject = from }); }
            catch (Exception e) { ForgeLog.WarnOnce("dmg.player", "Damage player: " + e.Message); return false; }
        }
    }
}
