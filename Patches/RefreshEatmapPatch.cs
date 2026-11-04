using HarmonyLib;
using Il2Cpp;

namespace CottonLibrary.Patches;

public class RefreshEatmapPatch
{
    [HarmonyPatch(typeof(SlimeDiet), "RefreshEatMap")]
    [HarmonyPostfix]
    public static void AddCustomEats(SlimeDiet __instance, SlimeDefinitions definitions, SlimeDefinition definition)
    {
        if (definition == null || __instance == null || __instance.EatMap == null || !Library.customEatmaps.TryGetValue(definition, out var value))
        {
            return;
        }
        foreach (SlimeDiet.EatMapEntry item in value)
        {
            if (item != null && item.EatsIdent != null)
            {
                __instance.EatMap.Add(item);
            }
        }
    }
}
