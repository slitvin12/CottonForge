using System;
using System.Collections.Generic;
using CottonLibrary.Forge;
using UnityEngine;
using UnityEngine.Localization;

namespace CottonLibrary.Patches;

/// <summary>
/// Keeps modded <see cref="LocalizedString"/>s in sync with the selected language.
/// <para>
/// Fork: Cotton 0.3.1 never reset its timer (<c>timer += Time.unscaledTime</c>), so after the first second it reloaded the
/// language CSV and re-created every modded string every frame, and it enumerated the live dictionary, so a string created
/// later threw "Collection was modified" and stopped the loop for the session. Now: a snapshot is re-applied at once on a
/// language change and every <c>LocalizationInterval</c> seconds otherwise, 32 strings per frame.
/// </para>
/// </summary>
internal static class LocalizationRepair
{
    private static bool running, reload;
    private static string code;
    private static float next;
    private static readonly Queue<(LocalizedString str, string key, Library.ModdedLocalizedText text)> queue = new();

    /// <summary>Strings re-applied since start (dev console).</summary>
    internal static int Applied;

    /// <summary>Current language code ("en", "ru", ...) or null.</summary>
    internal static string Code => code;

    /// <summary>Reload the language on the next tick (new CSV added, defaults changed).</summary>
    internal static void RequestReload() => reload = true;

    /// <summary>Re-check the language right now.</summary>
    internal static void CheckNow() { reload = true; next = 0f; }

    public static void StartLoop()
    {
        if (running) return;
        running = true;
        Scheduler.Every(0f, Tick, "Cotton localization loop");
    }

    private static string CurrentCode()
    {
        try
        {
            var dir = Library.systemContext != null ? Library.systemContext.LocalizationDirector : null;
            if (dir == null) return null;
            int i = dir.GetCurrentLocaleIndex();
            var items = dir.Locales._items;
            if (i < 0 || items == null || i >= items.Length || items[i] == null) return null;
            return items[i].Formatter.Cast<Il2CppSystem.Globalization.CultureInfo>()._name;
        }
        catch { return null; }
    }

    private static bool Tick()
    {
        if (queue.Count > 0)
        {
            for (int n = 0; n < 32 && queue.Count > 0; n++)
            {
                var it = queue.Dequeue();
                try
                {
                    var fresh = Library.CreateStaticString(Library.LoadLocalizedText(it.key, it.text.parameters ?? Array.Empty<object>()), it.text.srKey, it.text.table);
                    it.str.m_TableEntryReference = fresh.TableEntryReference;
                    it.str.m_TableReference = fresh.TableReference;
                    Applied++;
                }
                catch (Exception e) { ForgeLog.WarnOnce("loc:" + it.key, $"[loc] {it.key}: {e.Message}"); }
            }
            return true;
        }
        float now = Time.unscaledTime;
        if (!reload && now < next) return true;
        string c = CurrentCode();
        if (c == null) { next = now + 0.5f; return true; }
        if (c != code || reload)
        {
            try { Library.LoadLanguage(c); }
            catch (Exception e) { ForgeLog.WarnOnce("loadlang", "[loc] LoadLanguage failed: " + e.Message); }
            if (c != code) ForgeLog.Debug("[loc] language " + c);
            code = c;
            reload = false;
        }
        next = now + Mathf.Max(0.25f, ForgeConfig.LocalizationInterval != null ? ForgeConfig.LocalizationInterval.Value : 2f);
        // copy first: mods may add strings while the queue is processed
        foreach (var kv in new List<KeyValuePair<string, Library.ModdedLocalizedText>>(Library.moddedLocalizedStrings))
            if (kv.Value.str != null) queue.Enqueue((kv.Value.str, kv.Key, kv.Value));
        return true;
    }
}
