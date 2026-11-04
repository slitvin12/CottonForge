using System;
using System.Collections.Generic;
using System.Linq;
using CottonLibrary;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using Object = UnityEngine.Object;
using Entry = Il2Cpp.ResourceSpawnerDefinition.WeightedResourceEntry;

namespace CottonLibrary.Forge
{
    /// <summary>Food group of a food type.</summary>
    public enum FoodKind { Veggie, Fruit, Meat, Nectar }

    /// <summary>
    /// Foods: turning on hidden SR2 foods (SR2 still ships Oca Oca, Silver Parsnip, Phase Lemon ... with models, icons and
    /// growers, but they are not registered) and adding foods to wild veggie patches / fruit trees with a share.
    /// </summary>
    public static class Foods
    {
        sealed class Wild
        {
            public IdentifiableType Type; public IdentifiableTypeGroup Group;
            public string[] Scenes; public Func<float> Share;
        }

        static readonly List<Wild> _wild = new();
        static readonly List<IdentifiableType> _enabled = new();
        /// <summary>Foods turned on with <see cref="EnableHidden"/>.</summary>
        public static IReadOnlyList<IdentifiableType> Enabled => _enabled;
        /// <summary>Wild grower additions per "scene:food" (diagnostics).</summary>
        public static readonly Dictionary<string, int> WildAdded = new();
        internal static bool DietsDirty;

        /// <summary>Cotton food group for a kind (valid from SaveDirectorLoading on).</summary>
        public static IdentifiableTypeGroup GroupOf(FoodKind kind) => kind switch
        {
            FoodKind.Fruit => Library.fruits,
            FoodKind.Meat => Library.meat,
            FoodKind.Nectar => Library.nectar,
            _ => Library.veggies,
        };

        /// <summary>
        /// Turns on an existing but unregistered SR2 food: save id + Cotton save registration, food group (asset and runtime),
        /// <c>groupType</c>, vacuumable, and a localized name from <paramref name="locKey"/> when the game has none.
        /// Call from <see cref="ForgeMod{M}.DeclareContent"/> (before slimes look up their favourites). Returns the type or null.
        /// </summary>
        public static IdentifiableType EnableHidden(string identName, FoodKind kind, string locKey = null)
        {
            var t = Idents.Find(identName);
            if (t == null) { ForgeLog.Warn($"Food {identName} not found in this game version"); return null; }
            try
            {
                Idents.RegisterForSave(t);
                var group = GroupOf(kind);
                Idents.AddToGroup(group, t);
                if (t.groupType == null) t.groupType = group;
                Idents.AddToGroup("VaccableNonLiquids", t);
                string shown = null;
                try { shown = t.localizedName?.GetLocalizedString(); } catch { }
                if (locKey != null && (t.localizedName == null || string.IsNullOrEmpty(shown) || shown.StartsWith("No translation", StringComparison.OrdinalIgnoreCase) || shown == locKey))
                    t.localizedName = Loc.String(locKey);
                if (!_enabled.Any(x => x.Pointer == t.Pointer)) _enabled.Add(t);
                DietsDirty = true;
                ForgeLog.Msg($"  food {t.name} enabled (ref {t.referenceId}, group {group?.name}, name '{shown}')");
                return t;
            }
            catch (Exception e) { ForgeLog.Error($"Enabling food {identName} failed: {e}"); return null; }
        }

        /// <summary>
        /// Makes wild growers (veggie patches / fruit trees of the same food group; not ranch gardens, not gold) in the given
        /// zones also grow <paramref name="food"/>, with about <paramref name="share"/> (0..0.9) of each grower's yield.
        /// </summary>
        public static void AddToWild(IdentifiableType food, Library.SpawnLocations zones, Func<float> share, FoodKind? kind = null)
        {
            if (food == null) return;
            var group = kind.HasValue ? GroupOf(kind.Value) : (food.groupType ?? (Idents.InGroup(Library.fruits, food) ? Library.fruits : Library.veggies));
            var scenes = Library.GetSceneNamesFromSpawnerZones(zones).Select(x => x.ToLowerInvariant()).ToArray();
            _wild.Add(new Wild { Type = food, Group = group, Scenes = scenes, Share = share ?? (() => 0.15f) });
            ForgeLog.Msg($"  food {food.name} grows wild in [{string.Join(",", scenes)}]");
        }

        /// <inheritdoc cref="AddToWild(IdentifiableType, Library.SpawnLocations, Func{float}, FoodKind?)"/>
        public static void AddToWild(IdentifiableType food, Library.SpawnLocations zones, float share, FoodKind? kind = null) =>
            AddToWild(food, zones, () => share, kind);

        static readonly Dictionary<(IntPtr, string), ResourceGrowerDefinition> _clones = new();
        static readonly HashSet<IntPtr> _mine = new();

        internal static void OnSpawnResourceAwake(SpawnResource sr)
        {
            if (_wild.Count == 0 || sr == null) return;
            var def = sr._resourceGrowerDefinition;
            if (def == null || _mine.Contains(def.Pointer)) return;
            string scene = sr.gameObject.scene.name?.ToLowerInvariant() ?? "";
            if (_clones.TryGetValue((def.Pointer, scene), out var known)) { if (known != null) sr._resourceGrowerDefinition = known; return; }
            if (sr.GetComponentInParent<LandPlot>() != null) return;   // ranch garden: the player chooses what grows
            var res = def._resources;
            if (res == null || res.Length == 0 || (def.name ?? "").IndexOf("gold", StringComparison.OrdinalIgnoreCase) >= 0) return;
            float sum = 0f;
            var present = new HashSet<IntPtr>();
            foreach (var e in res)
            {
                if (e?.ResourceIdentifiableType == null) continue;
                present.Add(e.ResourceIdentifiableType.Pointer);
                sum += e.Weight;
            }
            if (sum <= 0f) return;
            var extra = new List<Entry>();
            foreach (var w in _wild)
            {
                if (present.Contains(w.Type.Pointer) || !w.Scenes.Any(z => scene.Contains(z))) continue;
                bool sameGroup = false;
                foreach (var e in res) if (e?.ResourceIdentifiableType != null && Idents.InGroup(w.Group, e.ResourceIdentifiableType)) { sameGroup = true; break; }
                if (!sameGroup) continue;
                float p = Mathf.Clamp(w.Share(), 0f, 0.9f);
                if (p <= 0f) continue;
                extra.Add(new Entry { ResourceIdentifiableType = w.Type, Weight = sum * p / (1f - p), MinimumAmount = 0 });
                present.Add(w.Type.Pointer);
            }
            if (extra.Count == 0) { _clones[(def.Pointer, scene)] = null; return; }
            var clone = Object.Instantiate(def);
            clone.name = def.name + "_Forge";
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var list = new List<Entry>(res.Length + extra.Count);
            foreach (var e in res) list.Add(e);
            list.AddRange(extra);
            clone._resources = Gc.Array(list);
            _clones[(def.Pointer, scene)] = clone;
            _mine.Add(clone.Pointer);
            sr._resourceGrowerDefinition = clone;
            foreach (var e in extra)
            {
                string k = scene + ":" + e.ResourceIdentifiableType.name;
                WildAdded.TryGetValue(k, out int c); WildAdded[k] = c + 1;
            }
        }
    }

    [HarmonyPatch(typeof(SpawnResource), nameof(SpawnResource.Awake))]
    static class SpawnResourceAwakePatch
    {
        static void Prefix(SpawnResource __instance) => Safe.Run("wild foods", () => Foods.OnSpawnResourceAwake(__instance));
    }
}
