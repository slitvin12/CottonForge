using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.World;

namespace CottonLibrary.Patches.Callback;

[HarmonyPatch(typeof(PlayerZoneTracker), "OnEntered")]
internal static class ZoneEnterPatch
{
    public static void Postfix(ZoneDefinition zone)
    {
        Callbacks.Invoke_onZoneEnter(zone);
    }
}
