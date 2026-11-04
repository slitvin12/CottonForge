using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.UI;
using MelonLoader;

namespace CottonLibrary.Patches;

[HarmonyPatch(typeof(MarketUI))]
public static class MarketPatch
{
    private const int MaxEntries = 34;

    [HarmonyPatch("Start")]
    [HarmonyPrefix]
    public static void Prefix(MarketUI __instance)
    {
        try
        {
            MarketUIConfiguration config = __instance._config;
            if (config == null || config._plorts == null || (Library.marketPlortEntries.Count == 0 && Library.removeMarketPlortEntries.Count == 0))
            {
                return;
            }
            List<PlortEntry> list = new List<PlortEntry>();
            bool flag = false;
            foreach (PlortEntry plort in config._plorts)
            {
                if (plort != null && plort.IdentType != null)
                {
                    string refId = plort.IdentType.ReferenceId;
                    if (Library.removeMarketPlortEntries.Exists((IdentifiableType y) => y != null && y.ReferenceId == refId))
                    {
                        flag = true;
                        continue;
                    }
                }
                list.Add(plort);
            }
            foreach (KeyValuePair<PlortEntry, bool> marketPlortEntry in Library.marketPlortEntries)
            {
                if (!marketPlortEntry.Value && !(marketPlortEntry.Key.IdentType == null))
                {
                    string refId2 = marketPlortEntry.Key.IdentType.ReferenceId;
                    if (!list.Exists((PlortEntry z) => z != null && z.IdentType != null && z.IdentType.ReferenceId == refId2))
                    {
                        list.Add(marketPlortEntry.Key);
                        flag = true;
                    }
                }
            }
            if (flag)
            {
                config._plorts = new Il2CppReferenceArray<PlortEntry>(list.Take(34).ToArray());
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Cotton] MarketUI patch failed: " + ex.Message);
        }
    }
}
