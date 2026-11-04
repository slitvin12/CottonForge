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
    internal static GameV11 lastGameState;

    public static T Get<T>(string name) where T : UnityEngine.Object
    {
        return Resources.FindObjectsOfTypeAll<T>().FirstOrDefault((T x) => x.name == name);
    }

    public static GameObject CreatePrefab(string Name, GameObject baseObject)
    {
        GameObject gameObject = baseObject.CopyObject();
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        gameObject.name = Name;
        gameObject.transform.parent = rootOBJ.transform;
        foreach (Behaviour component in gameObject.GetComponents<Behaviour>())
        {
            component.enabled = true;
        }
        return gameObject;
    }

    public static Texture2D LoadPNG(string embeddedFile)
    {
        Assembly callingAssembly = Assembly.GetCallingAssembly();
        Stream? manifestResourceStream = callingAssembly.GetManifestResourceStream(callingAssembly.GetName().Name + "." + embeddedFile + ".png");
        byte[] array = new byte[manifestResourceStream.Length];
        manifestResourceStream.Read(array, 0, array.Length);
        Texture2D texture2D = new Texture2D(1, 1);
        Il2CppImageConversionManager.LoadImage(texture2D, array);
        texture2D.filterMode = FilterMode.Bilinear;
        return texture2D;
    }

    public static AssetBundle LoadBundle(string embeddedFile)
    {
        Assembly callingAssembly = Assembly.GetCallingAssembly();
        Stream? manifestResourceStream = callingAssembly.GetManifestResourceStream(callingAssembly.GetName().Name + "." + embeddedFile);
        byte[] array = new byte[manifestResourceStream.Length];
        manifestResourceStream.Read(array, 0, array.Length);
        return AssetBundle.LoadFromMemory(new Il2CppStructArray<byte>(array));
    }

    public static string BaseSlimesListAsString()
    {
        string text = "Base slimes:\n";
        foreach (IdentifiableType item in baseSlimes.GetAllMembersArray())
        {
            text += $"Localized Name: {item.localizedName.GetLocalizedString()}  |  Actual name: {item.name}\n";
        }
        return text;
    }

    public static void ExecuteInFrames(Action action, int frames)
    {
        MelonCoroutines.Start(Wait(action, frames));
    }

    private static IEnumerator Wait(Action action, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }
        action();
    }

    public static GameObject Get(string name)
    {
        return Get<GameObject>(name);
    }
}
