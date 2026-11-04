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
    public enum PediaDetailType
    {
        Slimeology,
        Plort,
        Risk,
        About,
        HowToUse
    }

    public enum PediaCategoryType
    {
        Slimes,
        Tutorial,
        Science,
        World,
        Ranch,
        Toys,
        Resources,
        Blueprints,
        Weather
    }

    public struct PediaDetail
    {
        public int index;

        public LocalizedString text;

        public PediaDetailType type;

        public static PediaDetail Create(int index, LocalizedString text, PediaDetailType type)
        {
            return new PediaDetail
            {
                index = index,
                text = text,
                type = type
            };
        }

        public static PediaDetail[] Params(params PediaDetail[] array)
        {
            return array;
        }
    }

    private static System.Collections.Generic.Dictionary<PediaDetailType, PediaDetailSection> pediaDetails = new System.Collections.Generic.Dictionary<PediaDetailType, PediaDetailSection>();

    internal static System.Collections.Generic.Dictionary<PediaCategoryType, System.Collections.Generic.List<PediaEntry>> pediaEntries = new System.Collections.Generic.Dictionary<PediaCategoryType, System.Collections.Generic.List<PediaEntry>>
    {
        {
            PediaCategoryType.Slimes,
            new System.Collections.Generic.List<PediaEntry>()
        },
        {
            PediaCategoryType.Resources,
            new System.Collections.Generic.List<PediaEntry>()
        },
        {
            PediaCategoryType.Ranch,
            new System.Collections.Generic.List<PediaEntry>()
        },
        {
            PediaCategoryType.Weather,
            new System.Collections.Generic.List<PediaEntry>()
        },
        {
            PediaCategoryType.Toys,
            new System.Collections.Generic.List<PediaEntry>()
        },
        {
            PediaCategoryType.Blueprints,
            new System.Collections.Generic.List<PediaEntry>()
        },
        {
            PediaCategoryType.Science,
            new System.Collections.Generic.List<PediaEntry>()
        },
        {
            PediaCategoryType.Tutorial,
            new System.Collections.Generic.List<PediaEntry>()
        },
        {
            PediaCategoryType.World,
            new System.Collections.Generic.List<PediaEntry>()
        }
    };

    public static IdentifiablePediaEntry CreatePediaEntryForSlime(SlimeDefinition slime, string persistID, LocalizedString intro, LocalizedString slimeology, LocalizedString risks, LocalizedString plort)
    {
        return CreateIdentPediaEntry(slime, persistID, Get<IdentifiablePediaEntry>("Pink"), intro, PediaCategoryType.Slimes, PediaDetail.Params(PediaDetail.Create(0, slimeology, PediaDetailType.Slimeology), PediaDetail.Create(1, risks, PediaDetailType.Risk), PediaDetail.Create(2, plort, PediaDetailType.Plort)));
    }

    internal static void PediaDetailInitialize()
    {
        pediaDetails.Add(PediaDetailType.HowToUse, Get<PediaDetailSection>("How To Use"));
        pediaDetails.Add(PediaDetailType.About, Get<PediaDetailSection>("About"));
        pediaDetails.Add(PediaDetailType.Plort, Get<PediaDetailSection>("Plortonomics"));
        pediaDetails.Add(PediaDetailType.Risk, Get<PediaDetailSection>("Rancher Risks"));
        pediaDetails.Add(PediaDetailType.Slimeology, Get<PediaDetailSection>("Slimeology"));
    }

    public static IdentifiablePediaEntry CreateIdentPediaEntry(IdentifiableType ident, string persistID, IdentifiablePediaEntry baseEntry, LocalizedString intro, PediaCategoryType category, PediaDetail[] details)
    {
        IdentifiablePediaEntry identifiablePediaEntry = UnityEngine.Object.Instantiate(baseEntry);
        identifiablePediaEntry._title = ident.localizedName;
        identifiablePediaEntry._identifiableType = ident;
        identifiablePediaEntry._description = intro;
        int count = identifiablePediaEntry._details.Count;
        LocalizedString localizedString = null;
        for (int i = 0; i < details.Length; i++)
        {
            PediaDetail pediaDetail = details[i];
            if (pediaDetail.index < count)
            {
                localizedString = identifiablePediaEntry._details[pediaDetail.index].TextGamepad;
                identifiablePediaEntry._details[pediaDetail.index].Text = pediaDetail.text;
                identifiablePediaEntry._details[pediaDetail.index].Section = pediaDetails[pediaDetail.type];
                continue;
            }
            new PediaEntryDetail
            {
                Contents = new Il2CppReferenceArray<PediaEntryDetailSubContent>(0L),
                Section = pediaDetails[pediaDetail.type],
                Text = pediaDetail.text,
                TextGamepad = localizedString,
                TextPS4 = localizedString
            };
        }
        pediaEntries[category].Add(identifiablePediaEntry);
        identifiablePediaEntry.name = persistID;
        identifiablePediaEntry._identifiableType._pediaPersistenceSuffix = persistID;
        return identifiablePediaEntry;
    }

    public static FixedPediaEntry CreateFixedPediaEntry(FixedPediaEntry baseEntry, string persistID, LocalizedString title, LocalizedString intro, Sprite icon, PediaCategoryType category, PediaDetail[] details)
    {
        FixedPediaEntry fixedPediaEntry = UnityEngine.Object.Instantiate(baseEntry);
        fixedPediaEntry._title = title;
        fixedPediaEntry._description = intro;
        fixedPediaEntry._icon = icon;
        int count = fixedPediaEntry._details.Count;
        LocalizedString localizedString = null;
        for (int i = 0; i < details.Length; i++)
        {
            PediaDetail pediaDetail = details[i];
            if (pediaDetail.index < count)
            {
                localizedString = fixedPediaEntry._details[pediaDetail.index].TextGamepad;
                fixedPediaEntry._details[pediaDetail.index].Text = pediaDetail.text;
                fixedPediaEntry._details[pediaDetail.index].Section = pediaDetails[pediaDetail.type];
                continue;
            }
            new PediaEntryDetail
            {
                Contents = new Il2CppReferenceArray<PediaEntryDetailSubContent>(0L),
                Section = pediaDetails[pediaDetail.type],
                Text = pediaDetail.text,
                TextGamepad = localizedString,
                TextPS4 = localizedString
            };
        }
        pediaEntries[category].Add(fixedPediaEntry);
        fixedPediaEntry.name = persistID;
        fixedPediaEntry._persistenceSuffix = persistID;
        return fixedPediaEntry;
    }
}
