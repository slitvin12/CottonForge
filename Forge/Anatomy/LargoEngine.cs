using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Largo pipeline. Definition level (when largos are generated): diets of both bases are united and the voice set of
    /// the dominant base is applied. Spawn level: <see cref="Anatomy.Resolve"/> runs the <see cref="ILargoMerger"/>
    /// chain — layers, parts, archetypes (at <see cref="LargoIntensity"/>) and voices of both bases.
    /// Works for any pair: Forge + Forge, Forge + vanilla, vanilla + vanilla with declared anatomy.
    /// </summary>
    public static class LargoEngine
    {
        /// <summary>Strength of each base archetype in a largo (1 = same as the base slime).</summary>
        public static float LargoIntensity = 0.75f;
        /// <summary>Unite the bases' food groups, extra foods and favourites into the largo diet (largos with a Forge base or declared anatomy).</summary>
        public static bool MergeDiets = true;
        /// <summary>Also merge diets of largos made only of vanilla / other mods' slimes (off: their diets stay as they are).</summary>
        public static bool MergeAllLargos = false;
        /// <summary>Largos processed at definition level / diets widened / voices applied.</summary>
        public static int Processed, DietsMerged, VoicesApplied;

        static readonly List<ILargoMerger> _mergers = new() { new LayerMerger(), new PartMerger(), new ArchetypeMerger(), new VoiceMerger() };
        static readonly Dictionary<IntPtr, int> _done = new();

        public static IReadOnlyList<ILargoMerger> Mergers => _mergers;

        /// <summary>Adds a merger at the end of the chain (or replaces the one with the same id).</summary>
        public static void AddMerger(ILargoMerger m)
        {
            if (m == null) return;
            int i = _mergers.FindIndex(x => x.Id == m.Id);
            if (i >= 0) _mergers[i] = m; else _mergers.Add(m);
            Anatomy.Invalidate();
        }

        public static bool RemoveMerger(string id) { bool r = _mergers.RemoveAll(x => x.Id == id) > 0; Anatomy.Invalidate(); return r; }

        internal static AnatomySpec Merge(SlimeDefinition largo, List<AnatomySpec> bases)
        {
            var r = new AnatomySpec();
            foreach (var m in _mergers)
                try { m.Merge(largo, bases, r); }
                catch (Exception e) { ForgeLog.WarnOnce("merger:" + m.Id, $"Largo merger {m.Id} failed for {largo?.name}: {e.Message}"); }
            r.Def = largo;
            return r;
        }

        /// <summary>Definition-level processing of one largo (idempotent; re-runs after its anatomy changed).</summary>
        public static void ProcessDefinition(SlimeDefinition largo)
        {
            if (largo == null || !largo.IsLargo) return;
            var spec = Anatomy.Resolve(largo);
            int version = spec == null ? 0 : spec.Version + 1 + spec.Archetypes.Count;
            if (_done.TryGetValue(largo.Pointer, out var v) && v == version) return;
            bool first = !_done.ContainsKey(largo.Pointer);
            _done[largo.Pointer] = version;
            Processed++;
            if (first && MergeDiets && (MergeAllLargos || spec != null || HasForgeBase(largo)) && Safe.Run("largo diet " + largo.name, () => MergeDiet(largo), false)) DietsMerged++;
            if (spec?.Voice != null && Voices.Apply(largo, spec.Voice)) VoicesApplied++;
        }

        static bool HasForgeBase(SlimeDefinition largo)
        {
            if (largo.BaseSlimes == null) return false;
            foreach (var b in largo.BaseSlimes) if (b != null && SlimeRegistry.Of(b) != null) return true;
            return false;
        }

        /// <summary>Re-runs definition-level processing for all largos (after declaring anatomy late).</summary>
        public static void Reprocess() { Anatomy.Invalidate(); foreach (var d in Largos.All()) ProcessDefinition(d); }

        /// <summary>Unites food groups, extra foods and favourites of the bases into the largo's diet. True when it changed.</summary>
        public static bool MergeDiet(SlimeDefinition largo)
        {
            var diet = largo?.Diet;
            if (diet == null || largo.BaseSlimes == null) return false;
            bool changed = false;
            var groups = new List<IdentifiableTypeGroup>();
            if (diet.MajorFoodIdentifiableTypeGroups != null) foreach (var g in diet.MajorFoodIdentifiableTypeGroups) if (g != null) groups.Add(g);
            foreach (var b in largo.BaseSlimes)
            {
                var bd = b?.Diet;
                if (bd == null) continue;
                if (bd.MajorFoodIdentifiableTypeGroups != null)
                    foreach (var g in bd.MajorFoodIdentifiableTypeGroups)
                        if (g != null && !groups.Exists(x => x.Pointer == g.Pointer)) { groups.Add(g); changed = true; }
                if (bd.AdditionalFoodIdents != null)
                    foreach (var t in bd.AdditionalFoodIdents)
                        if (t != null && !Contains(diet.AdditionalFoodIdents, t)) { largo.AddExtraEatIdent(t); changed = true; }
                if (bd.FavoriteIdents != null)
                    foreach (var t in bd.FavoriteIdents)
                        if (t != null && !Contains(diet.FavoriteIdents, t)) { largo.AddFavorite(t); changed = true; }
            }
            if (!changed) return false;
            diet.MajorFoodIdentifiableTypeGroups = Gc.Array(groups);
            try { largo.RefreshEatmap(); } catch (Exception e) { ForgeLog.WarnOnce("eatmap:" + largo.name, $"RefreshEatmap {largo.name}: {e.Message}"); }
            return true;
        }

        static bool Contains(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<IdentifiableType> a, IdentifiableType t)
        {
            if (a == null) return false;
            foreach (var x in a) if (x != null && x.Pointer == t.Pointer) return true;
            return false;
        }

        // ------------------------------------------------------------------ default mergers

        /// <summary>Union of layers (by name); the first base wins on duplicates.</summary>
        public sealed class LayerMerger : ILargoMerger
        {
            public string Id => "layers";
            public void Merge(SlimeDefinition largo, IReadOnlyList<AnatomySpec> bases, AnatomySpec result)
            {
                foreach (var b in bases)
                    foreach (var l in b.Layers)
                        if (l.OnLargos && !result.Layers.Exists(x => x.Name == l.Name)) result.Layers.Add(l);
            }
        }

        /// <summary>Union of parts (by name).</summary>
        public sealed class PartMerger : ILargoMerger
        {
            public string Id => "parts";
            public void Merge(SlimeDefinition largo, IReadOnlyList<AnatomySpec> bases, AnatomySpec result)
            {
                foreach (var b in bases)
                    foreach (var p in b.Parts)
                        if (p.OnLargos && !result.Parts.Exists(x => x.Name == p.Name)) result.Parts.Add(p);
            }
        }

        /// <summary>Union of archetypes at <see cref="LargoIntensity"/>; an archetype both bases share keeps full strength.</summary>
        public sealed class ArchetypeMerger : ILargoMerger
        {
            public string Id => "archetypes";
            public void Merge(SlimeDefinition largo, IReadOnlyList<AnatomySpec> bases, AnatomySpec result)
            {
                var sum = new Dictionary<string, (IArchetype a, float i)>();
                var order = new List<string>();
                foreach (var b in bases)
                    foreach (var u in b.Archetypes)
                    {
                        float i = u.Intensity * LargoIntensity;
                        if (sum.TryGetValue(u.Archetype.Id, out var prev)) sum[u.Archetype.Id] = (prev.a, Mathf.Min(Mathf.Max(prev.i, u.Intensity), prev.i + i));
                        else { sum[u.Archetype.Id] = (u.Archetype, i); order.Add(u.Archetype.Id); }
                    }
                foreach (var id in order) result.Archetypes.Add(new AnatomySpec.ArchetypeUse(sum[id].a, sum[id].i));
            }
        }

        /// <summary>The voice of the base with the highest priority (ties: the first base).</summary>
        public sealed class VoiceMerger : ILargoMerger
        {
            public string Id => "voices";
            public void Merge(SlimeDefinition largo, IReadOnlyList<AnatomySpec> bases, AnatomySpec result)
            {
                foreach (var b in bases)
                    if (b.Voice != null && (result.Voice == null || b.VoicePriority > result.VoicePriority)) { result.Voice = b.Voice; result.VoicePriority = b.VoicePriority; }
            }
        }
    }
}
