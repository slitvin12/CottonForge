using System;
using System.Collections.Generic;
using MelonLoader;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Which other mods/plugins are loaded — for optional integrations without a hard reference
    /// (<c>if (Compat.IsLoaded("SR2E")) ...</c>). Names are matched case-insensitively against the MelonInfo name
    /// and the assembly name.
    /// </summary>
    public static class Compat
    {
        /// <summary>True when a melon (mod or plugin) with this MelonInfo name or assembly name is registered.</summary>
        public static bool IsLoaded(string name) => Find(name) != null;

        /// <summary>The registered melon with this MelonInfo name or assembly name, or null.</summary>
        public static MelonBase Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var m in All())
                if (Eq(m.Info?.Name, name) || Eq(AsmName(m), name)) return m;
            return null;
        }

        /// <summary>Version string from MelonInfo (null when not loaded).</summary>
        public static string Version(string name) => Find(name)?.Info?.Version;

        /// <summary>
        /// SR2MP (multiplayer) is loaded. Effects that only one machine should run (spawning actors, damage, teleports)
        /// need care then: SR2MP syncs actors it knows, not the private state of mod behaviours.
        /// </summary>
        public static bool Multiplayer => IsLoaded("SR2MP") || IsLoaded("SR2 Multiplayer");

        /// <summary>SR2E (Slime Rancher 2 Essentials, console/cheats) is loaded.</summary>
        public static bool SR2E => IsLoaded("SR2E") || IsLoaded("SR2Essentials");

        /// <summary>All registered melons: "Name version (assembly)".</summary>
        public static List<string> Mods()
        {
            var l = new List<string>();
            foreach (var m in All()) l.Add($"{m.Info?.Name} {m.Info?.Version} ({AsmName(m)})");
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        static IEnumerable<MelonBase> All()
        {
            IEnumerable<MelonBase> src;
            try { src = MelonBase.RegisteredMelons; } catch { yield break; }
            if (src == null) yield break;
            foreach (var m in src) if (m != null) yield return m;
        }

        static string AsmName(MelonBase m)
        {
            try { return m.MelonAssembly?.Assembly?.GetName().Name; } catch { return null; }
        }

        static bool Eq(string a, string b) => a != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
