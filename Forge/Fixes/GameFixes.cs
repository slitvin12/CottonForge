using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CottonLibrary;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Persist;
using MelonLoader;
using UnityEngine;
using UnityEngine.Localization;

namespace CottonLibrary.Forge
{
    // Repairs that hook game methods (fixes of Cotton's own code are made directly in its sources).

    /// <summary>Cotton puts modded slimes into SlimeDefinitions.Slimes several times ("same key" errors on every RefreshIndexes).</summary>
    [HarmonyPatch(typeof(SlimeDefinitions), nameof(SlimeDefinitions.RefreshIndexes))]
    static class SlimeIndexDedup
    {
        internal static int Removed;
        static bool _failed;

        static void Prefix(SlimeDefinitions __instance)
        {
            if (_failed || !ForgeConfig.FixDuplicateSlimes.Value) return;
            try
            {
                var arr = __instance.Slimes;
                if (arr == null || arr.Length < 2) return;
                var seen = new HashSet<IntPtr>();
                List<SlimeDefinition> keep = null;
                for (int i = 0; i < arr.Length; i++)
                {
                    var d = arr[i];
                    if (d == null || seen.Add(d.Pointer)) { keep?.Add(d); continue; }
                    if (keep == null) { keep = new List<SlimeDefinition>(arr.Length); for (int j = 0; j < i; j++) keep.Add(arr[j]); }
                }
                if (keep == null) return;
                if (Removed == 0) ForgeLog.Msg($"[fix] dropped {arr.Length - keep.Count} duplicate SlimeDefinitions.Slimes entries (Cotton double registration); further cases are silent");
                Removed += arr.Length - keep.Count;
                __instance.Slimes = Gc.Array(keep);
            }
            catch (Exception e) { _failed = true; ForgeLog.Warn("[fix] slime dedup failed, switched off: " + e.Message); }
        }
    }

    /// <summary>
    /// Content generation window: AutoSaveDirector.Awake runs every Cotton mod's SaveDirectorLoaded and the whole largo
    /// generation in one frame. GC paused around it (incremental GC + Il2CppInterop raw array stores = random coreclr crashes),
    /// largos repaired afterwards, then <c>AutoLargosLoaded()</c> (declared by Cotton, never called) for every Cotton mod.
    /// </summary>
    [HarmonyPatch(typeof(AutoSaveDirector), "Awake")]
    static class ContentGenerationWindow
    {
        static bool _paused;

        [HarmonyPriority(Priority.First)]
        static void Prefix()
        {
            if (_paused) return;
            _paused = true;
            Gc.Pause("content generation (SaveDirectorLoaded + largos)");
            // Awake (prefix, original, postfixes) is synchronous: the next update is after generation, even if it throws.
            Scheduler.Frames(1, () => Gc.Resume("content generation finished"), "gc resume");
        }

        [HarmonyPriority(Priority.Last)]
        static void Postfix()
        {
            Safe.Run("largo repair after generation", () => Largos.Process("generation"));
            if (!ForgeConfig.CallAutoLargosLoaded.Value) return;
            foreach (var m in MelonBase.RegisteredMelons)
                if (m is CottonMod cm)
                    Safe.Run(cm.Info.Name + ".AutoLargosLoaded", cm.AutoLargosLoaded);
        }
    }

    /// <summary>First save load: re-check largos (some mods generate them late) and refresh largo diets when foods were added.</summary>
    [HarmonyPatch(typeof(GameModelPushHelpers), "PushGame")]
    static class FirstGameLoad
    {
        static bool _done;

        [HarmonyPriority(Priority.First)]
        static void Postfix()
        {
            if (_done) return;
            _done = true;
            Gc.Window("first game load", () =>
            {
                Safe.Run("largo repair on first load", () => Largos.Process("first game load"));
                if (Foods.Enabled.Count > 0)
                {
                    int n = Idents.RefreshLargoDiets();
                    ForgeLog.Msg($"Refreshed {n} largo diets (new foods)");
                }
            });
        }
    }

    /// <summary>
    /// Save sanitization and diagnostics: removes unregistered slime IDs from RadiantSlimesV03 (collected, shuffle bags,
    /// and lost queue) before RadiantSlimesModel.Push runs so removed mods or disabled slimes never break save loads.
    /// </summary>
    [HarmonyPatch(typeof(RadiantSlimesModel), nameof(RadiantSlimesModel.Push))]
    static class RadiantPushDiag
    {
        static bool Known(ILoadReferenceTranslation lookups, int id) { try { return lookups.GetIdentifiableType(id) != null; } catch { return false; } }
        static string Name(ILoadReferenceTranslation lookups, int id) { try { return lookups.GetIdentifiableReferenceId(id) ?? ("#" + id); } catch { return "#" + id; } }

        static void Prefix(RadiantSlimesV03 persistence, ILoadReferenceTranslation lookups)
        {
            if (persistence == null || lookups == null) return;
            var unknown = new List<string>();
            var badCollected = new List<int>();
            var badBags = new List<int>();
            try
            {
                if (persistence.RadiantsCollected != null)
                {
                    foreach (var kv in persistence.RadiantsCollected)
                        if (!Known(lookups, kv.Key)) { badCollected.Add(kv.Key); unknown.Add("collected:" + Name(lookups, kv.Key)); }
                    foreach (var k in badCollected) persistence.RadiantsCollected.Remove(k);
                }
                if (persistence.RadiantShuffleBags != null)
                {
                    foreach (var kv in persistence.RadiantShuffleBags)
                        if (!Known(lookups, kv.Key)) { badBags.Add(kv.Key); unknown.Add("bag:" + Name(lookups, kv.Key)); }
                    foreach (var k in badBags) persistence.RadiantShuffleBags.Remove(k);
                }
                var lostIds = persistence.LostRadiantIdQueue;
                var lostTimes = persistence.LostRadiantTimeQueue;
                if (lostIds != null)
                {
                    for (int i = lostIds.Count - 1; i >= 0; i--)
                    {
                        int id = lostIds[i];
                        if (Known(lookups, id)) continue;
                        unknown.Add("lost:" + Name(lookups, id));
                        lostIds.RemoveAt(i);
                        if (lostTimes != null && i < lostTimes.Count) lostTimes.RemoveAt(i);
                    }
                }
            }
            catch (Exception e) { ForgeLog.Warn("[save] radiant diag: " + e.Message); }
            if (unknown.Count > 0)
                ForgeLog.Warn($"[save] radiant-slime data pruned {unknown.Count} unregistered slime type(s): {string.Join(", ", unknown)}");
        }
    }

    /// <summary>
    /// Prevents NullReferenceException inside SlimeRadiant.SetRadiantAppearance when a saved radiant slime/largo
    /// references a parent or variant whose radiant appearance does not exist or was disabled in config.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppMonomiPark.SlimeRancher.Slime.SlimeRadiant), nameof(Il2CppMonomiPark.SlimeRancher.Slime.SlimeRadiant.SetRadiantAppearance))]
    static class SlimeRadiantAppearanceGuard
    {
        static bool Prefix(Il2CppMonomiPark.SlimeRancher.Slime.SlimeRadiant __instance, ref SlimeDefinition radiantBaseSlimeDef)
        {
            try
            {
                if (__instance == null) return true;
                var def = __instance.GetComponent<Identifiable>()?.identType?.TryCast<SlimeDefinition>();
                if (def == null) return true;
                if (!def.IsLargo)
                {
                    if (def.RadiantBase != null) return true;
                    if (__instance._slimeModel != null) __instance._slimeModel.RadiantBaseType = null;
                    return false;
                }
                if (radiantBaseSlimeDef != null)
                {
                    SlimeAppearance cur = null;
                    try { cur = def.GetRadiantAppearance(radiantBaseSlimeDef); } catch { }
                    if (cur != null) return true;
                }
                var bases = def.BaseSlimes;
                if (bases != null)
                {
                    for (int i = 0; i < bases.Length; i++)
                    {
                        var bs = bases[i];
                        if (bs == null) continue;
                        SlimeAppearance alt = null;
                        try { alt = def.GetRadiantAppearance(bs); } catch { }
                        if (alt != null)
                        {
                            radiantBaseSlimeDef = bs;
                            if (__instance._slimeModel != null) __instance._slimeModel.RadiantBaseType = bs;
                            return true;
                        }
                    }
                }
                if (__instance._slimeModel != null) __instance._slimeModel.RadiantBaseType = null;
                return false;
            }
            catch { return true; }
        }

        static Exception Finalizer(Il2CppMonomiPark.SlimeRancher.Slime.SlimeRadiant __instance, Exception __exception)
        {
            if (__exception == null) return null;
            try
            {
                ForgeLog.WarnOnce("radapp:" + (__instance != null ? __instance.name : "?"), $"[save] SetRadiantAppearance recovered from {__exception.GetType().Name}: {__exception.Message}");
                if (__instance != null && __instance._slimeModel != null) __instance._slimeModel.RadiantBaseType = null;
            }
            catch { }
            return null;
        }
    }

    /// <summary>Keeps modded PediaEntry persistence IDs in SaveReferenceTranslation when vanilla rebuilds the list or pushes saves.</summary>
    [HarmonyPatch(typeof(SaveReferenceTranslation), nameof(SaveReferenceTranslation.OnPediaEntryListChanged))]
    static class PediaLookupRefreshPatch
    {
        static void Postfix(SaveReferenceTranslation __instance) => Library.INTERNAL_RefreshAllPediaEntries(__instance);
    }

    [HarmonyPatch(typeof(GameModelPushHelpers), nameof(GameModelPushHelpers.PushPedia))]
    static class PushPediaSyncPatch
    {
        static void Prefix() => Library.INTERNAL_RefreshAllPediaEntries(null);
    }
}
