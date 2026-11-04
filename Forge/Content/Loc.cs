using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CottonLibrary;
using HarmonyLib;
using Il2Cpp;
using UnityEngine.Localization;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Localization on top of Cotton's CSV system.
    /// <para>CSV (embedded resource, UTF-8, CRLF): header <c>key,en,ru,...</c>, one row per key; <c>#</c> starts a comment line;
    /// <c>\n</c> in a value is a line break; an empty cell falls back to English (Cotton itself would show an empty string).</para>
    /// <para>Key conventions used by <see cref="SlimeBuilder"/> (<c>key</c> = <see cref="SlimeBuilder.Key"/>):
    /// <c>l.key_slime</c>, <c>l.key_plort</c>, largos <c>l.a_b_largo</c> (either order, vanilla slimes by lower-case name, e.g.
    /// <c>l.rad_pink_largo</c>), pedia (table "Pedia") <c>m.intro.key_slime</c>, <c>m.slimeology.key_slime</c>,
    /// <c>m.risks.key_slime</c>, <c>m.plortonomics.key_slime</c>.</para>
    /// </summary>
    public static class Loc
    {
        static readonly Dictionary<Assembly, HashSet<string>> _keys = new();
        static readonly HashSet<string> _all = new(StringComparer.Ordinal);
        static readonly Dictionary<string, LocalizedString> _cache = new(StringComparer.Ordinal);

        /// <summary>
        /// Loads <c>&lt;csvName&gt;.csv</c> embedded in <paramref name="asm"/> into Cotton (what <c>Library.AddLanguages</c> does,
        /// but for any assembly, at any time). <see cref="ForgeMod{M}"/> calls this for its own assembly automatically.
        /// Returns the number of keys (0 when the file does not exist).
        /// </summary>
        public static int AddLanguages(Assembly asm, string csvName = "localized")
        {
            var text = ModAssets.Of(asm).Text(csvName + ".csv");
            if (text == null) return 0;
            var langs = new List<string>();
            var data = new Dictionary<string, Dictionary<string, string>>();
            var keys = KeySet(asm);
            bool header = true;
            foreach (var row in ParseCsv(text))
            {
                if (row.Count == 0) continue;
                if (header)
                {
                    header = false;
                    for (int i = 1; i < row.Count; i++) { langs.Add(row[i]); if (!data.ContainsKey(row[i])) data[row[i]] = new Dictionary<string, string>(); }
                    continue;
                }
                string key = row[0];
                if (string.IsNullOrEmpty(key)) continue;
                keys.Add(key); _all.Add(key);
                for (int i = 1; i < row.Count && i - 1 < langs.Count; i++)
                    if (row[i].Length > 0) data[langs[i - 1]][key] = row[i].Replace("\\n", "\n");
            }
            var f = AccessTools.Field(typeof(Library), "languages");
            if (f?.GetValue(null) is not Dictionary<string, List<Dictionary<string, string>>> all)
            {
                ForgeLog.Error("Cotton's language table not found: use Library.AddLanguages in OnInitializeMelon instead");
                return 0;
            }
            foreach (var kv in data)
            {
                if (!all.TryGetValue(kv.Key, out var list)) all[kv.Key] = list = new List<Dictionary<string, string>>();
                list.Add(kv.Value);
            }
            // Cotton caches the merged English table on the first LoadLanguage: drop it so these keys are included.
            Safe.Quiet(() => AccessTools.Field(typeof(Library), "defaultLang")?.SetValue(null, null));
            CottonLibrary.Patches.LocalizationRepair.RequestReload();
            ForgeLog.Debug($"[loc] {asm.GetName().Name}: {keys.Count} keys, languages {string.Join(",", langs)}");
            return keys.Count;
        }

        /// <summary>Only indexes the keys of a CSV (for mods that call <c>Library.AddLanguages</c> themselves).</summary>
        public static int IndexKeys(Assembly asm, string csvName = "localized")
        {
            var text = ModAssets.Of(asm).Text(csvName + ".csv");
            if (text == null) return 0;
            var keys = KeySet(asm);
            bool header = true;
            foreach (var row in ParseCsv(text))
            {
                if (row.Count == 0) continue;
                if (header) { header = false; continue; }
                if (!string.IsNullOrEmpty(row[0])) { keys.Add(row[0]); _all.Add(row[0]); }
            }
            return keys.Count;
        }

        static HashSet<string> KeySet(Assembly asm)
        {
            if (!_keys.TryGetValue(asm, out var s)) _keys[asm] = s = new HashSet<string>(StringComparer.Ordinal);
            return s;
        }

        /// <summary>True when any loaded Forge CSV has the key.</summary>
        public static bool Has(string key) => key != null && _all.Contains(key);
        /// <summary>True when the CSV of <paramref name="asm"/> has the key.</summary>
        public static bool Has(Assembly asm, string key) => key != null && _keys.TryGetValue(asm, out var s) && s.Contains(key);
        /// <summary>Keys of one mod's CSV.</summary>
        public static IEnumerable<string> Keys(Assembly asm) => _keys.TryGetValue(asm, out var s) ? s : (IEnumerable<string>)Array.Empty<string>();

        /// <summary>
        /// Cotton-managed localized string for a CSV key (re-applied on language change). Cached: one instance per key, so
        /// Cotton keeps refreshing the instance you hold (Cotton tracks only the first string created for a key).
        /// </summary>
        public static LocalizedString String(string key, string table = "Actor")
        {
            if (_cache.TryGetValue(table + "\u0001" + key, out var s) && s != null) return s;
            s = Library.CreateLocalizedString(key, table);
            _cache[table + "\u0001" + key] = s;
            return s;
        }

        /// <summary>Fixed text (not from the CSV), e.g. a generated name. Not re-applied on language change.</summary>
        public static LocalizedString Static(string text, string key, string table = "Actor") => Library.CreateStaticString(text, key, table);

        /// <summary>Text of a key in the current language, <c>$1</c>, <c>$2</c> ... replaced by <paramref name="args"/>.</summary>
        public static string Text(string key, params object[] args) => Library.LoadLocalizedText(key, args);

        /// <summary>Creates every <c>*_largo</c> string of the mod up-front (in SaveDirectorLoaded, before largo generation).</summary>
        internal static void PreRegisterLargos(Assembly asm)
        {
            foreach (var k in Keys(asm))
                if (k.EndsWith("_largo", StringComparison.Ordinal)) String(k);
        }

        /// <summary>
        /// Names a largo from <c>l.a_b_largo</c> / <c>l.b_a_largo</c> when such a key exists. <paramref name="keyOf"/> maps a base
        /// slime to its key part (Forge slimes: <see cref="SlimeBuilder.Key"/>, others: lower-case name).
        /// </summary>
        public static bool NameLargo(SlimeDefinition d, Func<SlimeDefinition, string> keyOf = null)
        {
            if (d == null || d.BaseSlimes == null || d.BaseSlimes.Length != 2 || d.BaseSlimes[0] == null || d.BaseSlimes[1] == null) return false;
            keyOf ??= SlimeRegistry.KeyOf;
            string a = keyOf(d.BaseSlimes[0]), b = keyOf(d.BaseSlimes[1]);
            foreach (var k in new[] { $"l.{a}_{b}_largo", $"l.{b}_{a}_largo" })
                if (_all.Contains(k)) { d.localizedName = String(k); return true; }
            return false;
        }

        /// <summary>RFC-4180-ish CSV: quoted fields with "" escapes and line breaks, whitespace around fields trimmed, # comment lines.</summary>
        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;
            if (text[0] == '\uFEFF') text = text.Substring(1);
            int i = 0, n = text.Length;
            while (i < n)
            {
                // start of a record: skip blank lines and comments
                int ls = i;
                while (ls < n && (text[ls] == ' ' || text[ls] == '\t')) ls++;
                if (ls < n && (text[ls] == '\r' || text[ls] == '\n')) { i = ls + 1; continue; }
                if (ls < n && text[ls] == '#') { while (ls < n && text[ls] != '\n') ls++; i = ls + 1; continue; }
                var row = new List<string>();
                var sb = new StringBuilder();
                bool endOfRecord = false;
                while (!endOfRecord)
                {
                    sb.Clear();
                    while (i < n && (text[i] == ' ' || text[i] == '\t')) i++;
                    if (i < n && text[i] == '"')
                    {
                        i++;
                        while (i < n)
                        {
                            if (text[i] == '"')
                            {
                                if (i + 1 < n && text[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                                i++; break;
                            }
                            sb.Append(text[i++]);
                        }
                        while (i < n && text[i] != ',' && text[i] != '\n' && text[i] != '\r') i++;   // junk after the closing quote
                        row.Add(sb.ToString());
                    }
                    else
                    {
                        while (i < n && text[i] != ',' && text[i] != '\n' && text[i] != '\r') sb.Append(text[i++]);
                        row.Add(sb.ToString().Trim());
                    }
                    if (i >= n) endOfRecord = true;
                    else if (text[i] == ',') i++;
                    else
                    {
                        if (text[i] == '\r' && i + 1 < n && text[i + 1] == '\n') i++;
                        i++;
                        endOfRecord = true;
                    }
                }
                rows.Add(row);
            }
            return rows;
        }
    }
}
