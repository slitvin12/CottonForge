using System;
using System.Collections.Generic;
using System.Linq;
using CottonLibrary.Patches;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(CottonLibrary.CottonMain), "CottonForge", CottonLibrary.Forge.ForgeCore.Version, "slitvin12 (fork of Cotton Library by PinkTarr)")]
[assembly: MelonGame("MonomiPark", "SlimeRancher2")]
// patched class by class in ForgeCore.Init (a broken target must not disable the other patches)
[assembly: MelonLoader.HarmonyDontPatchAll]

namespace CottonLibrary;

public class CottonMain : MelonMod
{
    public override void OnInitializeMelon()
    {
        Forge.ForgeCore.Init(this);
    }

    public override void OnUpdate()
    {
        Forge.ForgeCore.Tick();
    }

    public override void OnLateInitializeMelon()
    {
        foreach (MelonBase registeredMelon in MelonBase.RegisteredMelons)
        {
            if (registeredMelon is CottonMod)
            {
                CottonMod cottonMod = registeredMelon as CottonMod;
                Library.mods.Add(cottonMod);
                MelonLogger.Msg("Cotton registered mod: " + cottonMod.MelonAssembly.Assembly.FullName);
            }
        }
        if ((bool)Library.Get("SR2ELibraryROOT"))
        {
            Library.rootOBJ = Library.Get("CottonLibraryROOT");
            return;
        }
        Library.rootOBJ = new GameObject();
        Library.rootOBJ.SetActive(value: false);
        Library.rootOBJ.name = "CottonLibraryROOT";
        UnityEngine.Object.DontDestroyOnLoad(Library.rootOBJ);
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        switch (sceneName)
        {
        case "GameCore":
            foreach (CottonMod mod in Library.mods)
            {
                mod.OnGameCoreLoaded();
            }
            break;
        case "ZoneCore":
            foreach (CottonMod mod2 in Library.mods)
            {
                mod2.OnZoneCoreLoaded();
            }
            break;
        case "SystemCore":
            foreach (CottonMod mod3 in Library.mods)
            {
                mod3.OnSystemSceneLoaded();
            }
            LocalizationRepair.StartLoop();
            break;
        case "PlayerCore":
            foreach (CottonMod mod4 in Library.mods)
            {
                mod4.OnPlayerSceneLoaded();
            }
            Library.InitializeFX();
            break;
        }
        KeyValuePair<string, List<Action>> keyValuePair = Library.onSceneLoaded.FirstOrDefault((KeyValuePair<string, List<Action>> x) => sceneName.Contains(x.Key));
        if (keyValuePair.Value == null)
        {
            return;
        }
        foreach (Action item in keyValuePair.Value)
        {
            item();
        }
    }
}
