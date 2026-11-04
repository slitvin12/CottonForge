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
    public static void MakeSellable(IdentifiableType ident, float marketValue, float marketSaturation, bool hideInMarket = false)
    {
        if (marketData.ContainsKey(ident))
        {
            MelonLogger.Error("Failed to make Object sellable: The Object is already sellable");
            return;
        }
        if (removeMarketPlortEntries.Contains(ident))
        {
            removeMarketPlortEntries.Remove(ident);
        }
        marketPlortEntries.Add(new PlortEntry
        {
            IdentType = ident
        }, hideInMarket);
        marketData.Add(ident, new ModdedMarketData(marketSaturation, marketValue));
        INTERNAL_RefreshMarketData();
    }

    public static bool IsSellable(IdentifiableType ident)
    {
        bool result = false;
        System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>
        {
            "PinkPlort", "CottonPlort", "PhosphorPlort", "TabbyPlort", "AnglerPlort", "RockPlort", "HoneyPlort", "BoomPlort", "PuddlePlort", "FirePlort",
            "BattyPlort", "CrystalPlort", "HunterPlort", "FlutterPlort", "RingtailPlort", "SaberPlort", "YolkyPlort", "TanglePlort", "DervishPlort", "TwinPlort",
            "SloomberPlort", "ShadowPlort", "PrismaPlort", "GoldPlort"
        };
        if (removeMarketPlortEntries.Count != 0)
        {
            foreach (string item in list)
            {
                if (!(item == ident.name))
                {
                    continue;
                }
                result = true;
                foreach (IdentifiableType removeMarketPlortEntry in removeMarketPlortEntries)
                {
                    if (ident == removeMarketPlortEntry)
                    {
                        result = false;
                    }
                }
            }
        }
        if (marketData.ContainsKey(ident))
        {
            return true;
        }
        foreach (System.Collections.Generic.KeyValuePair<PlortEntry, bool> marketPlortEntry in marketPlortEntries)
        {
            if (marketPlortEntry.Key.IdentType == ident)
            {
                return true;
            }
        }
        return result;
    }

    public static void MakeNOTSellable(IdentifiableType ident)
    {
        removeMarketPlortEntries.Add(ident);
        foreach (System.Collections.Generic.KeyValuePair<PlortEntry, bool> marketPlortEntry in marketPlortEntries)
        {
            PlortEntry key = marketPlortEntry.Key;
            if (key.IdentType == ident)
            {
                marketPlortEntries.Remove(key);
                break;
            }
        }
        if (marketData.ContainsKey(ident))
        {
            marketData.Remove(ident);
        }
    }

    internal static void INTERNAL_RefreshMarketData(PlortEconomySettings settings = null)
    {
        try
        {
            if (settings == null)
            {
                settings = Get<PlortEconomySettings>("PlortEconomy");
            }
            if (settings == null)
            {
                return;
            }
            System.Collections.Generic.List<PlortValueConfiguration> list = new System.Collections.Generic.List<PlortValueConfiguration>();
            foreach (PlortValueConfiguration plort in settings.PlortsTable.Plorts)
            {
                if (plort == null || plort.Type == null)
                {
                    continue;
                }
                bool flag = false;
                foreach (System.Collections.Generic.KeyValuePair<IdentifiableType, ModdedMarketData> marketDatum in marketData)
                {
                    if (marketDatum.Key != null && plort.Type.ReferenceId == marketDatum.Key.ReferenceId)
                    {
                        flag = true;
                        break;
                    }
                }
                if (!flag)
                {
                    list.Add(plort);
                }
            }
            foreach (System.Collections.Generic.KeyValuePair<IdentifiableType, ModdedMarketData> marketDatum2 in marketData)
            {
                if (!(marketDatum2.Key == null))
                {
                    list.Add(new PlortValueConfiguration
                    {
                        FullSaturation = marketDatum2.Value.saturation,
                        Type = marketDatum2.Key,
                        InitialValue = marketDatum2.Value.value
                    });
                }
            }
            settings.PlortsTable = new PlortValueConfigurationTable
            {
                Plorts = new Il2CppReferenceArray<PlortValueConfiguration>(list.ToArray())
            };
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Cotton] Failed to refresh market data: " + ex.Message);
        }
    }
}
