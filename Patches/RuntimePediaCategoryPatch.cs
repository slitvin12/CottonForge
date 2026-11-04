using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using MelonLoader;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(PediaCategory), "GetRuntimeCategory")]
public class RuntimePediaCategoryPatch
{
    private static Dictionary<string, Library.PediaCategoryType> categories = new Dictionary<string, Library.PediaCategoryType>
    {
        {
            "Science",
            Library.PediaCategoryType.Science
        },
        {
            "Slimes",
            Library.PediaCategoryType.Slimes
        },
        {
            "World",
            Library.PediaCategoryType.World
        },
        {
            "Ranch",
            Library.PediaCategoryType.Ranch
        },
        {
            "Tutorials",
            Library.PediaCategoryType.Tutorial
        },
        {
            "Toys",
            Library.PediaCategoryType.Toys
        },
        {
            "Resources",
            Library.PediaCategoryType.Resources
        },
        {
            "Blueprints",
            Library.PediaCategoryType.Blueprints
        },
        {
            "Weather",
            Library.PediaCategoryType.Weather
        }
    };

    private static bool _starlightResolved;

    private static MethodInfo _starlightPostfix;

    private static bool _starlightFailed;

    public static void Postfix(PediaCategory __instance, IPediaRuntimeCategory<PediaEntry> __result)
    {
        PediaRuntimeCategory pediaRuntimeCategory = null;
        try
        {
            pediaRuntimeCategory = __result?.TryCast<PediaRuntimeCategory>();
        }
        catch
        {
        }
        if (categories.TryGetValue(__instance.name, out var value))
        {
            try
            {
                foreach (PediaEntry item in Library.pediaEntries[value])
                {
                    if (!(item == null))
                    {
                        if (!__instance._items.Contains(item))
                        {
                            __instance._items = __instance._items.Add(item);
                        }
                        if (pediaRuntimeCategory != null && !pediaRuntimeCategory._items.Contains(item))
                        {
                            pediaRuntimeCategory._items.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Cotton] Failed to add pedia entries to " + __instance.name + ": " + ex.Message);
            }
        }
        StarlightBridge(__instance, pediaRuntimeCategory);
    }

    private static void StarlightBridge(PediaCategory category, PediaRuntimeCategory runtimeCategory)
    {
        if (_starlightFailed || runtimeCategory == null)
        {
            return;
        }
        try
        {
            if (!_starlightResolved)
            {
                _starlightResolved = true;
                Type type = (from a in AppDomain.CurrentDomain.GetAssemblies()
                    select a.GetType("Starlight.Prism.Patches.RuntimePediaCategoryPatch", throwOnError: false)).FirstOrDefault((Type t) => t != null);
                if (type == null)
                {
                    _starlightFailed = true;
                    return;
                }
                MethodInfo method = type.GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                ParameterInfo[] array = method?.GetParameters();
                if (method == null || array.Length != 2 || array[1].ParameterType != typeof(PediaRuntimeCategory))
                {
                    _starlightFailed = true;
                    return;
                }
                HarmonyLib.Patches patchInfo = HarmonyLib.Harmony.GetPatchInfo(AccessTools.Method(typeof(PediaCategory), "GetRuntimeCategory"));
                if (patchInfo != null && patchInfo.Postfixes.Any((Patch p) => p.PatchMethod.DeclaringType == type))
                {
                    _starlightFailed = true;
                    return;
                }
                _starlightPostfix = method;
                MelonLogger.Msg("[Cotton] Bridging Starlight pedia categories (Starlight patch is not compatible with this game version).");
            }
            if (!(_starlightPostfix == null))
            {
                object[] parameters = new object[2] { category, runtimeCategory };
                _starlightPostfix.Invoke(null, parameters);
            }
        }
        catch (Exception ex)
        {
            if (!(ex is TargetInvocationException) || !(ex.InnerException is KeyNotFoundException))
            {
                _starlightFailed = true;
                MelonLogger.Warning("[Cotton] Starlight pedia bridge disabled: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }
    }
}
