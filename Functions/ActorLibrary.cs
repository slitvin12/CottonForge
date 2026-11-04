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
    public interface IColliderData
    {
        int Type => -1;

        float GetRadius();

        Mesh GetMesh();

        float GetWidth();

        float GetHeight();

        float GetDepth();
    }

    public class DefaultColliderData : IColliderData
    {
        public int Type => 1;

        public float GetRadius()
        {
            return 1.5f;
        }

        public Mesh GetMesh()
        {
            throw new NotImplementedException();
        }

        public float GetWidth()
        {
            return 1.5f;
        }

        public float GetHeight()
        {
            return 1.5f;
        }

        public float GetDepth()
        {
            return 1.5f;
        }
    }

    public class SphereColliderData : IColliderData
    {
        public float radius;

        public int Type => 1;

        public float GetRadius()
        {
            return radius;
        }

        public Mesh GetMesh()
        {
            throw new NotImplementedException();
        }

        public float GetWidth()
        {
            return radius;
        }

        public float GetHeight()
        {
            return radius;
        }

        public float GetDepth()
        {
            return radius;
        }
    }

    public class CapsuleColliderData : IColliderData
    {
        public float radius;

        public float length;

        public int Type => 2;

        public float GetRadius()
        {
            return radius;
        }

        public Mesh GetMesh()
        {
            throw new NotImplementedException();
        }

        public float GetWidth()
        {
            return length;
        }

        public float GetHeight()
        {
            return radius;
        }

        public float GetDepth()
        {
            return radius;
        }
    }

    public class MeshColliderData : IColliderData
    {
        public Mesh mesh;

        public int Type => 3;

        public float GetRadius()
        {
            throw new NotImplementedException();
        }

        public Mesh GetMesh()
        {
            return mesh;
        }

        public float GetWidth()
        {
            throw new NotImplementedException();
        }

        public float GetHeight()
        {
            throw new NotImplementedException();
        }

        public float GetDepth()
        {
            throw new NotImplementedException();
        }
    }

    public class CubeColliderData : IColliderData
    {
        public Vector3 size;

        public int Type => 0;

        public Mesh GetMesh()
        {
            throw new NotImplementedException();
        }

        public float GetRadius()
        {
            return size.magnitude;
        }

        public float GetWidth()
        {
            return size.x;
        }

        public float GetHeight()
        {
            return size.y;
        }

        public float GetDepth()
        {
            return size.z;
        }
    }

    /// <summary>
    /// Creates a game object for a veggie/fruit object. This uses the veggie shader ('SR/AMP/Actor/Resource/Veggie').
    /// </summary>
    /// <param name="ident">The identifiable type attached to the food object.</param>
    /// <param name="mesh">The mesh for the food. Use an AssetBundle to import this.</param>
    /// <param name="texture">The main texture for the food</param>
    /// <param name="masks">Masks texture, R/G/B - Occlusion/Smoothness/Emission</param>
    /// <param name="scale">The size of the food game object. The default for a carrot is 0.12f</param>
    /// <returns>The game object of the food.</returns>
    public static GameObject CreateFoodObject(IdentifiableType ident, Mesh mesh, Texture2D texture, Texture2D masks, ref GameObject baitObject, float scale = 0.12f)
    {
        return CreateFoodObject(ident, mesh, texture, masks, scale, new DefaultColliderData(), out baitObject);
    }

    public static GameObject CreateFoodObject(IdentifiableType ident, Mesh mesh, Texture2D texture, Texture2D masks, float scale, IColliderData colliderData, out GameObject baitObject)
    {
        GameObject gameObject = GetVeggie("CarrotVeggie").prefab.CopyObject();
        baitObject = GetVeggie("CarrotVeggie").gordoSnareBaitPrefab.CopyObject();
        gameObject.name = "customFood" + ident.name;
        baitObject.name = "customFood" + ident.name + "_GordoSnareBait";
        gameObject.RemoveComponent<CapsuleCollider>();
        if (colliderData.Type == 3)
        {
            gameObject.RemoveComponent<SphereCollider>();
            gameObject.AddComponent<BoxCollider>().size = new Vector3(colliderData.GetWidth(), colliderData.GetHeight(), colliderData.GetDepth());
        }
        else if (colliderData.Type == 1)
        {
            gameObject.GetComponent<SphereCollider>().radius = colliderData.GetRadius();
        }
        else if (colliderData.Type == 2)
        {
            gameObject.RemoveComponent<SphereCollider>();
            CapsuleCollider capsuleCollider = gameObject.AddComponent<CapsuleCollider>();
            capsuleCollider.radius = colliderData.GetRadius();
            capsuleCollider.height = colliderData.GetHeight();
            capsuleCollider.direction = 0;
        }
        else if (colliderData.Type == 3)
        {
            gameObject.RemoveComponent<SphereCollider>();
            gameObject.AddComponent<MeshCollider>().sharedMesh = colliderData.GetMesh();
        }
        Transform transform = gameObject.transform.FindChild("model_carrot");
        transform.GetComponent<MeshFilter>().mesh = mesh;
        Material material = UnityEngine.Object.Instantiate(transform.GetComponent<MeshRenderer>().material);
        material.SetTexture("_Albedo", texture);
        material.SetTexture("_Masks", masks);
        transform.GetComponent<MeshRenderer>().material = material;
        gameObject.SetObjectIdent(ident);
        gameObject.transform.localScale = Vector3.one * scale;
        ident.gordoSnareBaitPrefab = baitObject;
        Transform child = baitObject.transform.GetChild(0);
        child.GetComponent<MeshFilter>().mesh = mesh;
        child.GetComponent<MeshRenderer>().material = transform.GetComponent<MeshRenderer>().material;
        baitObject.transform.localScale = Vector3.one * scale;
        return gameObject;
    }

    public static GameObject CreateHenObject(IdentifiableType ident, Texture2D texture, Texture2D masks, out GameObject baitObject)
    {
        GameObject gameObject = GetMeat("Hen").prefab.CopyObject();
        baitObject = GetMeat("Hen").gordoSnareBaitPrefab.CopyObject();
        gameObject.name = "customFood" + ident.name;
        baitObject.name = "customFood" + ident.name + "_GordoSnareBait";
        SkinnedMeshRenderer componentInChildren = gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
        Material material = UnityEngine.Object.Instantiate(componentInChildren.material);
        material.SetTexture("_MainTex", texture);
        material.SetTexture("_MaskMap", masks);
        componentInChildren.material = material;
        foreach (MeshRenderer componentsInChild in gameObject.GetComponentsInChildren<MeshRenderer>())
        {
            componentsInChild.material = material;
        }
        gameObject.SetObjectIdent(ident);
        ident.gordoSnareBaitPrefab = baitObject;
        baitObject.transform.GetChild(0).GetChild(0).GetComponent<SkinnedMeshRenderer>()
            .material = componentInChildren.material;
        foreach (MeshRenderer componentsInChild2 in baitObject.GetComponentsInChildren<MeshRenderer>())
        {
            componentsInChild2.material = material;
        }
        return gameObject;
    }

    public static IdentifiableType GetIdent(this GameObject obj)
    {
        try
        {
            return obj.GetComponent<IdentifiableActor>().identType;
        }
        catch
        {
            return null;
        }
    }

    public static IdentifiableType GetIdentifiableType(this GameObject obj)
    {
        IdentifiableActor component = obj.GetComponent<IdentifiableActor>();
        if (component != null)
        {
            return component.identType;
        }
        return null;
    }

    public static GameObject SpawnActor(this GameObject obj, Vector3 pos)
    {
        return obj.SpawnActor(pos, Quaternion.identity);
    }

    public static GameObject SpawnActor(this GameObject obj, Vector3 pos, Vector3 rot)
    {
        return obj.SpawnActor(pos, Quaternion.Euler(rot));
    }

    public static GameObject SpawnActor(this GameObject obj, Vector3 pos, Quaternion rot)
    {
        if (obj == null) return null;
        var actor = obj.GetComponent<IdentifiableActor>();
        return SpawnActorCore(obj, actor != null ? actor.identType : null, pos, rot);
    }

    /// <summary>Spawns an actor of <paramref name="type"/> (slime, plort, food ...) through the game's actor model, like the game itself.</summary>
    public static GameObject SpawnActor(this IdentifiableType type, Vector3 pos, Quaternion rot)
    {
        if (type == null) return null;
        return SpawnActorCore(type.prefab, type, pos, rot);
    }

    static GameObject SpawnActorCore(GameObject prefab, IdentifiableType ident, Vector3 pos, Quaternion rot)
    {
        var sc = SRSingleton<SceneContext>.Instance;
        var group = sc.RegionRegistry.CurrentSceneGroup;
        // Current SR2 builds: InstantiateActor gained a Nullable<AmmoMetadata> parameter whose default (null) makes the
        // Il2CppInterop wrapper throw a NullReferenceException. Prefer the model path the game uses itself
        // (GameModel.InstantiateActorModel + InstantiateActorFromModel); fall back to InstantiateActor with an empty Nullable.
        if (ident != null && sc.GameModel != null)
        {
            ActorModel model = null;
            try { model = sc.GameModel.InstantiateActorModel(ident, group, pos, rot); }
            catch (Exception e) { MelonLogger.Warning($"[Cotton] InstantiateActorModel({ident.name}) failed, using InstantiateActor: {e.Message}"); }
            // A created model owns the actor (it may spawn later when its region loads), so never instantiate a second one.
            if (model != null) return InstantiationHelpers.InstantiateActorFromModel(model);
        }
        if (prefab == null) return null;
        return InstantiationHelpers.InstantiateActor(prefab, group, pos, rot, false,
            SlimeAppearance.AppearanceSaveSet.NONE, SlimeAppearance.AppearanceSaveSet.NONE,
            new Il2CppSystem.Nullable<Il2CppMonomiPark.SlimeRancher.Player.AmmoSlot.AmmoMetadata>());
    }

    public static GameObject SpawnDynamic(this GameObject obj, Vector3 pos, Quaternion rot)
    {
        return InstantiationHelpers.InstantiateDynamic(obj, pos, rot);
    }

    public static void SetObjectPrefab(this IdentifiableType Object, GameObject prefab)
    {
        Object.prefab = prefab;
    }

    public static void SetObjectIdent(this GameObject prefab, IdentifiableType Object)
    {
        if (Object is SlimeDefinition)
        {
            prefab.GetComponent<SlimeEat>().SlimeDefinition = (SlimeDefinition)Object;
            prefab.GetComponent<SlimeAppearanceApplicator>().SlimeDefinition = (SlimeDefinition)Object;
        }
        prefab.GetComponent<IdentifiableActor>().identType = Object;
    }

    public static IdentifiableType CreatePlortType(string Name, Color32 VacColor, Sprite Icon, string RefID, float marketValue, float marketSaturation)
    {
        IdentifiableType identifiableType = ScriptableObject.CreateInstance<IdentifiableType>();
        UnityEngine.Object.DontDestroyOnLoad(identifiableType);
        identifiableType.hideFlags = HideFlags.HideAndDontSave;
        identifiableType.name = Name + "Plort";
        identifiableType.color = VacColor;
        identifiableType.icon = Icon;
        identifiableType.IsPlort = true;
        if (marketValue > 0f)
        {
            MakeSellable(identifiableType, marketValue, marketSaturation);
        }
        identifiableType.AddToGroup("VaccableNonLiquids");
        INTERNAL_SetupLoadForIdent(RefID, identifiableType);
        return identifiableType;
    }

    public static IdentifiableType CreateBlankType(string Name, Color32 VacColor, Sprite Icon, string RefID)
    {
        IdentifiableType identifiableType = UnityEngine.Object.Instantiate(GetVeggie("CarrotVeggie"));
        UnityEngine.Object.DontDestroyOnLoad(identifiableType);
        identifiableType.hideFlags = HideFlags.HideAndDontSave;
        identifiableType.name = Name;
        identifiableType.color = VacColor;
        identifiableType.icon = Icon;
        identifiableType.AddToGroup("VaccableNonLiquids");
        INTERNAL_SetupLoadForIdent(RefID, identifiableType);
        return identifiableType;
    }

    public static void MakeVaccable(this IdentifiableType ident)
    {
        if (!ident.prefab.GetComponent<Vacuumable>())
        {
            throw new NullReferenceException("This object cannot be made vaccable, it's missing a Vacuumable component, you need to add one.");
        }
        ident.AddToGroup("VaccableNonLiquids");
    }

    public static void SetPlortColor(Color32 Top, Color32 Middle, Color32 Bottom, GameObject Prefab)
    {
        Material material = Prefab.GetComponent<MeshRenderer>().material;
        material.SetColor("_TopColor", Top);
        material.SetColor("_MiddleColor", Middle);
        material.SetColor("_BottomColor", Bottom);
    }

    public static void SetPlortTwinColor(Color32 Top, Color32 Middle, Color32 Bottom, GameObject Prefab)
    {
        Material material = Prefab.GetComponent<MeshRenderer>().material;
        material.SetColor("_TwinTopColor", Top);
        material.SetColor("_TwinMiddleColor", Middle);
        material.SetColor("_TwinBottomColor", Bottom);
    }

    public static IdentifiableType GetPlort(string name)
    {
        foreach (IdentifiableType item in plorts.GetAllMembersArray())
        {
            if (item.name.ToUpper() == name.ToUpper())
            {
                return item;
            }
        }
        return null;
    }

    public static IdentifiableType GetVeggie(string name)
    {
        foreach (IdentifiableType item in veggies.GetAllMembersArray())
        {
            if (item.name.ToUpper() == name.ToUpper())
            {
                return item;
            }
        }
        return null;
    }

    public static IdentifiableType GetFruit(string name)
    {
        foreach (IdentifiableType item in fruits.GetAllMembersArray())
        {
            if (item.name.ToUpper() == name.ToUpper())
            {
                return item;
            }
        }
        return null;
    }

    public static IdentifiableType GetMeat(string name)
    {
        foreach (IdentifiableType item in meat.GetAllMembersArray())
        {
            if (item.name.ToUpper() == name.ToUpper())
            {
                return item;
            }
        }
        return null;
    }

    public static IdentifiableType GetFood(string name)
    {
        foreach (IdentifiableType item in food.GetAllMembersArray())
        {
            if (item.name.ToUpper() == name.ToUpper())
            {
                return item;
            }
        }
        return null;
    }

    public static IdentifiableType GetCraft(string name)
    {
        foreach (IdentifiableType item in crafts.GetAllMembersArray())
        {
            if (item.name.ToUpper() == name.ToUpper())
            {
                return item;
            }
        }
        return null;
    }

    public static IdentifiableTypeGroup MakeNewGroup(IdentifiableType[] types, string groupName, IdentifiableTypeGroup[] subGroups = null)
    {
        IdentifiableTypeGroup identifiableTypeGroup = new IdentifiableTypeGroup();
        Il2CppSystem.Collections.Generic.List<IdentifiableType> list = new Il2CppSystem.Collections.Generic.List<IdentifiableType>();
        foreach (IdentifiableType item in types)
        {
            try
            {
                list.Add(item);
            }
            catch
            {
            }
        }
        Il2CppSystem.Collections.Generic.List<IdentifiableTypeGroup> list2 = new Il2CppSystem.Collections.Generic.List<IdentifiableTypeGroup>();
        foreach (IdentifiableTypeGroup item2 in subGroups)
        {
            try
            {
                list2.Add(item2);
            }
            catch
            {
            }
        }
        identifiableTypeGroup._memberTypes = list;
        identifiableTypeGroup._memberGroups = list2;
        SRSingleton<GameContext>.Instance.LookupDirector.RegisterIdentifiableTypeGroup(identifiableTypeGroup);
        return identifiableTypeGroup;
    }

    public static IdentifiableTypeGroup CreateIdentifiableGroup(LocalizedString localizedName, string codeName, System.Collections.Generic.List<IdentifiableType> types, System.Collections.Generic.List<IdentifiableTypeGroup> subGroups, bool isFood = false)
    {
        IdentifiableTypeGroup identifiableTypeGroup = ScriptableObject.CreateInstance<IdentifiableTypeGroup>();
        identifiableTypeGroup._memberTypes = new Il2CppSystem.Collections.Generic.List<IdentifiableType>();
        foreach (IdentifiableType type in types)
        {
            identifiableTypeGroup._memberTypes.Add(type);
        }
        identifiableTypeGroup._memberGroups = new Il2CppSystem.Collections.Generic.List<IdentifiableTypeGroup>();
        foreach (IdentifiableTypeGroup subGroup in subGroups)
        {
            identifiableTypeGroup._memberGroups.Add(subGroup);
        }
        identifiableTypeGroup._isFood = isFood;
        identifiableTypeGroup._localizedName = localizedName;
        identifiableTypeGroup.name = codeName;
        identifiableTypeGroup.AllowedCategories = new Il2CppSystem.Collections.Generic.List<IdentifiableCategory>();
        identifiableTypeGroup._runtimeObject = new IdentifiableTypeGroupRuntimeObject(identifiableTypeGroup);
        customGroups.Add(identifiableTypeGroup);
        return identifiableTypeGroup;
    }
}
