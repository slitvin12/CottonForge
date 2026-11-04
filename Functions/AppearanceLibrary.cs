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
    public static SlimeAppearanceStructure AddStructure(this SlimeAppearance app, Mesh mesh, SlimeAppearance.SlimeBone rootBone, SlimeAppearance.SlimeBone parentBone, string elementName)
    {
        GameObject gameObject = app._structures[0].Element.Prefabs[0].gameObject.CopyObject();
        gameObject.GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
        SlimeAppearanceObject component = gameObject.GetComponent<SlimeAppearanceObject>();
        component.IgnoreLODIndex = true;
        component.RootBone = rootBone;
        component.ParentBone = parentBone;
        component.AttachedBones = new Il2CppStructArray<SlimeAppearance.SlimeBone>(0L);
        SlimeAppearanceStructure slimeAppearanceStructure = new SlimeAppearanceStructure(app._structures[0]);
        slimeAppearanceStructure.Element = ScriptableObject.CreateInstance<SlimeAppearanceElement>();
        slimeAppearanceStructure.Element.CastsShadows = true;
        slimeAppearanceStructure.Element.Name = elementName;
        slimeAppearanceStructure.Element.Prefabs = new Il2CppReferenceArray<SlimeAppearanceObject>(new SlimeAppearanceObject[1] { component });
        app._structures = app._structures.Add(slimeAppearanceStructure);
        return slimeAppearanceStructure;
    }
}
