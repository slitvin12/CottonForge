using System;
using System.Collections.Generic;
using System.Reflection;
using CottonLibrary;
using Il2Cpp;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>What <see cref="Idents.FindByHints"/> may return.</summary>
    public enum IdentKind
    {
        /// <summary>Anything except slimes and plorts.</summary>
        Any,
        /// <summary>Food: not a toy, slime or plort (optionally restricted to a group).</summary>
        Food,
        /// <summary><c>ToyDefinition</c> only.</summary>
        Toy,
        /// <summary><c>SlimeDefinition</c> only.</summary>
        Slime,
        /// <summary>Plorts only.</summary>
        Plort,
    }

    /// <summary>
    /// Lookup and registration of <c>IdentifiableType</c>s (slimes, foods, plorts, toys ...). Lookups are case-insensitive
    /// and indexed once (call <see cref="Reindex"/> after creating many new types if you look them up by name).
    /// </summary>
    public static class Idents
    {
        static Dictionary<string, IdentifiableType> _byName;
        static List<KeyValuePair<string, IdentifiableType>> _ordered;

        /// <summary>Rebuilds the name index (cheap, ~1 ms).</summary>
        public static void Reindex()
        {
            _byName = new Dictionary<string, IdentifiableType>(StringComparer.OrdinalIgnoreCase);
            _ordered = new List<KeyValuePair<string, IdentifiableType>>();
            foreach (var t in Resources.FindObjectsOfTypeAll<IdentifiableType>())
            {
                if (t == null || string.IsNullOrEmpty(t.name) || _byName.ContainsKey(t.name)) continue;
                _byName[t.name] = t;
                _ordered.Add(new KeyValuePair<string, IdentifiableType>(t.name, t));
            }
            _ordered.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));   // deterministic hint matching
        }

        static void Ensure() { if (_byName == null) Reindex(); }

        /// <summary>Type by exact asset name (case-insensitive), e.g. "PinkPlort", "CarrotVeggie", or null.</summary>
        public static IdentifiableType Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Ensure();
            if (_byName.TryGetValue(name, out var t) && t != null) return t;
            Reindex();   // types created after the first lookup
            return _byName.TryGetValue(name, out t) ? t : null;
        }

        /// <summary>Typed <see cref="Find"/>, e.g. <c>Find&lt;ToyDefinition&gt;("BeachBall")</c>.</summary>
        public static T Find<T>(string name) where T : IdentifiableType => Find(name).As<T>();

        static bool Matches(IdentifiableType t, IdentKind kind, IdentifiableTypeGroup group)
        {
            if (t == null) return false;
            bool toy = t.Is<ToyDefinition>(), slime = t.Is<SlimeDefinition>(), plort = false;
            try { plort = t.IsPlort; } catch { }
            switch (kind)
            {
                case IdentKind.Toy: if (!toy) return false; break;
                case IdentKind.Slime: if (!slime) return false; break;
                case IdentKind.Plort: if (!plort) return false; break;
                case IdentKind.Food: if (toy || slime || plort) return false; break;
                default: if (slime || plort) return false; break;
            }
            return group == null || InGroup(group, t);
        }

        /// <summary>
        /// First type matching one of the <paramref name="hints"/> (in the given order): exact name first, then a name that
        /// starts with the hint, then a name that contains it. Lets mods survive renamed or missing assets between game versions:
        /// <c>FindByHints(new[] { "OcaOca", "Oca", "Beet" }, IdentKind.Food, Library.veggies)</c>.
        /// </summary>
        public static IdentifiableType FindByHints(IEnumerable<string> hints, IdentKind kind = IdentKind.Food, IdentifiableTypeGroup group = null)
        {
            if (hints == null) return null;
            Ensure();
            foreach (var h in hints)
            {
                if (string.IsNullOrEmpty(h)) continue;
                if (_byName.TryGetValue(h, out var exact) && Matches(exact, kind, group)) return exact;
                IdentifiableType contains = null;
                foreach (var kv in _ordered)
                {
                    int i = kv.Key.IndexOf(h, StringComparison.OrdinalIgnoreCase);
                    if (i < 0 || !Matches(kv.Value, kind, group)) continue;
                    if (i == 0) return kv.Value;
                    contains ??= kv.Value;
                }
                if (contains != null) return contains;
            }
            return null;
        }

        /// <summary>Group lookup by asset name, e.g. "VeggieGroup", "VaccableNonLiquids".</summary>
        public static IdentifiableTypeGroup Group(string name) => string.IsNullOrEmpty(name) ? null : Library.Get<IdentifiableTypeGroup>(name);

        /// <summary>Null-safe <c>IsMember</c> (includes sub-groups).</summary>
        public static bool InGroup(IdentifiableTypeGroup g, IdentifiableType t)
        {
            if (g == null || t == null) return false;
            try { return g.IsMember(t); } catch { return false; }
        }

        /// <summary>
        /// Adds <paramref name="t"/> to the group asset AND to its runtime object, skipping either when already a member
        /// (Cotton's <c>AddToGroup</c> adds duplicates). Returns true when something was added.
        /// </summary>
        public static bool AddToGroup(IdentifiableTypeGroup g, IdentifiableType t)
        {
            if (g == null || t == null) return false;
            bool added = false;
            try { if (!g._memberTypes.Contains(t)) { g._memberTypes.Add(t); added = true; } }
            catch (Exception e) { ForgeLog.WarnOnce("group:" + g.name, $"group {g.name}: {e.Message}"); }
            try
            {
                var rt = g.GetRuntimeObject();
                if (rt != null && !rt.IsMember(t)) { rt._memberTypes.Add(t); added = true; }
            }
            catch (Exception e) { ForgeLog.WarnOnce("grouprt:" + g.name, $"group {g.name} runtime: {e.Message}"); }
            return added;
        }

        /// <summary><see cref="AddToGroup(IdentifiableTypeGroup, IdentifiableType)"/> by group name.</summary>
        public static bool AddToGroup(string groupName, IdentifiableType t) => AddToGroup(Group(groupName), t);

        static MethodInfo _setupLoad;

        /// <summary>
        /// Registers an existing type for saving through Cotton (the same path Cotton uses for its own types), giving it a
        /// reference id first when it has none (<c>"IdentifiableType." + name</c>). Needed for hidden SR2 types that a mod
        /// turns on (they have no save id, so actors of that type would vanish on reload).
        /// </summary>
        public static bool RegisterForSave(IdentifiableType t, string refId = null)
        {
            if (t == null) return false;
            refId ??= string.IsNullOrEmpty(t.referenceId) ? "IdentifiableType." + t.name : t.referenceId;
            t.referenceId = refId;
            _setupLoad ??= typeof(Library).GetMethod("INTERNAL_SetupLoadForIdent", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (_setupLoad == null) { t.SetupForSaving(refId); ForgeLog.WarnOnce("setupload", "Cotton INTERNAL_SetupLoadForIdent not found, used SetupForSaving"); return true; }
            _setupLoad.Invoke(null, new object[] { refId, t });
            return true;
        }

        /// <summary>Localized name of a type, or its asset name.</summary>
        public static string DisplayName(IdentifiableType t)
        {
            if (t == null) return "null";
            try { var s = t.localizedName?.GetLocalizedString(); if (!string.IsNullOrEmpty(s)) return s; } catch { }
            return t.name;
        }

        /// <summary>Calls <c>RefreshEatmap</c> on every base (non-largo) slime: needed after adding foods to food groups.</summary>
        public static int RefreshBaseDiets()
        {
            int n = 0;
            foreach (var d in Resources.FindObjectsOfTypeAll<SlimeDefinition>())
            {
                if (d == null || d.IsLargo) continue;
                try { d.RefreshEatmap(); n++; } catch { }
            }
            return n;
        }

        /// <summary>Calls <c>RefreshEatmap</c> on every largo (thousands with many mods: do it once, e.g. on the first game load).</summary>
        public static int RefreshLargoDiets()
        {
            int n = 0;
            var defs = GameContext.Instance != null ? GameContext.Instance.SlimeDefinitions : null;
            if (defs?.Slimes == null) return 0;
            Gc.Window("largo diets", () =>
            {
                foreach (var d in defs.Slimes)
                {
                    if (d == null || !d.IsLargo) continue;
                    try { d.RefreshEatmap(); n++; } catch { }
                }
            });
            return n;
        }
    }
}
