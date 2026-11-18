using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using MelonLoader;
using UnityEngine;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(AutoSaveDirector), "Awake")]
public static class SaveDirectorPatch
{
    public static void Prefix(AutoSaveDirector __instance)
    {
        CottonLibrary.Forge.VanillaSlimes.Snapshot();   // fork: what is vanilla (before any mod creates slimes)
        Library.slimes = Library.Get<IdentifiableTypeGroup>("SlimesGroup");
        Library.baseSlimes = Library.Get<IdentifiableTypeGroup>("BaseSlimeGroup");
        Library.largos = Library.Get<IdentifiableTypeGroup>("LargoGroup");
        Library.meat = Library.Get<IdentifiableTypeGroup>("MeatGroup");
        Library.food = Library.Get<IdentifiableTypeGroup>("FoodGroup");
        Library.veggies = Library.Get<IdentifiableTypeGroup>("VeggieGroup");
        Library.fruits = Library.Get<IdentifiableTypeGroup>("FruitGroup");
        Library.nectar = Library.Get<IdentifiableTypeGroup>("NectarFoodGroup");
        Library.plorts = Library.Get<IdentifiableTypeGroup>("PlortGroup");
        Library.crafts = Library.Get<IdentifiableTypeGroup>("CraftGroup");
        foreach (CottonMod mod in Library.mods)
        {
            try
            {
                mod.SaveDirectorLoading(__instance);
            }
            catch (Exception value)
            {
                MelonLogger.Error($"[Cotton] {mod.Info.Name}.SaveDirectorLoading failed: {value}");
            }
        }
    }

    public static void Postfix()
    {
        try
        {
            Library.PediaDetailInitialize();
        }
        catch (Exception value)
        {
            MelonLogger.Error($"[Cotton] PediaDetailInitialize failed: {value}");
        }
        ToyDefinition toyDefinition = Library.Get<ToyDefinition>("SteamFox");
        if ((bool)toyDefinition)
        {
            Library.INTERNAL_SetupLoadForIdent(toyDefinition.ReferenceId, toyDefinition);
        }
        foreach (CottonMod mod in Library.mods)
        {
            try
            {
                mod.SaveDirectorLoaded();
            }
            catch (Exception value2)
            {
                MelonLogger.Error($"[Cotton] {mod.Info.Name}.SaveDirectorLoaded failed: {value2}");
            }
        }
        foreach (CottonMod mod2 in Library.mods)
        {
            try
            {
                mod2.LateSaveDirectorLoaded();
            }
            catch (Exception value3)
            {
                MelonLogger.Error($"[Cotton] {mod2.Info.Name}.LateSaveDirectorLoaded failed: {value3}");
            }
        }
        foreach (Action createLargoAction in Library.createLargoActions)
        {
            try
            {
                createLargoAction();
            }
            catch (Exception value4)
            {
                MelonLogger.Error($"[Cotton] Largo creation failed: {value4}");
            }
        }
        try
        {
            Library.INTERNAL_RefreshAllSavedIdents();
            Library.INTERNAL_RefreshAllPediaEntries();
        }
        catch (Exception value5)
        {
            MelonLogger.Error($"[Cotton] Failed to register modded idents: {value5}");
        }
        foreach (PediaCategory item in Resources.FindObjectsOfTypeAll<PediaCategory>())
        {
            try
            {
                item.GetRuntimeCategory();
            }
            catch
            {
            }
        }
    }
}
