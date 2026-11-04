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
    internal static void INTERNAL_SetupSaveForIdent(string RefID, IdentifiableType ident)
    {
        if (ident == null || string.IsNullOrWhiteSpace(RefID))
        {
            return;
        }
        try
        {
            AutoSaveDirector autoSaveDirector = gameContext?.AutoSaveDirector;
            SaveReferenceTranslation saveReferenceTranslation = ((autoSaveDirector != null) ? autoSaveDirector._saveReferenceTranslation : null);
            if (saveReferenceTranslation != null)
            {
                saveReferenceTranslation._identifiableTypeLookup.TryAdd(RefID, ident);
                PersistenceIdLookupTable<IdentifiableType> identifiableTypeToPersistenceId = saveReferenceTranslation._identifiableTypeToPersistenceId;
                if (identifiableTypeToPersistenceId != null)
                {
                    if (identifiableTypeToPersistenceId._primaryIndex != null && identifiableTypeToPersistenceId._primaryIndex.Count > 0 && !identifiableTypeToPersistenceId._primaryIndex.Contains(RefID))
                    {
                        identifiableTypeToPersistenceId._primaryIndex = identifiableTypeToPersistenceId._primaryIndex.AddString(RefID);
                    }
                    if (identifiableTypeToPersistenceId._reverseIndex != null)
                    {
                        identifiableTypeToPersistenceId._reverseIndex.TryAdd(RefID, identifiableTypeToPersistenceId._reverseIndex.Count);
                    }
                }
            }
            if (SlimeDefinition.IsSlimeDefinition(ident) && gameContext?.SlimeDefinitions != null)
            {
                SlimeDefinition slimeDefinition = ident.Cast<SlimeDefinition>();
                gameContext.SlimeDefinitions._slimeDefinitionsByIdentifiable.TryAdd(ident, slimeDefinition);
                // fork: compared by pointer (Contains compared wrappers, so every call added the slime again)
                bool forkListed = false;
                var forkSlimes = gameContext.SlimeDefinitions.Slimes;
                if (forkSlimes != null) for (int fi = 0; fi < forkSlimes.Length; fi++) if (forkSlimes[fi] != null && forkSlimes[fi].Pointer == slimeDefinition.Pointer) { forkListed = true; break; }
                if (!forkListed)
                {
                    gameContext.SlimeDefinitions.Slimes = gameContext.SlimeDefinitions.Slimes.Add(slimeDefinition);
                }
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Cotton] Failed to set up saving for " + RefID + ": " + ex.Message);
        }
        ident.referenceId = RefID;
    }

    internal static void INTERNAL_SetupLoadForIdent(string RefID, IdentifiableType ident)
    {
        if (ident == null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(RefID))
        {
            RefID = ident.ReferenceId;
        }
        ident.SetupForSaving(RefID);
        try
        {
            AutoSaveDirector autoSaveDirector = gameContext?.AutoSaveDirector;
            IdentifiableTypeGroup identifiableTypeGroup = ((autoSaveDirector != null && autoSaveDirector._configuration != null) ? autoSaveDirector._configuration._identifiableTypes : null);
            if (identifiableTypeGroup != null && !identifiableTypeGroup.IsMember(ident))
            {
                gameContext.LookupDirector.AddIdentifiableTypeToGroup(ident, identifiableTypeGroup);
            }
            gameContext?.LookupDirector?._identifiableTypeByRefId.TryAdd(RefID, ident);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Cotton] Failed to register " + RefID + " in lookup: " + ex.Message);
        }
        INTERNAL_SetupSaveForIdent(RefID, ident);
    }

    internal static void INTERNAL_RefreshAllSavedIdents()
    {
        System.Collections.Generic.KeyValuePair<string, IdentifiableType>[] array = savedIdents.ToArray();
        for (int i = 0; i < array.Length; i++)
        {
            System.Collections.Generic.KeyValuePair<string, IdentifiableType> keyValuePair = array[i];
            INTERNAL_SetupLoadForIdent(keyValuePair.Key, keyValuePair.Value);
        }
    }
}
