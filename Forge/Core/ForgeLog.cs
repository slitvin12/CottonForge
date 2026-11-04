using System;
using System.Collections.Generic;
using MelonLoader;

namespace CottonLibrary.Forge
{
    /// <summary>Cotton/Forge log (MelonLoader <c>Latest.log</c>).</summary>
    public static class ForgeLog
    {
        static readonly HashSet<string> _once = new();

        static MelonLogger.Instance L => ForgeCore.Logger;

        public static void Msg(string s) { if (L != null) L.Msg(s); else MelonLogger.Msg("[Cotton] " + s); }
        public static void Warn(string s) { if (L != null) L.Warning(s); else MelonLogger.Warning("[Cotton] " + s); }
        public static void Error(string s) { if (L != null) L.Error(s); else MelonLogger.Error("[Cotton] " + s); }

        /// <summary>Only with <c>Verbose = true</c>.</summary>
        public static void Debug(string s) { if (ForgeConfig.Verbose != null && ForgeConfig.Verbose.Value) Msg("[debug] " + s); }

        /// <summary>Logs a warning only the first time for the given key (for things that repeat every frame).</summary>
        public static void WarnOnce(string key, string s) { if (_once.Add(key)) Warn(s + " (further cases are silent)"); }

        /// <summary>Logs a message only the first time for the given key.</summary>
        public static void MsgOnce(string key, string s) { if (_once.Add(key)) Msg(s); }
    }

    /// <summary>Exception guards. An exception in Update/FixedUpdate repeats every frame and floods the log, so wrap risky code.</summary>
    public static class Safe
    {
        /// <summary>Runs the action; logs the full exception once per <paramref name="what"/>. Returns false on failure.</summary>
        public static bool Run(string what, Action a)
        {
            try { a(); return true; }
            catch (Exception e) { ForgeLog.WarnOnce("safe:" + what, $"{what} failed: {e}"); return false; }
        }

        /// <summary>Runs the function; returns <paramref name="fallback"/> and logs once on failure.</summary>
        public static T Run<T>(string what, Func<T> f, T fallback = default)
        {
            try { return f(); }
            catch (Exception e) { ForgeLog.WarnOnce("safe:" + what, $"{what} failed: {e}"); return fallback; }
        }

        /// <summary>Runs the action and ignores every exception.</summary>
        public static void Quiet(Action a) { try { a(); } catch { } }
    }
}
