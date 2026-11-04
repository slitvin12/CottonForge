using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Damage;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppMonomiPark.SlimeRancher.Persist;
using Il2CppMonomiPark.SlimeRancher.Script.Util;
using Il2CppMonomiPark.SlimeRancher.Slime;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World;
using Il2CppSystem.Collections.Generic;
using Il2CppSystem.Linq;
using MelonLoader;
using Microsoft.VisualBasic.FileIO;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace CottonLibrary;

public static partial class Library
{
    internal static System.Collections.Generic.List<Action<DirectedActorSpawner>> executeOnSpawnerAwake = new System.Collections.Generic.List<Action<DirectedActorSpawner>>();

    internal static System.Collections.Generic.List<ReplacementSpawnerData> spawnerReplacements = new System.Collections.Generic.List<ReplacementSpawnerData>();

    internal static System.Collections.Generic.List<IdentifiableTypeGroup> customGroups = new System.Collections.Generic.List<IdentifiableTypeGroup>();

    internal static System.Collections.Generic.Dictionary<string, IdentifiableType> savedIdents = new System.Collections.Generic.Dictionary<string, IdentifiableType>();

    internal static System.Collections.Generic.List<CottonMod> mods = new System.Collections.Generic.List<CottonMod>();

    internal static System.Collections.Generic.Dictionary<IdentifiableType, ModdedMarketData> marketData = new System.Collections.Generic.Dictionary<IdentifiableType, ModdedMarketData>(0);

    internal static System.Collections.Generic.Dictionary<PlortEntry, bool> marketPlortEntries = new System.Collections.Generic.Dictionary<PlortEntry, bool>();

    internal static System.Collections.Generic.List<IdentifiableType> removeMarketPlortEntries = new System.Collections.Generic.List<IdentifiableType>();

    internal static GameObject rootOBJ;

    public static IdentifiableTypeGroup slimes;

    public static IdentifiableTypeGroup plorts;

    public static IdentifiableTypeGroup largos;

    public static IdentifiableTypeGroup baseSlimes;

    public static IdentifiableTypeGroup food;

    public static IdentifiableTypeGroup meat;

    public static IdentifiableTypeGroup veggies;

    public static IdentifiableTypeGroup fruits;

    public static IdentifiableTypeGroup nectar;

    public static IdentifiableTypeGroup crafts;

    public static GameObject player;

    private static SlimeAppearanceDirector _mainAppearanceDirector;

    /// <summary>
    /// The key for this is $"{table}__{key}"
    /// </summary>
    internal static System.Collections.Generic.Dictionary<string, LocalizedString> existingTranslations = new System.Collections.Generic.Dictionary<string, LocalizedString>();

    public static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> addedTranslations = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>();

    internal static System.Collections.Generic.List<Action> createLargoActions = new System.Collections.Generic.List<Action>();

    private static System.Collections.Generic.List<string> largoCombos = new System.Collections.Generic.List<string>();

    public static WeatherStateDefinition[] weatherStateDefinitions => Resources.FindObjectsOfTypeAll<WeatherStateDefinition>();

    public static SystemContext systemContext => SRSingleton<SystemContext>.Instance;

    public static GameContext gameContext => SRSingleton<GameContext>.Instance;

    public static SceneContext sceneContext => SRSingleton<SceneContext>.Instance;

    public static SlimeDefinitions slimeDefinitions => gameContext.SlimeDefinitions;

    public static SlimeAppearanceDirector mainAppearanceDirector
    {
        get
        {
            if (_mainAppearanceDirector == null)
            {
                _mainAppearanceDirector = Get<SlimeAppearanceDirector>("MainSlimeAppearanceDirector");
            }
            return _mainAppearanceDirector;
        }
        set
        {
            _mainAppearanceDirector = value;
        }
    }

    public static GameV11 Save => lastGameState;

    public static bool inGame => systemContext.SceneLoader.IsCurrentSceneGroupGameplay();

    internal static Sprite LoadSprite(string fileName)
    {
        return LoadPNG(fileName).ConvertToSprite();
    }
}
