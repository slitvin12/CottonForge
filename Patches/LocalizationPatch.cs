using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.Localization;
using Il2CppSystem.Collections.Generic;
using MelonLoader;
using UnityEngine.Localization.Tables;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(LocalizationDirector), "LoadTables")]
public static class LocalizationDirectorLoadTablePatch
{
    public static void Postfix(LocalizationDirector __instance)
    {
        MelonCoroutines.Start(LoadTable(__instance));
    }

    private static IEnumerator LoadTable(LocalizationDirector director)
    {
        yield return null;
        yield return null;
        Il2CppSystem.Collections.Generic.Dictionary<string, StringTable>.Enumerator enumerator = director.Tables.GetEnumerator();
        while (enumerator.MoveNext())
        {
            Il2CppSystem.Collections.Generic.KeyValuePair<string, StringTable> current = enumerator.Current;
            if (Library.addedTranslations.TryGetValue(current.Key, out var value))
            {
                foreach (System.Collections.Generic.KeyValuePair<string, string> item in value)
                {
                    current.Value.AddEntry(item.Key, item.Value);
                }
            }
            value = null;
        }
    }
}
