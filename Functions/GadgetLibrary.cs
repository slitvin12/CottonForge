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
    public static GameObject SpawnGadget(this GameObject obj, Vector3 pos, Quaternion rot)
    {
        GameObject gameObject = GadgetDirector.InstantiateGadget(obj, SRSingleton<SystemContext>.Instance.SceneLoader.CurrentSceneGroup, pos, rot);
        GadgetModel gadgetModel = sceneContext.GameModel.InstantiateGadgetModel(gameObject.GetComponent<Gadget>().identType.Cast<GadgetDefinition>(), SRSingleton<SystemContext>.Instance.SceneLoader.CurrentSceneGroup, pos, isPrePlaced: false);
        gadgetModel.eulerRotation = rot.eulerAngles;
        gameObject.GetComponent<Gadget>()._model = gadgetModel;
        SRSingleton<SceneContext>.Instance.ActorRegistry.Register(gameObject.GetComponent<Gadget>());
        return gameObject;
    }

    public static GameObject SpawnGadget(this GameObject obj, Vector3 pos)
    {
        return obj.SpawnGadget(pos, Quaternion.identity);
    }

    public static GameObject SpawnGadget(this GadgetDefinition obj, Vector3 pos, Quaternion rot)
    {
        return obj.prefab.SpawnGadget(pos, rot);
    }

    public static GameObject SpawnGadget(this GadgetDefinition obj, Vector3 pos)
    {
        return obj.prefab.SpawnGadget(pos, Quaternion.identity);
    }

    internal static Gadget RaycastForGadget()
    {
        if (Physics.Raycast(new Ray(Camera.main.transform.position, Camera.main.transform.forward), out var hitInfo))
        {
            Transform parent = hitInfo.collider.transform.parent;
            for (int i = 0; i < 10; i++)
            {
                if (!(parent != null))
                {
                    break;
                }
                Gadget component = parent.GetComponent<Gadget>();
                if (component != null)
                {
                    return component;
                }
                parent = parent.parent;
            }
            return null;
        }
        return null;
    }

    public static GadgetDefinition CreateTeleporter(string name, PurchaseCost cost, Color color1, Color color2, Color color3, Color color4)
    {
        GadgetDefinition gadgetDefinition = UnityEngine.Object.Instantiate(Get<GadgetDefinition>("TeleporterPink"));
        gadgetDefinition.name = name;
        gadgetDefinition.referenceId = "GadgetDefinition." + name;
        gadgetDefinition.SetupForSaving(gadgetDefinition.referenceId);
        gadgetDefinition.prefab = UnityEngine.Object.Instantiate(gadgetDefinition.prefab);
        gadgetDefinition.prefab.GetComponent<Gadget>().identType = gadgetDefinition;
        Material material = gadgetDefinition.prefab.transform.GetChild(0).GetChild(2).GetChild(0)
            .GetChild(1)
            .GetComponent<SkinnedMeshRenderer>()
            .material;
        material.SetColor("_Color00", color1);
        material.SetColor("_Color01", color2);
        material.SetColor("_Color02", color3);
        material.SetColor("_Color10", color4);
        return gadgetDefinition;
    }
}
