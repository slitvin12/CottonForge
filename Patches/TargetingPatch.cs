using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.UI.Localization;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(TargetingUI), "Update")]
public class TargetingPatch
{
    private static bool injectedStrings;

    private static SlimeEatStrings eatStrings;

    private static void Prefix(TargetingUI __instance)
    {
        eatStrings = __instance.SlimeEatStrings;
        if (!(eatStrings != null) || injectedStrings || eatStrings._foodGroupStringMap == null)
        {
            return;
        }
        foreach (IdentifiableTypeGroup customGroup in Library.customGroups)
        {
            if (customGroup._localizedName != null && customGroup._isFood)
            {
                eatStrings._foodGroupStringMap.TryAdd(customGroup, customGroup._localizedName);
            }
        }
        injectedStrings = true;
    }
}
