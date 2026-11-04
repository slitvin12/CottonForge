using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.World;

namespace CottonLibrary.Patches.Callback;

[HarmonyPatch(typeof(PlayerZoneTracker), "OnExited")]
internal static class ZoneExitPatch
{
    public static void Postfix(ZoneDefinition zone)
    {
        Callbacks.Invoke_onZoneExit(zone);
    }
}
