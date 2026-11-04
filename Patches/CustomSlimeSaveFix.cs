using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.DataModel;

namespace CottonLibrary.Patches;

[HarmonyPatch]
public static class EnsureSlimesSavableFix
{
    private static MethodBase TargetMethod()
    {
        return (from t in typeof(GameModel).Assembly.GetTypes()
            where t.Namespace == "Il2CppMonomiPark.SlimeRancher.Persist" && Regex.IsMatch(t.Name, "^GameV\\d+$") && t.GetMethod("LoadSummaryData", BindingFlags.Instance | BindingFlags.Public) != null
            orderby int.Parse(t.Name.Substring(5)) descending
            select t).FirstOrDefault()?.GetMethod("LoadSummaryData", BindingFlags.Instance | BindingFlags.Public);
    }

    private static bool Prepare()
    {
        return TargetMethod() != null;
    }

    private static void Prefix()
    {
        try
        {
            KeyValuePair<string, IdentifiableType>[] array = Library.savedIdents.ToArray();
            for (int i = 0; i < array.Length; i++)
            {
                KeyValuePair<string, IdentifiableType> keyValuePair = array[i];
                Library.INTERNAL_SetupSaveForIdent(keyValuePair.Key, keyValuePair.Value);
            }
        }
        catch
        {
        }
    }
}
