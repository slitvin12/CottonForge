using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Largo helpers and the repairs Forge applies to Cotton's generated largos right after generation (and once more on
    /// the first game load):
    /// <list type="bullet">
    /// <item><see cref="FixModules"/>: Cotton fills <c>SlimeModules</c> with <c>Get("moduleSlime" + name)</c>, which is null for modded
    /// slimes; a null module aborts slime setup and the largo AI never starts.</item>
    /// <item><see cref="FixPlexer"/>: largo prefabs are PinkRock copies with <c>RockSlimeRoll</c> destroyed, but the
    /// <c>SubbehaviourPlexer</c> keeps the dead reference: NullReferenceException every FixedUpdate, the largo stands still.</item>
    /// </list>
    /// For largos of Forge slimes it also applies names (<c>l.a_b_largo</c>), behaviours and <see cref="SlimeBuilder.CustomizeLargo"/>.
    /// </summary>
    public static class Largos
    {
        static readonly HashSet<IntPtr> _done = new();
        public static int FixedModules, FixedPlexers, Customized;

        /// <summary>Every distinct largo definition.</summary>
        public static List<SlimeDefinition> All()
        {
            var res = new List<SlimeDefinition>();
            var defs = GameContext.Instance != null ? GameContext.Instance.SlimeDefinitions : null;
            if (defs?.Slimes == null) return res;
            var seen = new HashSet<IntPtr>();
            foreach (var d in defs.Slimes)
                if (d != null && d.IsLargo && seen.Add(d.Pointer)) res.Add(d);
            return res;
        }

        /// <summary>Largos with <paramref name="baseDef"/> as a parent.</summary>
        public static IEnumerable<SlimeDefinition> Of(SlimeDefinition baseDef) =>
            baseDef == null ? Enumerable.Empty<SlimeDefinition>() : All().Where(d => d.BaseSlimes != null && d.BaseSlimes.Any(b => b != null && b.Pointer == baseDef.Pointer));

        /// <summary>The largo of two base slimes, or null.</summary>
        public static SlimeDefinition Find(SlimeDefinition a, SlimeDefinition b) =>
            a == null || b == null ? null : All().FirstOrDefault(d => d.BaseSlimes != null && d.BaseSlimes.Length == 2 &&
                ((d.BaseSlimes[0]?.Pointer == a.Pointer && d.BaseSlimes[1]?.Pointer == b.Pointer) || (d.BaseSlimes[0]?.Pointer == b.Pointer && d.BaseSlimes[1]?.Pointer == a.Pointer)));

        /// <summary>Replaces null <c>SlimeModules</c> with the parents' modules. Returns true when changed.</summary>
        public static bool FixModules(SlimeDefinition d)
        {
            var mods = d.SlimeModules;
            var bases = d.BaseSlimes;
            int len = Math.Max(mods?.Length ?? 0, bases?.Length ?? 0);
            var list = new List<GameObject>();
            bool hadNull = mods == null;
            for (int i = 0; i < len; i++)
            {
                GameObject m = mods != null && i < mods.Length ? mods[i] : null;
                if (m == null)
                {
                    hadNull = true;
                    var b = bases != null && i < bases.Length ? bases[i] : null;
                    if (b != null && b.SlimeModules != null)
                        foreach (var bm in b.SlimeModules) if (bm != null) { m = bm; break; }
                }
                if (m != null && !list.Any(x => x.Pointer == m.Pointer)) list.Add(m);
            }
            if (!hadNull || list.Count == 0) return false;
            d.SlimeModules = Gc.Array(list);
            return true;
        }

        /// <summary>
        /// Rebuilds <c>SubbehaviourPlexer._subbehaviors</c> without dead entries; with <paramref name="addMissing"/> also adds every live
        /// <c>Subbehaviour</c> of the prefab (enables behaviours copied from the parents, e.g. Tabby's StalkConsumable).
        /// </summary>
        public static bool FixPlexer(GameObject prefab, bool addMissing)
        {
            if (prefab == null) return false;
            var px = prefab.GetComponent<SubbehaviourPlexer>();
            if (px == null) return false;
            var cur = px._subbehaviors;
            var list = new List<Subbehaviour>();
            bool dead = cur == null;
            if (cur != null)
                foreach (var sb in cur)
                {
                    if (sb == null) { dead = true; continue; }
                    if (!list.Any(x => x.Pointer == sb.Pointer)) list.Add(sb);
                }
            int before = list.Count;
            if (addMissing || dead)
                foreach (var sb in prefab.GetComponents<Subbehaviour>())
                    if (sb != null && !list.Any(x => x.Pointer == sb.Pointer)) list.Add(sb);
            if (!dead && list.Count == before) return false;
            px._subbehaviors = Gc.Array(list);
            return true;
        }

        /// <summary>Runs the repairs and the Forge largo setup on every largo not processed yet (idempotent).</summary>
        internal static void Process(string when)
        {
            var all = All();
            if (all.Count == 0) return;
            int modules = 0, plexers = 0, named = 0, custom = 0, forgeLargos = 0;
            foreach (var d in all)
            {
                var builders = new List<SlimeBuilder>();
                if (d.BaseSlimes != null)
                    foreach (var b in d.BaseSlimes) { var sb = SlimeRegistry.Of(b); if (sb != null && !builders.Contains(sb)) builders.Add(sb); }
                bool forge = builders.Count > 0;
                if (forge) forgeLargos++;
                Safe.Run("LargoEngine " + d.name, () => LargoEngine.ProcessDefinition(d));
                if (ForgeConfig.FixLargoAI.Value)
                {
                    try { if (FixPlexer(d.prefab, forge)) plexers++; } catch (Exception e) { ForgeLog.WarnOnce("plexer:" + d.name, $"Largo AI {d.name}: {e.Message}"); }
                    try { if (FixModules(d)) modules++; } catch (Exception e) { ForgeLog.WarnOnce("modules:" + d.name, $"Largo modules {d.name}: {e.Message}"); }
                }
                if (!forge || !_done.Add(d.Pointer)) continue;
                if (Safe.Run("largo name " + d.name, () => Loc.NameLargo(d), false)) named++;
                foreach (var b in builders)
                {
                    foreach (var (add, largos) in b.Spawned) if (largos) Behaviours.Attach(d, add);
                    b.RunCustomizeLargo(d);
                    custom++;
                }
            }
            FixedModules += modules; FixedPlexers += plexers; Customized += custom;
            if (modules + plexers + named + custom > 0 || when == "generation")
                ForgeLog.Msg($"Largos ({when}): {all.Count} total, {forgeLargos} with Forge slimes; fixed AI lists {plexers}, modules {modules}; named {named}, customized {custom}" +
                    (LargoBlend.Built + LargoBlend.Fallbacks > 0 ? $"; smart looks {LargoBlend.Built} (radiant variants {LargoBlend.RadiantVariants}, fallbacks {LargoBlend.Fallbacks})" : ""));
        }
    }

    /// <summary>All slimes created with <see cref="SlimeBuilder"/>, from every mod.</summary>
    public static class SlimeRegistry
    {
        static readonly List<SlimeBuilder> _all = new();
        static readonly Dictionary<IntPtr, SlimeBuilder> _byDef = new();
        static readonly Dictionary<string, SlimeBuilder> _byName = new(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<SlimeBuilder> All => _all;

        internal static void Add(SlimeBuilder b)
        {
            if (b?.Def == null || _byDef.ContainsKey(b.Def.Pointer)) return;
            if (_byName.TryGetValue(b.Name, out var other) && other != b)
                ForgeLog.Error($"Two mods create a slime named {b.Name} ({other.ModAssembly.GetName().Name}, {b.ModAssembly.GetName().Name}): saves will mix them up");
            _all.Add(b);
            _byDef[b.Def.Pointer] = b;
            _byName[b.Name] = b;
        }

        /// <summary>Builder of a Forge slime definition, or null.</summary>
        public static SlimeBuilder Of(SlimeDefinition def) => def != null && _byDef.TryGetValue(def.Pointer, out var b) ? b : null;
        /// <summary>Builder by slime name, or null.</summary>
        public static SlimeBuilder Find(string name) => name != null && _byName.TryGetValue(name, out var b) ? b : null;
        public static bool IsForge(SlimeDefinition def) => Of(def) != null;
        /// <summary>Localization key part: <see cref="SlimeBuilder.KeyName"/> for Forge slimes, lower-case name otherwise.</summary>
        public static string KeyOf(SlimeDefinition def) => Of(def)?.KeyName ?? def?.name?.ToLowerInvariant() ?? "";
    }
}
