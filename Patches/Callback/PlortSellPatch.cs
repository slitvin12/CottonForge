using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Economy;

namespace CottonLibrary.Patches.Callback;

[HarmonyPatch(typeof(PlortEconomyDirector), "RegisterSold")]
internal static class PlortSellPatch
{
    public static void Postfix(IdentifiableType id, int count)
    {
        Callbacks.Invoke_onPlortSold(count, id);
    }
}
