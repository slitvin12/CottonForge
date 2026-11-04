using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// MelonPreferences that apply while the game runs: <see cref="Watch{T}"/> calls a handler when an entry changes
    /// (dev console <c>cfg</c>, another mod, or <c>cfg reload</c> after editing MelonPreferences.cfg), and
    /// <see cref="Set"/>/<see cref="Get"/> work with any mod's entries by name. Checked every <see cref="Interval"/> seconds.
    /// </summary>
    public static class LiveConfig
    {
        sealed class W { public MelonPreferences_Entry E; public object Last; public Action<object> Run; public string Name; }

        static readonly List<W> _w = new();
        static float _next;

        /// <summary>Seconds between change checks (real time).</summary>
        public static float Interval = 0.5f;
        /// <summary>Number of watched entries.</summary>
        public static int Count => _w.Count;

        /// <summary>
        /// <paramref name="onChange"/>(new value) every time <paramref name="entry"/> changes; with <paramref name="fireNow"/>
        /// also once right away (to apply the current value with the same code).
        /// </summary>
        public static void Watch<T>(MelonPreferences_Entry<T> entry, Action<T> onChange, bool fireNow = false)
        {
            if (entry == null || onChange == null) return;
            var w = new W { E = entry, Last = entry.Value, Run = o => onChange((T)o), Name = $"cfg:{entry.Category?.Identifier}.{entry.Identifier}" };
            _w.Add(w);
            if (fireNow) Fire(w, entry.Value);
        }

        /// <summary>Entry by category and entry identifier (case-insensitive), or null.</summary>
        public static MelonPreferences_Entry Find(string category, string entry)
        {
            foreach (var c in MelonPreferences.Categories)
            {
                if (!string.Equals(c.Identifier, category, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var e in c.Entries)
                    if (string.Equals(e.Identifier, entry, StringComparison.OrdinalIgnoreCase)) return e;
            }
            return null;
        }

        /// <summary>Current value as invariant text, or null when the entry does not exist.</summary>
        public static string Get(string category, string entry)
        {
            var e = Find(category, entry);
            return e == null ? null : Text(e.BoxedValue);
        }

        /// <summary>
        /// Parses <paramref name="value"/> for the entry's type (bool: true/false/1/0/on/off, numbers with '.' or ',',
        /// enums by name) and assigns it; watchers fire on the next check. Returns null on success, otherwise the error.
        /// </summary>
        public static string Set(string category, string entry, string value, bool save = true)
        {
            var e = Find(category, entry);
            if (e == null) return $"no entry {category}.{entry}";
            var t = e.BoxedValue?.GetType();
            if (t == null) return "entry has no value type";
            try
            {
                e.BoxedValue = Parse(t, value);
                if (save) e.Category?.SaveToFile(false);
                return null;
            }
            catch (Exception x) { return $"cannot set {t.Name} from '{value}': {x.Message}"; }
        }

        /// <summary>Re-reads MelonPreferences.cfg (after editing it by hand); watchers fire for changed entries.</summary>
        public static void ReloadFromDisk() => MelonPreferences.Load();

        /// <summary>Writes all categories to MelonPreferences.cfg.</summary>
        public static void Save() => MelonPreferences.Save();

        /// <summary>Category identifiers.</summary>
        public static List<string> Categories()
        {
            var l = new List<string>();
            foreach (var c in MelonPreferences.Categories) l.Add(c.Identifier);
            return l;
        }

        /// <summary>"Entry = value" lines of a category (empty when it does not exist).</summary>
        public static List<string> Entries(string category)
        {
            var l = new List<string>();
            foreach (var c in MelonPreferences.Categories)
                if (string.Equals(c.Identifier, category, StringComparison.OrdinalIgnoreCase))
                    foreach (var e in c.Entries) l.Add($"{e.Identifier} = {Text(e.BoxedValue)}");
            return l;
        }

        internal static void Tick()
        {
            if (_w.Count == 0 || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            foreach (var w in _w)
            {
                object v;
                try { v = w.E.BoxedValue; } catch { continue; }
                if (Equals(v, w.Last)) continue;
                Fire(w, v);
            }
        }

        static void Fire(W w, object v)
        {
            w.Last = v;
            try { w.Run(v); } catch (Exception x) { ForgeLog.WarnOnce(w.Name, $"LiveConfig handler {w.Name} failed: {x}"); }
        }

        static object Parse(Type t, string s)
        {
            s = (s ?? "").Trim();
            if (t == typeof(string)) return s;
            if (t == typeof(bool))
            {
                switch (s.ToLowerInvariant())
                {
                    case "1": case "on": case "yes": case "true": return true;
                    case "0": case "off": case "no": case "false": return false;
                }
                throw new FormatException("expected true/false");
            }
            if (t.IsEnum) return Enum.Parse(t, s, true);
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) s = s.Replace(',', '.');
            return Convert.ChangeType(s, t, CultureInfo.InvariantCulture);
        }

        static string Text(object v) => v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : v?.ToString() ?? "null";
    }
}
