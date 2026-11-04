using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.UI;
using MelonLoader;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(MarketUI), "Start")]
public static class MarketCapacityPatch
{
    private static bool logged;

    [HarmonyPrefix]
    [HarmonyPriority(int.MinValue)]
    public static void Prefix(MarketUI __instance)
    {
        try
        {
            MarketUIConfiguration config = __instance._config;
            if (config == null || config._plorts == null || __instance.pricesPanels == null)
            {
                return;
            }
            int num = 0;
            foreach (MarketUI.PricesPanelEntry pricesPanel in __instance.pricesPanels)
            {
                if (pricesPanel != null)
                {
                    num += pricesPanel.entryCount;
                }
            }
            int length = config._plorts.Length;
            List<PlortEntry> list = new List<PlortEntry>();
            HashSet<string> hashSet = new HashSet<string>();
            foreach (PlortEntry plort in config._plorts)
            {
                if (plort != null && plort.IdentType != null)
                {
                    string text = plort.IdentType.ReferenceId ?? plort.IdentType.name;
                    if (!string.IsNullOrEmpty(text) && !hashSet.Add(text))
                    {
                        continue;
                    }
                }
                list.Add(plort);
            }
            string text2 = "";
            if (num > 0 && list.Count > num)
            {
                text2 = string.Join(", ", from x in list.Skip(num)
                    select (x != null && !(x.IdentType == null)) ? x.IdentType.name : "<empty>");
                list = list.Take(num).ToList();
            }
            if (list.Count != length)
            {
                config._plorts = new Il2CppReferenceArray<PlortEntry>(list.ToArray());
            }
            if (!logged)
            {
                logged = true;
                MelonLogger.Msg($"[Cotton] Market board: {length} entries, {num} slots, showing {list.Count}" + ((text2.Length > 0) ? (" (no room for: " + text2 + ")") : ""));
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Cotton] Market capacity check failed: " + ex.Message);
        }
    }
}
