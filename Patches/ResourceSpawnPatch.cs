using System;
using CottonLibrary;
using HarmonyLib;
using Il2Cpp;

[HarmonyPatch(typeof(SpawnResource), "Awake")]
public class ResourceSpawnPatch
{
    private static void Prefix(SpawnResource __instance)
    {
        foreach (Action<SpawnResource> item in Library.onResourceGrowerAwake)
        {
            item(__instance);
        }
    }
}
