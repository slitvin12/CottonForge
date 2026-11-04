using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Damage;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppMonomiPark.SlimeRancher.Persist;
using Il2CppMonomiPark.SlimeRancher.Script.Util;
using Il2CppMonomiPark.SlimeRancher.Slime;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World;
using Il2CppSystem.Collections.Generic;
using Il2CppSystem.Linq;
using MelonLoader;
using Microsoft.VisualBasic.FileIO;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace CottonLibrary;

public static partial class Library
{
    [Flags]
    public enum SpawnLocations : ushort
    {
        RainbowFields = 1,
        StarlightStand = 2,
        EmberValley = 4,
        PowderfallBluffs = 8,
        LabyrinthWaterworks = 0x10,
        LabyrinthLavadepths = 0x20,
        LabyrinthDreamland = 0x40,
        LabyrinthHub = 0x80,
        CustomArray = 0x100,
        All = 0xFF
    }

    [Flags]
    public enum SpawnerTypes : byte
    {
        Slime = 1,
        Animal = 2
    }

    public enum SpawningMode
    {
        Default,
        [Obsolete("Doesn't work!")]
        ReplacementBasedSpawning
    }

    [Flags]
    public enum RequiredConditions
    {
        None = 0,
        RequiresAnglerSlime = 1,
        RequiresAnglerLargo = 2,
        RequiresLargosOnly = 4,
        RequiresOnlyPinkOrModdedSlimes = 8
    }

    internal struct ReplacementSpawnerData
    {
        public IdentifiableType ident;

        public float chance;

        public string[] zones;
    }

    public static System.Collections.Generic.Dictionary<string, GameObject> gordos = new System.Collections.Generic.Dictionary<string, GameObject>();

    public static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Action>> onSceneLoaded = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Action>>();

    public static System.Collections.Generic.Dictionary<string, ResourceGrowerDefinition> resourceGrowerDefinitions = new System.Collections.Generic.Dictionary<string, ResourceGrowerDefinition>();

    internal static System.Collections.Generic.List<Action<SpawnResource>> onResourceGrowerAwake = new System.Collections.Generic.List<Action<SpawnResource>>();

    public static void MakeSpawnableInZones(IdentifiableType ident, DirectedActorSpawner.TimeWindow timeWindow, SpawnLocations zones, float weight, SpawnerTypes spawnerTargets)
    {
        MakeSpawnableInZones(ident, timeWindow, zones, weight, spawnerTargets, SpawningMode.Default, RequiredConditions.None, Array.Empty<string>());
    }

    public static void MakeSpawnableInZones(IdentifiableType ident, DirectedActorSpawner.TimeWindow timeWindow, SpawnLocations zones, float weight, SpawnerTypes spawnerTargets, SpawningMode mode, RequiredConditions requiredConditions)
    {
        MakeSpawnableInZones(ident, timeWindow, zones, weight, spawnerTargets, mode, requiredConditions, Array.Empty<string>());
    }

    /// <summary>
    /// Makes an Identifiable able to spawn.
    /// </summary>
    /// <param name="ident">The object's type</param>
    /// <param name="timeWindow">The time of day the object will spawn in.</param>
    /// <param name="zones">Which zones to spawn in.</param>
    /// <param name="weight">The chance to spawn.</param>
    /// <param name="spawnerTargets">Which spawner type to inject into.</param>
    /// <param name="mode">Which spawning behavior to use.</param>
    /// <param name="requiredConditions">Required conditions on when not to inject into a spawner.</param>
    /// <param name="customZoneSceneNames">An array of custom scene names.</param>
    /// <exception cref="InvalidOperationException">Occurs when an invalid SpawningMode is used. Should never happen.</exception>
    public static void MakeSpawnableInZones(IdentifiableType ident, DirectedActorSpawner.TimeWindow timeWindow, SpawnLocations zones, float weight, SpawnerTypes spawnerTargets, SpawningMode mode, RequiredConditions requiredConditions, params string[] customZoneSceneNames)
    {
        System.Collections.Generic.List<string> list = GetSceneNamesFromSpawnerZones(zones);
        foreach (string item in customZoneSceneNames)
        {
            list.Add(item);
        }
        switch (mode)
        {
        case SpawningMode.Default:
            executeOnSpawnerAwake.Add(delegate(DirectedActorSpawner spawner)
            {
                if (ContainsZoneName(spawner.gameObject.scene.name, list))
                {
                    if (spawnerTargets.HasFlag(SpawnerTypes.Slime) && spawner.TryCast<DirectedSlimeSpawner>() != null)
                    {
                        AddSpawningSettings(spawner, ident, weight, timeWindow, requiredConditions);
                    }
                    if (spawnerTargets.HasFlag(SpawnerTypes.Animal) && spawner.TryCast<DirectedAnimalSpawner>() != null)
                    {
                        AddSpawningSettings(spawner, ident, weight, timeWindow, requiredConditions);
                    }
                }
            });
            break;
        case SpawningMode.ReplacementBasedSpawning:
            spawnerReplacements.Add(new ReplacementSpawnerData
            {
                chance = weight,
                ident = ident,
                zones = list.ToArray()
            });
            break;
        default:
            throw new InvalidOperationException("Unknown spawning mode used!");
        }
    }

    public static System.Collections.Generic.List<string> GetSceneNamesFromSpawnerZones(SpawnLocations zones)
    {
        System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
        if (zones.HasFlag(SpawnLocations.RainbowFields))
        {
            list.Add("zoneFields");
        }
        if (zones.HasFlag(SpawnLocations.EmberValley))
        {
            list.Add("zoneGorge");
        }
        if (zones.HasFlag(SpawnLocations.StarlightStand))
        {
            list.Add("zoneStrand");
        }
        if (zones.HasFlag(SpawnLocations.PowderfallBluffs))
        {
            list.Add("zoneBluffs");
        }
        if (zones.HasFlag(SpawnLocations.LabyrinthWaterworks))
        {
            list.Add("LabStrand");
        }
        if (zones.HasFlag(SpawnLocations.LabyrinthLavadepths))
        {
            list.Add("LabValley");
        }
        if (zones.HasFlag(SpawnLocations.LabyrinthDreamland))
        {
            list.Add("Dreamland");
        }
        if (zones.HasFlag(SpawnLocations.LabyrinthHub))
        {
            list.Add("Hub");
        }
        return list;
    }

    public static System.Collections.Generic.List<string> GetDefNamesFromSpawnerZones(SpawnLocations zones)
    {
        System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
        if (zones.HasFlag(SpawnLocations.RainbowFields))
        {
            list.Add("fields");
        }
        if (zones.HasFlag(SpawnLocations.EmberValley))
        {
            list.Add("gorge");
        }
        if (zones.HasFlag(SpawnLocations.StarlightStand))
        {
            list.Add("strand");
        }
        if (zones.HasFlag(SpawnLocations.PowderfallBluffs))
        {
            list.Add("bluffs");
        }
        if (zones.HasFlag(SpawnLocations.LabyrinthWaterworks))
        {
            list.Add("waterworks");
        }
        if (zones.HasFlag(SpawnLocations.LabyrinthLavadepths))
        {
            list.Add("lavadepths");
        }
        if (zones.HasFlag(SpawnLocations.LabyrinthDreamland))
        {
            list.Add("dream");
        }
        if (zones.HasFlag(SpawnLocations.LabyrinthHub))
        {
            list.Add("hub");
        }
        return list;
    }

    internal static bool ContainsZoneName(string sceneName, System.Collections.Generic.List<string> zoneNames)
    {
        foreach (string zoneName in zoneNames)
        {
            if (sceneName.Contains(zoneName))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool IsInZone(string[] zoneNames)
    {
        if (sceneContext == null)
        {
            return false;
        }
        if (sceneContext.Player == null)
        {
            return false;
        }
        PlayerZoneTracker component = sceneContext.Player.GetComponent<PlayerZoneTracker>();
        if (component == null)
        {
            return false;
        }
        ZoneDefinition currentZone = component.GetCurrentZone();
        if (currentZone == null)
        {
            return false;
        }
        foreach (string text in zoneNames)
        {
            if (currentZone.name.ToLower().Contains(text.ToLower()))
            {
                return true;
            }
        }
        return false;
    }

    public static void AddSpawningSettings(DirectedActorSpawner spawner, IdentifiableType ident, float weight, DirectedActorSpawner.TimeWindow timeWindow, RequiredConditions requiredConditions)
    {
        DirectedActorSpawner.SpawnConstraint spawnConstraint = null;
        float num = 0f;
        foreach (DirectedActorSpawner.SpawnConstraint constraint in spawner.Constraints)
        {
            RequiredConditions[] values = Enum.GetValues<RequiredConditions>();
            foreach (RequiredConditions requiredConditions2 in values)
            {
                if ((requiredConditions & requiredConditions2) != requiredConditions2)
                {
                    continue;
                }
                switch (requiredConditions2)
                {
                case RequiredConditions.RequiresAnglerSlime:
                {
                    bool flag4 = false;
                    foreach (SlimeSet.Member member in constraint.Slimeset.Members)
                    {
                        if (member.IdentType.name.ToLower() == "angler")
                        {
                            flag4 = true;
                            break;
                        }
                    }
                    if (!flag4)
                    {
                        return;
                    }
                    break;
                }
                case RequiredConditions.RequiresAnglerLargo:
                {
                    bool flag2 = false;
                    foreach (SlimeSet.Member member2 in constraint.Slimeset.Members)
                    {
                        if (member2.IdentType.name.ToLower().Contains("angler") && member2.IdentType.Cast<SlimeDefinition>().IsLargo)
                        {
                            flag2 = true;
                            break;
                        }
                    }
                    if (!flag2)
                    {
                        return;
                    }
                    break;
                }
                case RequiredConditions.RequiresLargosOnly:
                {
                    bool flag3 = false;
                    foreach (SlimeSet.Member member3 in constraint.Slimeset.Members)
                    {
                        if (!member3.IdentType.Cast<SlimeDefinition>().IsLargo)
                        {
                            flag3 = true;
                            break;
                        }
                    }
                    if (flag3)
                    {
                        return;
                    }
                    break;
                }
                case RequiredConditions.RequiresOnlyPinkOrModdedSlimes:
                {
                    bool flag = false;
                    foreach (SlimeSet.Member member4 in constraint.Slimeset.Members)
                    {
                        if (member4.IdentType.name.ToLower() != "pink" && !savedIdents.ContainsValue(member4.IdentType))
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (flag)
                    {
                        return;
                    }
                    break;
                }
                }
            }
            num = (num / 2f + constraint.Weight / 2f) * 2f;
            if (constraint.Window.TimeMode == timeWindow.TimeMode)
            {
                spawnConstraint = constraint;
                break;
            }
        }
        if (spawnConstraint == null)
        {
            spawnConstraint = new DirectedActorSpawner.SpawnConstraint();
            spawnConstraint.Window = timeWindow;
            spawnConstraint.Feral = false;
            spawnConstraint.Weight = num;
            spawnConstraint.Slimeset = new SlimeSet();
            spawnConstraint.Slimeset.Members = new Il2CppReferenceArray<SlimeSet.Member>(0L);
            spawner.Constraints = spawner.Constraints.Add(spawnConstraint);
        }
        SlimeSet.Member obj = new SlimeSet.Member
        {
            _prefab = ident.prefab,
            IdentType = ident,
            Weight = weight
        };
        spawnConstraint.Slimeset.Members = spawnConstraint.Slimeset.Members.Add(obj);
    }

    public static void SetResourceGrower(IdentifiableType ident, float chance, int minAmount, string spawnerObjectName, SpawnLocations zones, params string[] blacklistInSpawnerName)
    {
        onResourceGrowerAwake.Add(delegate(SpawnResource spawner)
        {
            string text = spawnerObjectName;
            int num = 0;
            string[] array = blacklistInSpawnerName;
            foreach (string value in array)
            {
                text += $"-blacklist{num}_{value}";
                num++;
            }
            text += $"-zones_{zones}";
            if (GetSceneNamesFromSpawnerZones(zones).Any((string scene) => spawner.gameObject.scene.name.Contains(scene)) && spawner.gameObject.name.Contains(spawnerObjectName) && !blacklistInSpawnerName.Any((string name) => spawner.gameObject.name.Contains(name)))
            {
                resourceGrowerDefinitions.TryGetValue(text, out var value2);
                if (value2 == null)
                {
                    value2 = UnityEngine.Object.Instantiate(spawner.ResourceGrowerDefinition);
                    value2.name = text;
                    UnityEngine.Object.DontDestroyOnLoad(value2);
                }
                if (value2._resources.FirstOrDefault((ResourceSpawnerDefinition.WeightedResourceEntry x) => x.ResourceIdentifiableType.name == ident.name) == null)
                {
                    value2._resources = value2._resources.Add(new ResourceSpawnerDefinition.WeightedResourceEntry
                    {
                        MinimumAmount = minAmount,
                        ResourceIdentifiableType = ident,
                        Weight = chance
                    });
                }
                spawner._resourceGrowerDefinition = value2;
                resourceGrowerDefinitions.TryAdd(text, value2);
            }
        });
    }

    public static void CreateGordoSpawnLocation(IdentifiableType ident, SpawnLocations zone, Vector3 position, Vector3 rotation, string cottonIdentifier)
    {
        string key = GetSceneNamesFromSpawnerZones(zone)[0];
        if (!onSceneLoaded.TryGetValue(key, out var value))
        {
            value = new System.Collections.Generic.List<Action>();
            onSceneLoaded.Add(key, value);
        }
        value.Add(delegate
        {
            if (gordos.TryGetValue(cottonIdentifier, out var value2))
            {
                if (value2 == null)
                {
                    gordos[cottonIdentifier] = InstantiationHelpers.InstantiateDynamic(ident.prefab, position, Quaternion.Euler(rotation));
                }
            }
            else
            {
                gordos.Add(cottonIdentifier, InstantiationHelpers.InstantiateDynamic(ident.prefab, position, Quaternion.Euler(rotation)));
            }
        });
    }
}
