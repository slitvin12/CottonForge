using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppSystem;
using Il2CppSystem.Collections.Generic;
using Il2CppSystem.Linq;
using Il2CppSystem.Reflection;
using UnityEngine;

namespace CottonLibrary;

public static class ExtentionLibrary
{
    public static Il2CppSystem.Type CPPTypeOf(this System.Type type)
    {
        string text = type.AssemblyQualifiedName;
        if (text.ToLower().StartsWith("il2cpp"))
        {
            text = text.Substring("il2cpp".Length);
        }
        return Il2CppSystem.Type.GetType(text);
    }

    public static T getObjRec<T>(this GameObject obj, string name) where T : class
    {
        System.Collections.Generic.List<GameObject> allChildren = obj.transform.GetAllChildren();
        for (int i = 0; i < allChildren.Count; i++)
        {
            if (allChildren[i].name == name)
            {
                if (typeof(T) == typeof(GameObject))
                {
                    return allChildren[i] as T;
                }
                if (typeof(T) == typeof(Transform))
                {
                    return allChildren[i].transform as T;
                }
                if (allChildren[i].GetComponent<T>() != null)
                {
                    return allChildren[i].GetComponent<T>();
                }
            }
        }
        return null;
    }

    public static System.Collections.Generic.List<GameObject> GetAllChildren(this GameObject obj)
    {
        Transform transform = obj.transform;
        System.Collections.Generic.List<GameObject> list = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            list.Add(child.gameObject);
            list.AddRange(child.GetAllChildren());
        }
        return list;
    }

    public static T[] GetAllChildrenOfType<T>(this GameObject obj) where T : Component
    {
        System.Collections.Generic.List<T> list = new System.Collections.Generic.List<T>();
        foreach (GameObject allChild in obj.GetAllChildren())
        {
            if (allChild.GetComponent<T>() != null)
            {
                list.Add(allChild.GetComponent<T>());
            }
        }
        return list.ToArray();
    }

    public static T[] GetAllChildrenOfType<T>(this Transform obj) where T : Component
    {
        System.Collections.Generic.List<T> list = new System.Collections.Generic.List<T>();
        foreach (GameObject allChild in obj.GetAllChildren())
        {
            if (allChild.GetComponent<T>() != null)
            {
                list.Add(allChild.GetComponent<T>());
            }
        }
        return list.ToArray();
    }

    public static T GetObjRec<T>(this Transform transform, string name) where T : class
    {
        System.Collections.Generic.List<GameObject> allChildren = transform.GetAllChildren();
        for (int i = 0; i < allChildren.Count; i++)
        {
            if (allChildren[i].name == name)
            {
                if (typeof(T) == typeof(GameObject))
                {
                    return allChildren[i] as T;
                }
                if (typeof(T) == typeof(Transform))
                {
                    return allChildren[i].transform as T;
                }
                if (allChildren[i].GetComponent<T>() != null)
                {
                    return allChildren[i].GetComponent<T>();
                }
            }
        }
        return null;
    }

    public static System.Collections.Generic.List<GameObject> GetAllChildren(this Transform container)
    {
        System.Collections.Generic.List<GameObject> list = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            list.Add(child.gameObject);
            list.AddRange(child.gameObject.GetAllChildren());
        }
        return list;
    }

    public static void SetupForSaving(this IdentifiableType ident, string RefID)
    {
        Library.savedIdents.TryAdd(RefID, ident);
    }

    /// <summary>Duplicates skipped by <see cref="AddToGroup"/> (dev console).</summary>
    internal static int GroupDuplicatesSkipped;

    /// <summary>
    /// Adds the type to the group (asset and runtime group). Fork: skips types that are already members (0.3.1 added
    /// duplicates, e.g. MakeVaccable after CreatePlortType) and a missing group is logged instead of a NullReferenceException.
    /// </summary>
    public static void AddToGroup(this IdentifiableType type, string groupName)
    {
        IdentifiableTypeGroup identifiableTypeGroup = Library.Get<IdentifiableTypeGroup>(groupName);
        if (identifiableTypeGroup == null || type == null)
        {
            MelonLoader.MelonLogger.Warning($"[Cotton] AddToGroup: group '{groupName}' not found ({(type != null ? type.name : "null")})");
            return;
        }
        if (!identifiableTypeGroup._memberTypes.Contains(type)) identifiableTypeGroup._memberTypes.Add(type);
        else GroupDuplicatesSkipped++;
        var runtime = identifiableTypeGroup.GetRuntimeObject();
        if (runtime != null && !runtime._memberTypes.Contains(type)) runtime._memberTypes.Add(type);
    }

    internal static void RefreshIfNotFound(this SaveReferenceTranslation table, IdentifiableType ident)
    {
        try
        {
            table.GetPersistenceId(ident);
        }
        catch
        {
            foreach (System.Collections.Generic.KeyValuePair<string, IdentifiableType> savedIdent in Library.savedIdents)
            {
                Library.INTERNAL_SetupSaveForIdent(savedIdent.Key, savedIdent.Value);
            }
        }
    }

    public static Il2CppArrayBase<IdentifiableType> GetAllMembersArray(this IdentifiableTypeGroup group)
    {
        return group.GetAllMembers().ToArray();
    }

    public static Sprite ConvertToSprite(this Texture2D texture)
    {
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 1f);
    }

    public static GameObject CopyObject(this GameObject obj)
    {
        return UnityEngine.Object.Instantiate(obj, Library.rootOBJ.transform);
    }

    /// <summary>
    /// Use this for copying components, please make sure you are copying the same types!
    /// </summary>
    public static void CopyFields(this UnityEngine.Object target, UnityEngine.Object source)
    {
        foreach (FieldInfo field in source.GetIl2CppType().GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!field.IsLiteral || field.IsInitOnly)
            {
                try
                {
                    target.GetIl2CppType().GetField(field.Name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, field.GetValue(source));
                }
                catch
                {
                }
            }
        }
    }

    public static Il2CppReferenceArray<T> Add<T>(this Il2CppReferenceArray<T> array, T obj) where T : Il2CppObjectBase
    {
        Il2CppSystem.Collections.Generic.List<T> list = new Il2CppSystem.Collections.Generic.List<T>();
        foreach (T item in array)
        {
            list.Add(item);
        }
        list.Add(obj);
        array = list.ToArray().Cast<Il2CppReferenceArray<T>>();
        return array;
    }

    public static Il2CppReferenceArray<T> AddRange<T>(this Il2CppReferenceArray<T> array, Il2CppReferenceArray<T> obj) where T : Il2CppObjectBase
    {
        Il2CppSystem.Collections.Generic.List<T> list = new Il2CppSystem.Collections.Generic.List<T>();
        foreach (T item in array)
        {
            list.Add(item);
        }
        foreach (T item2 in obj)
        {
            list.Add(item2);
        }
        array = list.ToArray().Cast<Il2CppReferenceArray<T>>();
        return array;
    }

    public static Il2CppReferenceArray<T> AddRangeNoMultiple<T>(this Il2CppReferenceArray<T> array, Il2CppReferenceArray<T> obj) where T : Il2CppObjectBase
    {
        Il2CppSystem.Collections.Generic.List<T> list = new Il2CppSystem.Collections.Generic.List<T>();
        foreach (T item in array)
        {
            if (!list.Contains(item))
            {
                list.Add(item);
            }
        }
        foreach (T item2 in obj)
        {
            if (!list.Contains(item2))
            {
                list.Add(item2);
            }
        }
        array = list.ToArray().Cast<Il2CppReferenceArray<T>>();
        return array;
    }

    public static void AddListRange<T>(this Il2CppSystem.Collections.Generic.List<T> list, Il2CppSystem.Collections.Generic.List<T> obj) where T : Il2CppObjectBase
    {
        Il2CppSystem.Collections.Generic.List<T>.Enumerator enumerator = obj.GetEnumerator();
        while (enumerator.MoveNext())
        {
            T current = enumerator.Current;
            list.Add(current);
        }
    }

    public static void AddListRangeNoMultiple<T>(this Il2CppSystem.Collections.Generic.List<T> list, Il2CppSystem.Collections.Generic.List<T> obj) where T : Il2CppObjectBase
    {
        Il2CppSystem.Collections.Generic.List<T>.Enumerator enumerator = obj.GetEnumerator();
        while (enumerator.MoveNext())
        {
            T current = enumerator.Current;
            if (!list.Contains(current))
            {
                list.Add(current);
            }
        }
    }

    public static Il2CppStringArray AddString(this Il2CppStringArray array, string str)
    {
        string[] array2 = new string[array.Count + 1];
        int num = 0;
        foreach (string item in array)
        {
            array2[num] = item;
            num++;
        }
        array2[num] = str;
        return new Il2CppStringArray(array2);
    }

    public static void MakePrefab(this GameObject obj)
    {
        UnityEngine.Object.DontDestroyOnLoad(obj);
        obj.transform.parent = Library.rootOBJ.transform;
    }

    public static void MakeNotSellable(this IdentifiableType ident)
    {
        Library.MakeNOTSellable(ident);
    }

    public static bool IsSellable(this IdentifiableType ident)
    {
        return ident.IsSellable();
    }

    public static bool MakeSellable(this IdentifiableType ident, float marketValue, float marketSaturation, bool hideInMarket = false)
    {
        return ident.MakeSellable(marketValue, marketSaturation, hideInMarket);
    }

    public static bool RemoveComponent<T>(this Transform obj) where T : Component
    {
        try
        {
            T component = obj.GetComponent<T>();
            _ = component.gameObject;
            UnityEngine.Object.Destroy(component);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RemoveComponent<T>(this GameObject obj) where T : Component
    {
        try
        {
            T component = obj.GetComponent<T>();
            _ = component.gameObject;
            UnityEngine.Object.Destroy(component);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RemoveComponent<T>(this Component obj) where T : Component
    {
        try
        {
            T component = obj.GetComponent<T>();
            _ = component.gameObject;
            UnityEngine.Object.Destroy(component);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static T AddComponent<T>(this Component obj) where T : Component
    {
        return obj.gameObject.AddComponent<T>();
    }

    public static bool IsInsideRange(this int number, int rangeMin, int rangeMax)
    {
        if (number >= rangeMin)
        {
            return number <= rangeMax;
        }
        return false;
    }

    internal static bool TryAddComponentTypeIfNull(this GameObject obj, Il2CppSystem.Type type, out Component comp)
    {
        if (!obj.GetComponent(type))
        {
            comp = obj.AddComponent(type);
            return true;
        }
        comp = null;
        return false;
    }

    public static bool ContainsAny(this string str, params string[] check)
    {
        foreach (string value in check)
        {
            if (str.Contains(value))
            {
                return true;
            }
        }
        return false;
    }
}
