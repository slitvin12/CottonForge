using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(PlortEconomyDirector), "InitModel")]
public static class EconomyPatch
{
    public static void Prefix(PlortEconomyDirector __instance)
    {
        Library.INTERNAL_RefreshMarketData(__instance._settings);
    }
}
