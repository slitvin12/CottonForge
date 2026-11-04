using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Persist;
using Il2CppMonomiPark.SlimeRancher;
using MelonLoader;
using System;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(GameModelPushHelpers), "PushGame")]
public static class SaveLoadedPatch
{
    public static void Prefix()
    {
        try
        {
            Library.INTERNAL_RefreshAllSavedIdents();
        }
        catch (Exception value)
        {
            MelonLogger.Error($"[Cotton] Failed to refresh modded idents before loading: {value}");
        }
    }

    public static void Postfix(GameV11 gameState)
    {
        Library.lastGameState = gameState;
        foreach (CottonMod mod in Library.mods)
        {
            try
            {
                mod.OnSavedGameLoaded();
            }
            catch (Exception value)
            {
                MelonLogger.Error($"[Cotton] {mod.Info.Name}.OnSavedGameLoaded failed: {value}");
            }
        }
    }
}

[HarmonyPatch(typeof(GameModelPullHelpers), "PullGame")]
public static class GameSavedPatch
{
    public static void Prefix()
    {
        foreach (CottonMod mod in Library.mods)
        {
            try
            {
                mod.PreGameSaving();
            }
            catch (Exception value)
            {
                MelonLogger.Error($"[Cotton] {mod.Info.Name}.PreGameSaving failed: {value}");
            }
        }
        try
        {
            Library.INTERNAL_RefreshAllSavedIdents();
        }
        catch (Exception value2)
        {
            MelonLogger.Error($"[Cotton] Failed to refresh modded idents before saving: {value2}");
        }
    }

    public static void Postfix(GameV11 __result)
    {
        if (__result != null)
        {
            Library.lastGameState = __result;
        }
    }
}
