using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Persist;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Mod data stored next to each save game (<c>UserData\CottonLibrary\saves\&lt;game&gt;.json</c>) without touching
    /// SR2's save format: virtual ammo slots, market events, extractor progress, your own counters. Keys are free text
    /// (use a prefix: <c>"MyMod.key"</c>). <see cref="Loaded"/> fires after a save is loaded, <see cref="Saving"/> right
    /// before the file is written (when the game saves).
    /// </summary>
    public static class ForgeSave
    {
        static Dictionary<string, string> _data = new();
        static string _game;

        /// <summary>Name of the loaded save game (null in the main menu).</summary>
        public static string Game => _game;
        /// <summary>After a save game was loaded (data available).</summary>
        public static event Action Loaded;
        /// <summary>Before the data is written: put your state in now.</summary>
        public static event Action Saving;

        public static bool Has(string key) => key != null && _data.ContainsKey(key);
        public static string Get(string key, string def = null) => key != null && _data.TryGetValue(key, out var v) ? v : def;
        public static void Set(string key, string value) { if (key == null) return; if (value == null) _data.Remove(key); else _data[key] = value; }
        public static void Remove(string key) { if (key != null) _data.Remove(key); }

        public static float GetFloat(string key, float def = 0f) =>
            float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;
        public static void SetFloat(string key, float v) => Set(key, v.ToString("R", CultureInfo.InvariantCulture));
        public static int GetInt(string key, int def = 0) =>
            int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : def;
        public static void SetInt(string key, int v) => Set(key, v.ToString(CultureInfo.InvariantCulture));
        public static bool GetBool(string key, bool def = false) => Get(key) is string s ? s == "1" || s == "true" : def;
        public static void SetBool(string key, bool v) => Set(key, v ? "1" : "0");

        /// <summary>Keys starting with <paramref name="prefix"/>.</summary>
        public static List<string> Keys(string prefix = "")
        {
            var l = new List<string>();
            foreach (var k in _data.Keys) if (k.StartsWith(prefix ?? "", StringComparison.Ordinal)) l.Add(k);
            return l;
        }

        static string FileOf(string game)
        {
            var safe = game;
            foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            return Path.Combine(DevConsole.Dir, "saves", safe + ".json");
        }

        internal static void OnLoaded(string game)
        {
            _game = string.IsNullOrEmpty(game) ? "default" : game;
            _data = new Dictionary<string, string>();
            try
            {
                var f = FileOf(_game);
                if (File.Exists(f)) _data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f)) ?? new();
            }
            catch (Exception e) { ForgeLog.Warn($"ForgeSave: cannot read data of '{_game}': {e.Message}"); }
            foreach (Action a in Loaded?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { a(); } catch (Exception e) { ForgeLog.Warn($"ForgeSave.Loaded handler {a.Method.Name}: {e}"); }
        }

        internal static void OnSaving()
        {
            if (_game == null) return;
            foreach (Action a in Saving?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { a(); } catch (Exception e) { ForgeLog.Warn($"ForgeSave.Saving handler {a.Method.Name}: {e}"); }
            Flush();
        }

        /// <summary>Writes the data now (normally done when the game saves).</summary>
        public static void Flush()
        {
            if (_game == null) return;
            try
            {
                var f = FileOf(_game);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                var tmp = f + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
                File.Copy(tmp, f, true);
                File.Delete(tmp);
            }
            catch (Exception e) { ForgeLog.Warn($"ForgeSave: cannot write data of '{_game}': {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(GameModelPushHelpers), "PushGame")]
    static class ForgeSaveLoadPatch
    {
        static void Postfix(GameV11 gameState) => Safe.Run("ForgeSave load", () => ForgeSave.OnLoaded(gameState?.GameName));
    }

    [HarmonyPatch(typeof(GameModelPullHelpers), "PullGame")]
    static class ForgeSaveWritePatch
    {
        static void Postfix() => Safe.Run("ForgeSave write", ForgeSave.OnSaving);
    }
}
