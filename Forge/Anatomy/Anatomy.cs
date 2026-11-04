using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Anatomy of one slime definition: extra skinned layers (glass shell, coat, glow), attached parts (horns, crystals,
    /// wings), behaviour archetypes and a voice set. Works the same for Forge slimes and vanilla ones
    /// (<c>Anatomy.For("Pink").Archetype(Archetypes.Get("batty"))</c>); largos get a merged spec (see <see cref="LargoEngine"/>).
    /// </summary>
    public sealed class AnatomySpec
    {
        /// <summary>A skinned layer drawn over the body (see <see cref="MeshKit.AddLayer"/>).</summary>
        public sealed class LayerSpec
        {
            public string Name; public Func<Material> Material; public float Inflate = 0.02f; public int Facets;
            /// <summary>Largos keep the layer (default true).</summary>
            public bool OnLargos = true;
        }

        /// <summary>An object attached to the spawned slime; <see cref="Attach"/> returns the created object.</summary>
        public sealed class PartSpec
        {
            public string Name; public Func<GameObject, GameObject> Attach;
            /// <summary>Scale applied to the part on largos (the largo body is already bigger).</summary>
            public float LargoScale = 1f;
            public bool OnLargos = true;
        }

        /// <summary>An archetype with its strength.</summary>
        public readonly struct ArchetypeUse
        {
            public readonly IArchetype Archetype; public readonly float Intensity;
            public ArchetypeUse(IArchetype a, float i) { Archetype = a; Intensity = i; }
        }

        public readonly List<LayerSpec> Layers = new();
        public readonly List<PartSpec> Parts = new();
        public readonly List<ArchetypeUse> Archetypes = new();
        /// <summary>Voices for the definition (largos: the base with the highest <see cref="VoicePriority"/> wins).</summary>
        public Voices.Set Voice;
        public int VoicePriority;
        /// <summary>When false largos inherit nothing from this slime.</summary>
        public bool PassToLargos = true;
        /// <summary>The definition (null for a merged spec under construction).</summary>
        public SlimeDefinition Def { get; internal set; }
        internal int Version;

        public bool IsEmpty => Layers.Count == 0 && Parts.Count == 0 && Archetypes.Count == 0 && Voice == null;

        public AnatomySpec Layer(string name, Func<Material> material, float inflate = 0.02f, int facets = 0, bool onLargos = true)
        { Layers.Add(new LayerSpec { Name = name, Material = material, Inflate = inflate, Facets = facets, OnLargos = onLargos }); return Touch(); }

        public AnatomySpec Part(string name, Func<GameObject, GameObject> attach, float largoScale = 1f, bool onLargos = true)
        { Parts.Add(new PartSpec { Name = name, Attach = attach, LargoScale = largoScale, OnLargos = onLargos }); return Touch(); }

        public AnatomySpec Archetype(IArchetype archetype, float intensity = 1f)
        {
            if (archetype == null) { ForgeLog.Warn("AnatomySpec.Archetype: null archetype"); return this; }
            Archetypes.RemoveAll(a => a.Archetype.Id == archetype.Id);
            Archetypes.Add(new ArchetypeUse(archetype, intensity));
            return Touch();
        }

        /// <summary>Archetype from the catalog by id (<c>"quantum"</c>, <c>"ringtail"</c>... see <see cref="Forge.Archetypes"/>).</summary>
        public AnatomySpec Archetype(string id, float intensity = 1f)
        {
            var a = Forge.Archetypes.Get(id);
            if (a == null) { ForgeLog.Warn($"AnatomySpec: unknown archetype '{id}'"); return this; }
            return Archetype(a, intensity);
        }

        public AnatomySpec Voices(Voices.Set set, int priority = 0) { Voice = set; VoicePriority = priority; return Touch(); }
        public AnatomySpec NoLargoInheritance() { PassToLargos = false; return Touch(); }

        AnatomySpec Touch() { Version++; Anatomy.Invalidate(); return this; }

        public override string ToString() =>
            $"layers [{string.Join(", ", Layers.ConvertAll(l => l.Name))}] parts [{string.Join(", ", Parts.ConvertAll(p => p.Name))}] " +
            $"archetypes [{string.Join(", ", Archetypes.ConvertAll(a => $"{a.Archetype.Id} x{a.Intensity:0.##}"))}] voice {(Voice != null ? "custom p" + VoicePriority : "-")}";
    }

    /// <summary>
    /// Anatomy registry and spawn-time application. Every slime that wakes up (SlimeEat.Awake) gets the resolved spec
    /// of its definition on the next frame: layers, parts and archetype runtimes (driven by <see cref="ActorRuntime"/>).
    /// </summary>
    public static class Anatomy
    {
        static readonly Dictionary<IntPtr, AnatomySpec> _declared = new();
        static readonly Dictionary<IntPtr, AnatomySpec> _resolved = new();
        static readonly List<GameObject> _queue = new();

        /// <summary>Slimes the anatomy was applied to.</summary>
        public static int Applied { get; private set; }
        /// <summary>Definitions with a declared spec.</summary>
        public static int Declared => _declared.Count;

        /// <summary>Declared spec of <paramref name="def"/> (created on first use).</summary>
        public static AnatomySpec For(SlimeDefinition def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!_declared.TryGetValue(def.Pointer, out var s)) { s = new AnatomySpec { Def = def }; _declared[def.Pointer] = s; Invalidate(); }
            return s;
        }

        /// <summary>Declared spec of the slime named <paramref name="slimeName"/> (e.g. "Pink", "Batty"); null when not found.</summary>
        public static AnatomySpec For(string slimeName)
        {
            var d = Idents.Find(slimeName)?.TryCast<SlimeDefinition>();
            if (d == null) { ForgeLog.Warn($"Anatomy.For: no slime '{slimeName}'"); return null; }
            return For(d);
        }

        internal static void Invalidate() => _resolved.Clear();

        /// <summary>Effective spec: the declared one, for largos merged from the bases (plus the largo's own). Null = nothing.</summary>
        public static AnatomySpec Resolve(SlimeDefinition def)
        {
            if (def == null) return null;
            if (_resolved.TryGetValue(def.Pointer, out var r)) return r;
            _declared.TryGetValue(def.Pointer, out var own);
            r = own;
            if (def.IsLargo && def.BaseSlimes != null)
            {
                var bases = new List<AnatomySpec>();
                foreach (var b in def.BaseSlimes)
                    if (b != null && _declared.TryGetValue(b.Pointer, out var bs) && bs.PassToLargos && !bs.IsEmpty) bases.Add(bs);
                if (bases.Count > 0)
                {
                    r = LargoEngine.Merge(def, bases);
                    if (own != null) Append(r, own);
                }
            }
            if (r != null && r.IsEmpty) r = null;
            _resolved[def.Pointer] = r;
            return r;
        }

        static void Append(AnatomySpec to, AnatomySpec from)
        {
            to.Layers.AddRange(from.Layers);
            to.Parts.AddRange(from.Parts);
            foreach (var a in from.Archetypes) { to.Archetypes.RemoveAll(x => x.Archetype.Id == a.Archetype.Id); to.Archetypes.Add(a); }
            if (from.Voice != null) { to.Voice = from.Voice; to.VoicePriority = from.VoicePriority; }
        }

        sealed class Marker : IActorLogic { public void Start(GameObject go) { } public void Tick(float dt) { } public void Stop() { } }

        /// <summary>Applies the resolved spec to a spawned slime now (normally automatic). Returns false when there is nothing to do.</summary>
        public static bool Apply(GameObject slime)
        {
            if (slime == null || ActorRuntime.Get<Marker>(slime) != null) return false;
            var actor = new SlimeActor(slime);
            var spec = Resolve(actor.Def);
            if (spec == null) return false;
            ActorRuntime.Add(actor.Go, new Marker());
            bool largo = actor.IsLargo;
            foreach (var l in spec.Layers)
            {
                if (largo && !l.OnLargos) continue;
                var mat = Safe.Run("anatomy layer " + l.Name, () => l.Material?.Invoke());
                if (mat != null) Safe.Run("anatomy layer " + l.Name, () => MeshKit.AddLayer(actor.Go, l.Name, mat, l.Inflate, l.Facets));
            }
            foreach (var p in spec.Parts)
            {
                if (largo && !p.OnLargos) continue;
                var part = Safe.Run("anatomy part " + p.Name, () => p.Attach?.Invoke(actor.Go));
                if (part != null && largo && !Mathf.Approximately(p.LargoScale, 1f)) part.transform.localScale *= p.LargoScale;
            }
            foreach (var a in spec.Archetypes)
            {
                var rt = Safe.Run("archetype " + a.Archetype.Id, () => a.Archetype.Create(actor, a.Intensity));
                if (rt != null) ActorRuntime.Add(actor.Go, rt);
            }
            Applied++;
            return true;
        }

        internal static void OnSlimeAwake(SlimeEat eat)
        {
            if (_declared.Count == 0 || eat == null) return;
            if (_queue.Count == 0) Scheduler.Frames(1, Flush, "anatomy");
            _queue.Add(eat.gameObject);
        }

        static void Flush()
        {
            var q = _queue.ToArray();
            _queue.Clear();
            foreach (var go in q) if (go != null) Safe.Run("anatomy", () => Apply(go));
        }
    }
}
