using System;
using System.Collections.Generic;
using System.Linq;
using CottonLibrary;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Natural spawns with a real share. Cotton's <c>MakeSpawnableInZones</c> adds separate spawn constraints, so a modded slime
    /// spawns as its own group regardless of the weights. Forge adds the slime as a member of each existing wild slime set
    /// of the zones with weight <c>S * p / (1 - p)</c> (S = sum of the set's weights), so it gets about share p of that set.
    /// Gold, lucky, tarr and yolky sets and feral constraints are left alone. The game rebuilds the sets after Awake, so the
    /// members are re-checked right before a spawner evaluates them.
    /// </summary>
    public static class Spawns
    {
        sealed class Entry { public SlimeDefinition Def; public Func<float> Share; public string[] Scenes; }
        static readonly List<Entry> _entries = new();
        static readonly HashSet<IntPtr> _touched = new();
        /// <summary>Set names excluded from modded spawns (substring of a member's type name).</summary>
        public static readonly List<string> Excluded = new() { "gold", "lucky", "tarr", "yolky" };
        public static readonly Dictionary<string, int> AddedPerScene = new();
        public static readonly Dictionary<int, string> Modified = new();
        public static int ReAdded;
        /// <summary>Natural spawns per type since start (only counted with the dev console on).</summary>
        public static readonly SortedDictionary<string, int> Natural = new();

        /// <summary>Adds <paramref name="def"/> to the wild slime spawners of <paramref name="zones"/> with about <paramref name="share"/> (0..0.9).</summary>
        public static void Add(SlimeDefinition def, Library.SpawnLocations zones, Func<float> share)
        {
            if (def == null || share == null) return;
            var scenes = Library.GetSceneNamesFromSpawnerZones(zones).Select(x => x.ToLowerInvariant()).ToArray();
            _entries.RemoveAll(e => e.Def.Pointer == def.Pointer);
            _entries.Add(new Entry { Def = def, Share = share, Scenes = scenes });
            ForgeLog.Msg($"  {def.name} spawns in [{string.Join(",", scenes)}] share={Safe.Run("share", share, 0f):0.###}");
        }

        public static void Add(SlimeDefinition def, Library.SpawnLocations zones, float share) => Add(def, zones, () => share);

        static int Apply(DirectedActorSpawner spawner, string scene)
        {
            var constraints = spawner.Constraints;
            if (constraints == null) return 0;
            int added = 0;
            foreach (var e in _entries)
            {
                if (!e.Scenes.Any(z => scene.Contains(z))) continue;
                float p = Mathf.Clamp(Safe.Run("spawn share " + e.Def.name, e.Share, 0f), 0f, 0.9f);
                if (p <= 0f) continue;
                foreach (var c in constraints)
                {
                    if (c == null || c.Feral || c.Slimeset == null || c.Slimeset.Members == null) continue;
                    var members = c.Slimeset.Members;
                    if (members.Length == 0) continue;
                    float sum = 0f; bool skip = false, already = false, anySlime = false;
                    foreach (var m in members)
                    {
                        if (m == null || m.IdentType == null) continue;
                        string n = m.IdentType.name.ToLowerInvariant();
                        if (Excluded.Any(x => n.Contains(x))) skip = true;
                        if (m.IdentType.Pointer == e.Def.Pointer) already = true;
                        if (m.IdentType.Is<SlimeDefinition>()) anySlime = true;
                        sum += m.Weight;
                    }
                    if (skip || already || !anySlime || sum <= 0f) continue;
                    var list = members.ToManagedList();
                    list.Add(new SlimeSet.Member { _prefab = e.Def.prefab, IdentType = e.Def, Weight = sum * p / (1f - p) });
                    c.Slimeset.Members = Gc.Array(list);   // write-barrier safe (runs during gameplay)
                    added++;
                }
            }
            return added;
        }

        static bool IsWildSlimeSpawner(DirectedActorSpawner s) => s.Is<DirectedSlimeSpawner>() && !s.Is<DirectedAnimalSpawner>();

        static string SceneOf(Component c) => c.gameObject.scene.name?.ToLowerInvariant() ?? "";

        internal static void OnSpawnerAwake(DirectedActorSpawner spawner)
        {
            if (_entries.Count == 0 || spawner == null || !IsWildSlimeSpawner(spawner)) return;
            string scene = SceneOf(spawner);
            if (!_entries.Any(e => e.Scenes.Any(z => scene.Contains(z)))) return;
            _touched.Add(spawner.Pointer);
            int k = Apply(spawner, scene);
            if (k <= 0) return;
            AddedPerScene.TryGetValue(scene, out int was);
            AddedPerScene[scene] = was + k;
            Modified[spawner.GetInstanceID()] = scene + "/" + spawner.name;
        }

        internal static void BeforeSpawnCheck(DirectedActorSpawner spawner)
        {
            if (spawner == null || !_touched.Contains(spawner.Pointer)) return;
            int k = Apply(spawner, SceneOf(spawner));
            if (k > 0) { ReAdded += k; Modified[spawner.GetInstanceID()] = SceneOf(spawner) + "/" + spawner.name; }
        }
    }

    // NB: CanSpawn/GetEligibleConstraints take Nullable<float>; Harmony trampolines for those crash in Il2CppInterop
    // (NullReferenceException in Memmove every frame). These have no nullable parameters.
    [HarmonyPatch(typeof(DirectedActorSpawner), nameof(DirectedActorSpawner.CanSpawnSomething))]
    static class SpawnerCanSpawnSomethingPatch
    {
        static void Prefix(DirectedActorSpawner __instance) { try { Spawns.BeforeSpawnCheck(__instance); } catch { } }
    }

    [HarmonyPatch(typeof(DirectedActorSpawner), nameof(DirectedActorSpawner.Spawn))]
    static class SpawnerSpawnPatch
    {
        static void Prefix(DirectedActorSpawner __instance) { try { Spawns.BeforeSpawnCheck(__instance); } catch { } }
    }

    [HarmonyPatch(typeof(DirectedActorSpawner), "Awake")]
    static class SpawnerAwakePatch
    {
        static void Postfix(DirectedActorSpawner __instance) => Safe.Run("spawner awake", () => Spawns.OnSpawnerAwake(__instance));
    }

    [HarmonyPatch(typeof(DirectedSlimeSpawner), nameof(DirectedSlimeSpawner.OnActorSpawned))]
    static class SpawnCountPatch
    {
        static void Postfix(GameObject spawnedObj, IdentifiableType id)
        {
            SlimeEvents.RaiseNaturalSpawn(spawnedObj, id);
            if (!ForgeConfig.DevConsole.Value) return;
            try { string n = id != null ? id.name : "null"; Spawns.Natural.TryGetValue(n, out int k); Spawns.Natural[n] = k + 1; } catch { }
        }
    }
}
