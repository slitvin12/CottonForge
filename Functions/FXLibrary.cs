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
    public static System.Collections.Generic.Dictionary<string, GameObject> allFXObjects = new System.Collections.Generic.Dictionary<string, GameObject>();

    internal static void InitializeFX()
    {
        foreach (ParticleSystem item in Resources.FindObjectsOfTypeAll<ParticleSystem>())
        {
            allFXObjects.TryAdd(item.gameObject.name, item.gameObject);
        }
    }

    public static GameObject GetFXObject(string name)
    {
        return allFXObjects[name];
    }

    public static GameObject PlayFX(string name, Vector3 position, Quaternion rotation)
    {
        return FXHelpers.SpawnAndPlayFX(GetFXObject(name), position, rotation);
    }
}
