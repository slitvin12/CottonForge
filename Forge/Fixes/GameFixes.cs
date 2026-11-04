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
    /// Read-only save diagnostics: a save that references a slime type which is no longer registered (mod removed, a largo
    /// whose saving failed) breaks loading of the radiant-slime block. Such entries are logged (the save is not modified).
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
            try
            {
                if (persistence.RadiantsCollected != null)
                    foreach (var kv in persistence.RadiantsCollected) if (!Known(lookups, kv.Key)) unknown.Add("collected:" + Name(lookups, kv.Key));
                if (persistence.RadiantShuffleBags != null)
                    foreach (var kv in persistence.RadiantShuffleBags) if (!Known(lookups, kv.Key)) unknown.Add("bag:" + Name(lookups, kv.Key));
                if (persistence.LostRadiantIdQueue != null)
                    foreach (var id in persistence.LostRadiantIdQueue) if (!Known(lookups, id)) unknown.Add("lost:" + Name(lookups, id));
            }
            catch (Exception e) { ForgeLog.Warn("[save] radiant diag: " + e.Message); }
            if (unknown.Count > 0)
                ForgeLog.Warn($"[save] radiant-slime data references {unknown.Count} unregistered slime type(s): {string.Join(", ", unknown)}");
        }
    }
}
