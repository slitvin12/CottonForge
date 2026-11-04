using System;
using HarmonyLib;
using Il2Cpp;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(DirectedActorSpawner), "Awake")]
public class SpawnerPatch
{
    [HarmonyPostfix]
    private static void PostAwake(DirectedActorSpawner __instance)
    {
        foreach (Action<DirectedActorSpawner> item in Library.executeOnSpawnerAwake)
        {
            item(__instance);
        }
    }
}
