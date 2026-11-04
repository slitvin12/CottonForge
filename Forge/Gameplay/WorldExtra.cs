using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using UnityEngine;

namespace CottonLibrary.Forge
{
    public static partial class World
    {
        /// <summary>Like <see cref="GroundPoint"/>, but roughly in direction <paramref name="dir"/> (± <paramref name="spreadDeg"/>).</summary>
        public static bool GroundPointToward(Vector3 around, Vector3 dir, float minR, float maxR, float spreadDeg, out Vector3 point)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return GroundPoint(around, minR, maxR, out point);
            dir.Normalize();
            for (int i = 0; i < 8; i++)
            {
                var d = Quaternion.Euler(0f, Rand(-spreadDeg, spreadDeg), 0f) * dir;
                var probe = around + d * Rand(minR, maxR) + Vector3.up * 2.5f;
                if (Physics.Linecast(around + Vector3.up * 0.6f, probe, GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(probe, Vector3.down, out var hit, 7f, GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.6f) continue;
                point = hit.point;
                return true;
            }
            point = around;
            return false;
        }

        /// <summary>Rest-pose bounds of a skinned mesh (from its mesh, not the animated renderer bounds) in <paramref name="root"/>'s local space.</summary>
        public static Bounds RootBounds(Transform root, SkinnedMeshRenderer r)
        {
            var mesh = r.sharedMesh;
            if (mesh == null)
            {
                var wb = r.bounds;
                var lb = new Bounds(root.InverseTransformPoint(wb.center), Vector3.zero);
                lb.Encapsulate(root.InverseTransformPoint(wb.min)); lb.Encapsulate(root.InverseTransformPoint(wb.max));
                return lb;
            }
            var mb = mesh.bounds;
            var b = new Bounds(root.InverseTransformPoint(r.transform.TransformPoint(mb.center)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                b.Encapsulate(root.InverseTransformPoint(r.transform.TransformPoint(c)));
            }
            return b;
        }

        /// <summary>All loaded actors whose identifiable type is named <paramref name="typeName"/> (case-insensitive).</summary>
        public static List<GameObject> AllOf(string typeName)
        {
            var r = new List<GameObject>();
            foreach (var id in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
            {
                var t = id != null ? id.identType : null;
                if (t != null && string.Equals(t.name, typeName, StringComparison.OrdinalIgnoreCase)) r.Add(id.gameObject);
            }
            return r;
        }

        /// <summary>The loaded actor of that type closest to <paramref name="from"/> (default: the camera), or null.</summary>
        public static GameObject Nearest(string typeName, Vector3? from = null)
        {
            var cam = Camera.main;
            var p = from ?? (cam != null ? cam.transform.position : PlayerPos);
            GameObject best = null; float bd = float.MaxValue;
            foreach (var go in AllOf(typeName))
            {
                float d = (go.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = go; }
            }
            return best;
        }

        /// <summary>Holds an actor still (kinematic, no velocity) for <paramref name="seconds"/> of real time — screenshots, tests.</summary>
        public static void Freeze(GameObject actor, float seconds)
        {
            var rb = actor != null ? actor.GetComponent<Rigidbody>() : null;
            if (rb == null) return;
            if (!rb.isKinematic) rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
            Scheduler.Seconds(seconds, () => { if (rb != null) rb.isKinematic = false; }, "unfreeze");
        }

        /// <summary>
        /// Moves an actor instantly (teleporting slimes, blinks): sets rigidbody and transform, stops it, clears its trails
        /// (no streak across the map) and plays <paramref name="fxFrom"/>/<paramref name="fxTo"/> (null = none) at both ends.
        /// </summary>
        public static void Blink(GameObject actor, Vector3 to, string fxFrom = "FX Despawn", string fxTo = "FX Slime Spawn", float fxScale = 1f)
        {
            if (actor == null) return;
            var t = actor.transform;
            var from = t.position;
            var rb = actor.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = to; if (!rb.isKinematic) rb.linearVelocity = Vector3.zero; }
            t.position = to;
            foreach (var tr in actor.GetComponentsInChildren<TrailRenderer>(true)) if (tr != null) tr.Clear();
            if (!string.IsNullOrEmpty(fxFrom)) Fx.Play(fxFrom, from + Vector3.up * 0.5f, fxScale);
            if (!string.IsNullOrEmpty(fxTo)) Fx.Play(fxTo, to + Vector3.up * 0.5f, fxScale);
        }

        // ------------------------------------------------------------------ player
        /// <summary>Player health, max health and radiation (zeros outside a game).</summary>
        public static (int hp, int max, float rad) PlayerStats()
        {
            var ps = SceneContext.Instance != null ? SceneContext.Instance.PlayerState : null;
            if (ps == null) return (0, 0, 0f);
            return ((int)ps.GetCurrHealth(), (int)ps.GetMaxHealth(), ps.GetCurrRad());
        }

        /// <summary>Adds (positive) or removes (negative) radiation on the player.</summary>
        public static void Rads(float amount)
        {
            var ps = SceneContext.Instance != null ? SceneContext.Instance.PlayerState : null;
            if (ps == null) return;
            if (amount >= 0f) ps.AddRads(amount); else ps.RemoveRads(-amount);
        }

        // ------------------------------------------------------------------ teleport
        /// <summary>Moves the player to <paramref name="pos"/> inside the current region (no scene loading; gadget teleporters).</summary>
        public static bool TeleportPlayer(Vector3 pos)
        {
            if (!InGame) return false;
            try
            {
                if (IPlayerController.TryGet(out var c)) { c.StopAbilitiesForTeleport(); c.ResetVelocity(false); }
                var loader = SystemContext.Instance.SceneLoader;
                var tp = SceneContext.Instance.Player.GetComponentInChildren<TeleportablePlayer>(true);
                if (tp == null) return false;
                tp.TeleportTo(pos, loader.CurrentSceneGroup, new Il2CppSystem.Nullable<Vector3>(), false);
                return true;
            }
            catch (Exception e) { ForgeLog.WarnOnce("tpplayer", $"World.TeleportPlayer: {e.Message}"); return false; }
        }

        /// <summary>Gameplay scene groups (regions) and named editor locations ("group" / "location" lines), for <see cref="Teleport"/>.</summary>
        public static List<string> Locations()
        {
            var r = new List<string>();
            foreach (var g in Resources.FindObjectsOfTypeAll<SceneGroup>())
                if (g != null && g.IsGameplay) r.Add($"group {g.name} {g.InitialPlayerPosition}");
            foreach (var d in Resources.FindObjectsOfTypeAll<EditorLocationData>())
                for (int i = 0; i < d.Count; i++)
                {
                    var x = d[i];
                    r.Add($"location {x.name.Replace(' ', '_')} {x.position} {x.sceneGroup?.name}");
                }
            return r;
        }

        /// <summary>
        /// Native teleport of the player (across regions too) to a named editor location or a scene group (name part,
        /// '_' = space), optionally to an exact position. Returns a short description or null when nothing matched.
        /// </summary>
        public static string Teleport(string where, Vector3? position = null)
        {
            if (string.IsNullOrEmpty(where) || !InGame) return null;
            string q = where.Replace('_', ' ');
            SceneGroup group = null; Vector3 pos = default; bool havePos = false;
            foreach (var d in Resources.FindObjectsOfTypeAll<EditorLocationData>())
                for (int i = 0; i < d.Count && group == null; i++)
                {
                    var x = d[i];
                    if (x.sceneGroup != null && string.Equals(x.name, q, StringComparison.OrdinalIgnoreCase)) { group = x.sceneGroup; pos = x.position; havePos = true; }
                }
            if (group == null)
                foreach (var g in Resources.FindObjectsOfTypeAll<SceneGroup>())
                    if (g != null && g.IsGameplay && g.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) { group = g; break; }
            if (group == null) return null;
            if (position.HasValue) { pos = position.Value; havePos = true; }
            if (!havePos) pos = group.InitialPlayerPosition;
            if (IPlayerController.TryGet(out var c)) { c.StopAbilitiesForTeleport(); c.ResetVelocity(false); }
            var loader = SystemContext.Instance.SceneLoader;
            var tp = SceneContext.Instance.Player.GetComponentInChildren<TeleportablePlayer>(true);
            string info = $"{group.name} {pos} from {loader.CurrentSceneGroup?.name}";
            if (loader.CurrentSceneGroup != group)
            {
                var prm = new SceneLoadingParameters
                {
                    TeleportPlayer = true, PauseGame = true,
                    TeleportPosition = new Il2CppSystem.Nullable<Vector3> { hasValue = true, value = pos },
                    TeleportRotation = new Il2CppSystem.Nullable<Vector3>(),
                    ReloadAllCoreScenes = false, ReloadAllNonCoreScenes = false
                };
                tp.PrepareForUnmanagedTeleport();
                loader.LoadSceneGroup(group, prm);
            }
            else tp.TeleportTo(pos, group, new Il2CppSystem.Nullable<Vector3>(), false);
            return info;
        }
    }
}
