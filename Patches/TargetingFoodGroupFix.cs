using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.UI.Localization;
using Il2CppSystem.Collections.Generic;
using MelonLoader;
using UnityEngine.Localization;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(TargetingUI))]
public static class TargetingFoodGroupFix
{
    private static bool _warned;

    [HarmonyPrefix]
    [HarmonyPatch("GetDirectFoodGroupsMsg")]
    public static void DirectPrefix(TargetingUI __instance, SlimeDiet diet)
    {
        EnsureDiet(__instance, diet);
    }

    [HarmonyPrefix]
    [HarmonyPatch("GetModulesFoodGroupsMsg")]
    public static void ModulesPrefix(TargetingUI __instance, SlimeDiet diet)
    {
        EnsureDiet(__instance, diet);
    }

    private static void EnsureDiet(TargetingUI ui, SlimeDiet diet)
    {
        try
        {
            if (ui == null || diet == null)
            {
                return;
            }
            SlimeEatStrings slimeEatStrings = ui.SlimeEatStrings;
            if (slimeEatStrings == null || slimeEatStrings._foodGroupStringMap == null || diet.MajorFoodIdentifiableTypeGroups == null)
            {
                return;
            }
            foreach (IdentifiableTypeGroup majorFoodIdentifiableTypeGroup in diet.MajorFoodIdentifiableTypeGroups)
            {
                EnsureGroup(slimeEatStrings, majorFoodIdentifiableTypeGroup, 0);
            }
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                MelonLogger.Warning("[Cotton] TargetingFoodGroupFix failed: " + ex.Message);
            }
        }
    }

    private static void EnsureGroup(SlimeEatStrings strings, IdentifiableTypeGroup group, int depth)
    {
        if (group == null || depth > 4)
        {
            return;
        }
        if (!strings._foodGroupStringMap.ContainsKey(group))
        {
            LocalizedString localizedString = FindName(group, 0);
            if (localizedString != null)
            {
                strings._foodGroupStringMap.TryAdd(group, localizedString);
            }
        }
        if (group._memberGroups != null)
        {
            List<IdentifiableTypeGroup>.Enumerator enumerator = group._memberGroups.GetEnumerator();
            while (enumerator.MoveNext())
            {
                IdentifiableTypeGroup current = enumerator.Current;
                EnsureGroup(strings, current, depth + 1);
            }
        }
    }

    private static LocalizedString FindName(IdentifiableTypeGroup group, int depth)
    {
        if (group == null || depth > 4)
        {
            return null;
        }
        if (group._localizedName != null)
        {
            return group._localizedName;
        }
        if (group._memberTypes != null)
        {
            List<IdentifiableType>.Enumerator enumerator = group._memberTypes.GetEnumerator();
            while (enumerator.MoveNext())
            {
                IdentifiableType current = enumerator.Current;
                if (current != null && current.localizedName != null)
                {
                    return current.localizedName;
                }
            }
        }
        if (group._memberGroups != null)
        {
            List<IdentifiableTypeGroup>.Enumerator enumerator2 = group._memberGroups.GetEnumerator();
            while (enumerator2.MoveNext())
            {
                LocalizedString localizedString = FindName(enumerator2.Current, depth + 1);
                if (localizedString != null)
                {
                    return localizedString;
                }
            }
        }
        return null;
    }
}
