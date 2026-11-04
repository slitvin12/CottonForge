using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using ET = Il2Cpp.SlimeAppearanceElement.ElementType;
using MI = Il2Cpp.SlimeAppearanceElement.MatInheritType;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// What the vanilla game does with its own slimes, read from the game data at runtime (nothing hard-coded): the groups
    /// every vanilla largo / base slime belongs to, the groups that hold every largo of one parent, the components every
    /// vanilla largo prefab has, whose body and face each vanilla slime gives its largos, whose palette they take, what they do
    /// with the parents' parts (kept, dropped, largo versions), which groups plorts are in, how largo zones and diets are set.
    /// <see cref="LargoBlend"/>, <see cref="LargoParity"/> and <see cref="LargoAudit"/> use it so generated largos follow the
    /// same rules as vanilla ones (dev command <c>largoaudit learn</c> shows everything).
    /// </summary>
    public static class VanillaSlimes
    {
        /// <summary>How often a slime gives its largos the body / the face (vanilla largos only).</summary>
        public sealed class Share
        {
            public int Own, Total;
            public float Value => Total > 0 ? Own / (float)Total : 0.5f;
            /// <summary>Enough vanilla largos and a clear preference (at least 3 of 4 either way).</summary>
            public bool Confident => Total >= MinSamples && (Value >= 0.75f || Value <= 0.25f);
            public override string ToString() => Own + "/" + Total;
        }

        /// <summary>Minimum number of vanilla largos before a learned rule counts.</summary>
        public static int MinSamples = 2;
        /// <summary>A group counts as "every largo" / "every base slime" when it holds this share of the vanilla ones.</summary>
        public static float UniversalShare = 0.9f;

        static HashSet<IntPtr> _snapshot;
        static bool _learned;

        /// <summary>Vanilla largos (two base slimes, prefab, appearance).</summary>
        public static readonly List<SlimeDefinition> Largos = new();
        /// <summary>Vanilla members of BaseSlimeGroup.</summary>
        public static readonly List<SlimeDefinition> Bases = new();
        /// <summary>Groups holding (nearly) every vanilla largo directly.</summary>
        public static readonly List<IdentifiableTypeGroup> LargoGroups = new();
        /// <summary>Groups holding (nearly) every vanilla base slime directly.</summary>
        public static readonly List<IdentifiableTypeGroup> BaseGroups = new();
        /// <summary>Root MonoBehaviour types (IL2CPP names) of (nearly) every vanilla largo prefab.</summary>
        public static readonly HashSet<string> CoreComponents = new();
        /// <summary>Share of vanilla largos whose NativeZones / showForZones are the union of their parents' (-1 = unknown).</summary>
        public static float ZonesUnion = -1f, ShowZonesUnion = -1f;
        /// <summary>FavoriteProductionCount of (nearly) every vanilla largo, -1 = no common value.</summary>
        public static int FavoriteProduction = -1;
        /// <summary>Animation overrides (idle, wing flap, bite) of vanilla largos that are the body parent's, else the other parent's.</summary>
        public static Share Animations = new();
        /// <summary>Applies the parents' animation overrides: no counter-evidence in the vanilla largos.</summary>
        public static bool AnimationsFromParents { get { Learn(); return Animations.Total < MinSamples || Animations.Value >= 0.75f; } }
        static int _dynLargos, _dynOwn, _dynParent, _dynOther;
        /// <summary>Groups holding (nearly) every plort of the vanilla largoable base slimes directly, and the groups their diets eat plorts from.</summary>
        public static readonly List<IdentifiableTypeGroup> PlortGroups = new();
        /// <summary>Palette distance (OKLab, 0 = same) up to which a vanilla largo has a parent's colours.</summary>
        public static float ColorMatch = 0.03f;
        /// <summary>Vanilla largos coloured like one parent / like neither.</summary>
        public static int ColorExact, ColorOther;
        /// <summary>Vanilla largos take the palette of one parent (not a mix): the engine does the same.</summary>
        public static bool OneParentColors { get { Learn(); return ColorExact >= 5 && ColorExact >= 0.75f * (ColorExact + ColorOther); } }
        /// <summary>Repaintable parts of vanilla largos that keep the parent's own colours (Own) or take the largo palette (the rest).</summary>
        public static Share PartColors = new();
        /// <summary>Minimum number of vanilla largos before a part rule counts.</summary>
        public static int PartMinSamples = 3;

        /// <summary>What the vanilla largos do with one part (element name) of a parent.</summary>
        public sealed class PartRule
        {
            public string Name, SubstituteName;
            /// <summary>Kept (the element itself or its largo version) / seen; for a body: kept by the largos it is the body of.</summary>
            public readonly Share Kept = new();
            /// <summary>Kept when the other parent has a part of that type.</summary>
            public readonly Dictionary<ET, Share> With = new();
            /// <summary>Part types of the other parent next to which vanilla largos (nearly) never keep it (not explained by another type).</summary>
            public readonly HashSet<ET> DropNext = new();
            internal readonly Dictionary<ET, HashSet<int>> Seen = new();
            /// <summary>Kept against a part of the same type of the other parent (only one of them was kept).</summary>
            public readonly Share Clash = new();
            /// <summary>Largo version of the part (FlutterWings -> FlutterWingsLargo, a body -> Saber Body), taken from a vanilla largo.</summary>
            public SlimeAppearanceStructure Substitute;
            public int SubstituteCount;
            internal readonly Dictionary<string, (int n, SlimeAppearanceStructure st)> Candidates = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Vanilla largos (nearly) never keep it, or never next to a part of one of the <paramref name="other"/> types.</summary>
            public bool Dropped(IEnumerable<ET> other, out string why)
            {
                why = null;
                if (Kept.Total >= PartMinSamples && Kept.Value <= 0.2f) { why = $"{Name} dropped (vanilla {Kept})"; return true; }
                if (other != null)
                    foreach (var t in other)
                        if (DropNext.Contains(t)) { why = $"{Name} dropped next to {t} (vanilla {With[t]})"; return true; }
                return false;
            }

            public override string ToString()
            {
                var drops = DropNext.Select(t => t + " " + With[t]).ToList();
                return $"{Name}: kept {Kept}" + (Clash.Total > 0 ? $", clashes won {Clash}" : "") +
                       (Substitute != null ? $", largo version {SubstituteName} ({SubstituteCount})" : "") +
                       (Kept.Total >= PartMinSamples && Kept.Value <= 0.2f ? ", DROPPED" : "") + (drops.Count > 0 ? ", dropped next to " + string.Join(", ", drops) : "");
            }
        }
        static readonly Dictionary<string, PartRule> _parts = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Share> _colors = new(StringComparer.OrdinalIgnoreCase);

        static readonly Dictionary<string, List<IdentifiableTypeGroup>> _parentGroups = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, HashSet<string>> _parentComponents = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Share> _body = new(StringComparer.OrdinalIgnoreCase), _face = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, List<SlimeDefinition>> _byParent = new(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<IntPtr> _largoableOnly = new();

        public static bool HasSnapshot => _snapshot != null;
        public static bool Learned => _learned;

        // ------------------------------------------------------------------ vanilla or modded

        /// <summary>Remembers every SlimeDefinition that exists before mods create theirs (AutoSaveDirector.Awake prefix).</summary>
        internal static void Snapshot()
        {
            if (_snapshot != null) return;
            Safe.Run("vanilla slime snapshot", () =>
            {
                var set = new HashSet<IntPtr>();
                foreach (var d in Resources.FindObjectsOfTypeAll<SlimeDefinition>()) if (d != null) set.Add(d.Pointer);
                if (set.Count == 0) return;
                _snapshot = set;
                ForgeLog.Debug($"[vanilla] {set.Count} slime definitions before mods");
            });
        }

        /// <summary>
        /// True for a slime of the game itself: it existed before mods ran (snapshot), else (no snapshot) it is not a Cotton
        /// clone (HideAndDontSave) and has no "Modded" reference id (Starlight / Prism).
        /// </summary>
        public static bool IsVanilla(SlimeDefinition d)
        {
            if (d == null) return false;
            if (_snapshot != null) return _snapshot.Contains(d.Pointer);
            return (d.hideFlags & HideFlags.HideInHierarchy) == 0 && (d.referenceId == null || !d.referenceId.Contains("Modded"));
        }

        // ------------------------------------------------------------------ learned data

        /// <summary>Groups that hold every vanilla largo of <paramref name="slime"/> (and only largos with such a parent).</summary>
        public static IReadOnlyList<IdentifiableTypeGroup> ParentGroups(string slime)
        {
            Learn();
            return slime != null && _parentGroups.TryGetValue(slime, out var l) ? l : (IReadOnlyList<IdentifiableTypeGroup>)Array.Empty<IdentifiableTypeGroup>();
        }

        /// <summary>Components every vanilla largo of <paramref name="slime"/> has (and <paramref name="slime"/> itself), but most other largos do not.</summary>
        public static IReadOnlyCollection<string> ParentComponents(string slime)
        {
            Learn();
            return slime != null && _parentComponents.TryGetValue(slime, out var s) ? s : (IReadOnlyCollection<string>)Array.Empty<string>();
        }

        /// <summary>Vanilla largos with <paramref name="slime"/> as a parent.</summary>
        public static IReadOnlyList<SlimeDefinition> LargosOf(string slime)
        {
            Learn();
            return slime != null && _byParent.TryGetValue(slime, out var l) ? l : (IReadOnlyList<SlimeDefinition>)Array.Empty<SlimeDefinition>();
        }

        /// <summary>A base group without any of the vanilla slimes that cannot become largos (Gold, Lucky ...): not for a slime with CanLargofy off.</summary>
        public static bool IsLargoableOnly(IdentifiableTypeGroup g) { Learn(); return g != null && _largoableOnly.Contains(g.Pointer); }

        public static Share BodyShare(string slime) { Learn(); return slime != null && _body.TryGetValue(slime, out var s) ? s : null; }
        public static Share FaceShare(string slime) { Learn(); return slime != null && _face.TryGetValue(slime, out var s) ? s : null; }
        /// <summary>How often vanilla largos take the palette of <paramref name="slime"/> (own / total).</summary>
        public static Share ColorShare(string slime) { Learn(); return slime != null && _colors.TryGetValue(slime, out var s) ? s : null; }
        /// <summary>What vanilla largos do with a part (element name), null = never seen.</summary>
        public static PartRule PartOf(string element) { Learn(); return element != null && _parts.TryGetValue(element, out var r) ? r : null; }
        public static IReadOnlyCollection<PartRule> PartRules { get { Learn(); return _parts.Values; } }

        /// <summary>Part identity of vanilla largos: 1 = parts keep the parent's colours, 0 = repainted, null = no clear rule.</summary>
        public static float? LearnedPartIdentity
        {
            get { Learn(); return PartColors.Total < 5 ? null : PartColors.Value >= 0.75f ? 1f : PartColors.Value <= 0.25f ? 0f : (float?)null; }
        }

        /// <summary>
        /// Colour rank of a slime: the share of its vanilla largos that have its palette; a modded slime gets one from its
        /// colour <paramref name="weight"/> (1 = 0.5, every +1 = +0.5, clamped to 0.05 .. 0.95).
        /// </summary>
        public static float ColorRank(SlimeDefinition d, float weight, out bool learned)
        {
            var s = d != null ? ColorShare(d.name) : null;
            learned = s != null && s.Total >= MinSamples;
            return learned ? s.Value : Mathf.Clamp(0.5f + (weight - 1f) * 0.5f, 0.05f, 0.95f);
        }

        /// <summary>Whose palette a largo of <paramref name="a"/> and <paramref name="b"/> takes (the higher colour rank), -1 = tie.</summary>
        public static int PreferColors(SlimeDefinition a, SlimeDefinition b, float wa, float wb, out string why)
        {
            float ra = ColorRank(a, wa, out bool ka), rb = ColorRank(b, wb, out bool kb);
            string f(float x) => x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            why = $"colours: vanilla rank {a?.name} {f(ra)}{(ka ? "" : " (weight)")} / {b?.name} {f(rb)}{(kb ? "" : " (weight)")}";
            return Math.Abs(ra - rb) < 0.01f ? -1 : ra > rb ? 0 : 1;
        }

        /// <summary>
        /// Which of two slimes gives the body according to the vanilla largos (-1 = no clear rule). Both known: a clear
        /// difference decides. <paramref name="tie"/> (the heuristics found no difference): one known slime may decide.
        /// </summary>
        public static int PreferBody(SlimeDefinition a, SlimeDefinition b, bool tie, out string why) => Prefer(_body, a, b, tie, "body", out why);
        /// <summary>Same for the face.</summary>
        public static int PreferFace(SlimeDefinition a, SlimeDefinition b, bool tie, out string why) => Prefer(_face, a, b, tie, "face", out why);

        static int Prefer(Dictionary<string, Share> map, SlimeDefinition a, SlimeDefinition b, bool tie, string what, out string why)
        {
            why = null;
            if (a == null || b == null) return -1;
            Learn();
            map.TryGetValue(a.name, out var sa); map.TryGetValue(b.name, out var sb);
            bool ca = sa != null && sa.Confident, cb = sb != null && sb.Confident;
            bool ka = sa != null && sa.Total >= MinSamples, kb = sb != null && sb.Total >= MinSamples;
            int r = -1;
            if (ka && kb) { if (Math.Abs(sa.Value - sb.Value) >= 0.2f) r = sa.Value > sb.Value ? 0 : 1; }
            else if (tie && (ca || cb))
            {
                var s = ca ? sa : sb; int side = ca ? 0 : 1;
                r = s.Value >= 0.75f ? side : 1 - side;
            }
            if (r >= 0) why = $"{what}: vanilla largos ({a.name} {sa?.ToString() ?? "-"}, {b.name} {sb?.ToString() ?? "-"})";
            return r;
        }

        // ------------------------------------------------------------------ helpers

        static bool Same(Il2CppObjectBase a, Il2CppObjectBase b) => (object)a != null && (object)b != null && a.Pointer == b.Pointer;
        static int Pick(bool a, bool b) => a == b ? -1 : a ? 0 : 1;
        static float Dist(Color a, Color b) => (ColorMix.ToOklab(a) - ColorMix.ToOklab(b)).magnitude;
        /// <summary>Palette distance (OKLab, average of top / middle / bottom; 0 = same).</summary>
        public static float Dist(LargoPalette a, LargoPalette b) => (Dist(a.Top, b.Top) + Dist(a.Middle, b.Middle) + Dist(a.Bottom, b.Bottom)) / 3f;

        static IEnumerable<SlimeAppearanceStructure> Structs(SlimeAppearance app)
        {
            var arr = app != null ? app.Structures : null;
            if (arr == null) yield break;
            for (int i = 0; i < arr.Length; i++) { var s = arr[i]; if (s != null && s.Element != null) yield return s; }
        }
        static string En(SlimeAppearanceStructure s) => s?.Element?.Name ?? "";
        static bool IsPart(SlimeAppearanceStructure s) => s?.Element != null && s.Element.Type != ET.BODY && s.Element.Type != ET.FACE;

        internal static SlimeAppearance App(SlimeDefinition d)
        {
            var arr = d != null ? d.AppearancesDefault : null;
            return arr != null && arr.Length > 0 ? arr[0] : null;
        }

        static Material Main(SlimeAppearanceStructure st)
        {
            var m = st != null ? st.DefaultMaterials : null;
            return m != null && m.Length > 0 ? m[0] : null;
        }

        static string Shader(Material m) { var s = m != null ? m.shader : null; return s != null ? s.name ?? "" : ""; }

        static HashSet<string> Keywords(Material m)
        {
            var set = new HashSet<string>();
            if (m == null) return set;
            try { var k = m.shaderKeywords; if (k != null) for (int i = 0; i < k.Length; i++) set.Add(k[i]); } catch { }
            return set;
        }

        static float Jaccard(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 && b.Count == 0) return 1f;
            int inter = a.Count(b.Contains);
            return inter / (float)(a.Count + b.Count - inter);
        }

        static bool NameHas(string element, string slime) =>
            !string.IsNullOrEmpty(element) && !string.IsNullOrEmpty(slime) && element.IndexOf(slime, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Which parent (0 / 1) gave largo appearance <paramref name="l"/> its body, -1 = cannot tell.</summary>
        public static int BodyOwner(SlimeAppearance l, SlimeAppearance a, SlimeAppearance b, SlimeDefinition da = null, SlimeDefinition db = null)
        {
            var sl = LargoBlend.BodyOf(l); var sa = LargoBlend.BodyOf(a); var sb = LargoBlend.BodyOf(b);
            if (sl == null || sa == null || sb == null) return -1;
            int r = Pick(Same(sl.Element, sa.Element), Same(sl.Element, sb.Element));
            if (r >= 0) return r;
            string nl = sl.Element.Name ?? "", na = sa.Element.Name ?? "", nb = sb.Element.Name ?? "";
            r = Pick(nl == na, nl == nb);
            if (r >= 0) return r;
            if (da != null && db != null) { r = Pick(NameHas(nl, da.name), NameHas(nl, db.name)); if (r >= 0) return r; }
            Material ml = Main(sl), ma = Main(sa), mb = Main(sb);
            r = Pick(Shader(ml) == Shader(ma), Shader(ml) == Shader(mb));
            if (r >= 0) return r;
            float ja = Jaccard(Keywords(ml), Keywords(ma)), jb = Jaccard(Keywords(ml), Keywords(mb));
            return Math.Abs(ja - jb) >= 0.2f ? (ja > jb ? 0 : 1) : -1;
        }

        /// <summary>Which parent (0 / 1) gave largo appearance <paramref name="l"/> its face, -1 = cannot tell.</summary>
        public static int FaceOwner(SlimeAppearance l, SlimeAppearance a, SlimeAppearance b)
        {
            if (l == null || a == null || b == null) return -1;
            var fl = l._face; var fa = a._face; var fb = b._face;
            if (fl == null) return -1;
            int r = Pick(Same(fl, fa), Same(fl, fb));
            if (r >= 0) return r;
            string n(SlimeFace f) => f != null ? f.name ?? "" : "\u0001";
            return Pick(n(fl) == n(fa), n(fl) == n(fb));
        }

        internal static HashSet<IntPtr> Set<T>(Il2CppReferenceArray<T> arr) where T : Il2CppObjectBase
        {
            var s = new HashSet<IntPtr>();
            if (arr != null) for (int i = 0; i < arr.Length; i++) if (arr[i] != null) s.Add(arr[i].Pointer);
            return s;
        }

        internal static HashSet<string> RootComponents(GameObject go)
        {
            var s = new HashSet<string>();
            if (go == null) return s;
            foreach (var c in go.GetComponents<MonoBehaviour>())
                if (c != null) Safe.Quiet(() => s.Add(c.GetIl2CppType().Name));
            return s;
        }

        static void Count(Dictionary<string, Share> map, string slime, int owner, int side)
        {
            if (owner < 0 || slime == null) return;
            if (!map.TryGetValue(slime, out var s)) map[slime] = s = new Share();
            s.Total++;
            if (owner == side) s.Own++;
        }

        static PartRule Rule(string en)
        {
            if (!_parts.TryGetValue(en, out var r)) _parts[en] = r = new PartRule { Name = en };
            return r;
        }

        static void Candidate(PartRule r, SlimeAppearanceStructure st)
        {
            var n = En(st);
            r.Candidates[n] = r.Candidates.TryGetValue(n, out var c) ? (c.n + 1, c.st) : (1, st);
        }

        // what one vanilla largo did with its parents' parts: kept, dropped, replaced by a largo version; clashes; part colours
        static void LearnParts(SlimeAppearance la, SlimeAppearance[] par, int bodyOwner)
        {
            var mine = Structs(la).ToList();
            var names = new HashSet<string>(mine.Select(En), StringComparer.OrdinalIgnoreCase);
            var parentNames = new HashSet<string>(par.SelectMany(Structs).Select(En), StringComparer.OrdinalIgnoreCase);
            var fresh = mine.Where(x => IsPart(x) && !parentNames.Contains(En(x))).ToList();
            var parts = new[] { Structs(par[0]).Where(IsPart).ToList(), Structs(par[1]).Where(IsPart).ToList() };
            var kept = new HashSet<IntPtr>();
            for (int s = 0; s < 2; s++)
            {
                var other = new HashSet<ET>(parts[1 - s].Select(x => x.Element.Type));
                foreach (var st in parts[s])
                {
                    var en = En(st);
                    if (en.Length == 0) continue;
                    var r = Rule(en);
                    bool k = names.Contains(en);
                    if (!k)
                    {
                        var sub = fresh.Where(f => En(f).IndexOf(en, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(f => En(f).Length).FirstOrDefault();
                        if (sub != null) { k = true; Candidate(r, sub); }
                    }
                    r.Kept.Total++;
                    if (k) { r.Kept.Own++; kept.Add(st.Pointer); }
                    foreach (var t in other)
                    {
                        if (!r.With.TryGetValue(t, out var w)) r.With[t] = w = new Share();
                        if (!r.Seen.TryGetValue(t, out var ids)) r.Seen[t] = ids = new HashSet<int>();
                        ids.Add(r.Kept.Total);
                        w.Total++;
                        if (k) w.Own++;
                    }
                }
            }
            foreach (var t in parts[0].Select(x => x.Element.Type).Intersect(parts[1].Select(x => x.Element.Type)).ToList())
            {
                if (!LargoBlend.UniqueSlots.Contains(t)) continue;
                bool k0 = parts[0].Any(x => x.Element.Type == t && kept.Contains(x.Pointer)), k1 = parts[1].Any(x => x.Element.Type == t && kept.Contains(x.Pointer));
                if (k0 == k1) continue;
                for (int s = 0; s < 2; s++)
                    foreach (var x in parts[s].Where(x => x.Element.Type == t))
                    {
                        var r = Rule(En(x));
                        r.Clash.Total++;
                        if (s == 0 ? k0 : k1) r.Clash.Own++;
                    }
            }
            if (bodyOwner >= 0)
            {
                var lb = LargoBlend.BodyOf(la); var ob = LargoBlend.BodyOf(par[bodyOwner]);
                if (lb != null && ob != null && En(ob).Length > 0)
                {
                    var r = Rule(En(ob));
                    r.Kept.Total++;
                    if (string.Equals(En(lb), En(ob), StringComparison.OrdinalIgnoreCase)) r.Kept.Own++;
                    else if (!parentNames.Contains(En(lb))) Candidate(r, lb);
                }
            }
            // colours of repaintable parts the largo kept: the parent's own or the largo palette
            var lpal = LargoBlend.PaletteOf(la);
            for (int s = 0; s < 2; s++)
            {
                var pbody = Main(LargoBlend.BodyOf(par[s]));
                foreach (var ps in parts[s])
                {
                    var inh = ps.Element.MaterialInheritance;
                    if (inh == MI.NONE) continue;
                    var ls = mine.FirstOrDefault(x => string.Equals(En(x), En(ps), StringComparison.OrdinalIgnoreCase));
                    if (ls == null) continue;
                    var pm = LargoBlend.Mats(ps); var lm = LargoBlend.Mats(ls);
                    for (int i = 0; i < pm.Count && i < lm.Count; i++)
                    {
                        var m = pm[i]; var x = lm[i];
                        if (m == null || x == null || LargoBlend.IsAura(m) || !LargoBlend.HasPalette(m) || !LargoBlend.HasPalette(x)) continue;
                        if (inh == MI.BODY && i == 0 && Same(m, pbody)) continue;
                        var mp = LargoPalette.Of(m, lpal); var xp = LargoPalette.Of(x, lpal);
                        if (Same(x, m) || Dist(xp, mp) <= ColorMatch) { PartColors.Total++; PartColors.Own++; }
                        else if (Dist(xp, lpal) <= ColorMatch) PartColors.Total++;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ learning

        /// <summary>Forgets and reads the vanilla data again.</summary>
        public static void Relearn() { _learned = false; Learn(); }

        /// <summary>Reads the vanilla data (once, on first use; needs the game's slime definitions).</summary>
        public static void Learn()
        {
            if (_learned) return;
            var defs = Library.gameContext != null ? Library.slimeDefinitions?.Slimes : null;
            if (defs == null) return;
            _learned = true;
            Safe.Run("learn vanilla slimes", () => LearnCore(defs));
        }

        static void LearnCore(IEnumerable<SlimeDefinition> defs)
        {
            Largos.Clear(); Bases.Clear(); LargoGroups.Clear(); BaseGroups.Clear(); CoreComponents.Clear();
            _parentGroups.Clear(); _parentComponents.Clear(); _body.Clear(); _face.Clear(); _byParent.Clear(); _largoableOnly.Clear();
            Animations = new Share(); _dynLargos = _dynOwn = _dynParent = _dynOther = 0;
            PlortGroups.Clear(); _parts.Clear(); _colors.Clear(); ColorExact = ColorOther = 0; PartColors = new Share();
            var seen = new HashSet<IntPtr>();
            foreach (var d in defs)
            {
                if (d == null || !seen.Add(d.Pointer) || !IsVanilla(d)) continue;
                if (d.IsLargo)
                {
                    var b = d.BaseSlimes;
                    if (b != null && b.Length == 2 && b[0] != null && b[1] != null && d.prefab != null && App(d) != null) Largos.Add(d);
                }
                else if (Idents.InGroup(Library.baseSlimes, d)) Bases.Add(d);
            }
            foreach (var l in Largos)
                foreach (var p in l.BaseSlimes)
                {
                    if (!_byParent.TryGetValue(p.name, out var list)) _byParent[p.name] = list = new List<SlimeDefinition>();
                    list.Add(l);
                }

            // plorts of the largoable base slimes and the groups their diets eat plorts from
            var plorts = new HashSet<IntPtr>();
            var edible = new HashSet<IntPtr>();
            foreach (var bs in Bases)
                Safe.Quiet(() =>
                {
                    var di = bs.Diet;
                    if (di == null || !bs.CanLargofy) return;
                    var pr = di.ProduceIdents;
                    if (pr != null) for (int i = 0; i < pr.Length; i++) if (pr[i] != null) plorts.Add(pr[i].Pointer);
                    if (di.EdiblePlortIdentifiableTypeGroup != null) edible.Add(di.EdiblePlortIdentifiableTypeGroup.Pointer);
                });

            // groups: direct members only (a largo joins the group itself, parent groups follow through sub-groups)
            var gseen = new HashSet<IntPtr>();
            foreach (var g in Resources.FindObjectsOfTypeAll<IdentifiableTypeGroup>())
            {
                if (g == null || !gseen.Add(g.Pointer)) continue;
                var mt = g._memberTypes;
                if (mt == null) continue;
                var members = new HashSet<IntPtr>();
                for (int i = 0; i < mt.Count; i++) { var t = mt[i]; if (t != null) members.Add(t.Pointer); }
                var inside = Largos.Where(l => members.Contains(l.Pointer)).ToList();
                if (Largos.Count >= 5 && inside.Count >= UniversalShare * Largos.Count) LargoGroups.Add(g);
                else if (inside.Count >= MinSamples)
                {
                    // parent-driven: every largo of a "trigger" parent is in it and every largo in it has a trigger parent
                    var triggers = _byParent.Where(kv => kv.Value.Count >= MinSamples && kv.Value.All(l => members.Contains(l.Pointer))).Select(kv => kv.Key).ToList();
                    var tset = new HashSet<string>(triggers, StringComparer.OrdinalIgnoreCase);
                    if (triggers.Count > 0 && inside.All(l => l.BaseSlimes.Any(p => tset.Contains(p.name))))
                        foreach (var t in triggers)
                        {
                            if (!_parentGroups.TryGetValue(t, out var gl)) _parentGroups[t] = gl = new List<IdentifiableTypeGroup>();
                            gl.Add(g);
                        }
                }
                if (plorts.Count >= 5 && (edible.Contains(g.Pointer) || plorts.Count(members.Contains) >= UniversalShare * plorts.Count)) PlortGroups.Add(g);
                if (Bases.Count >= 5 && Bases.Count(b => members.Contains(b.Pointer)) >= UniversalShare * Bases.Count)
                {
                    BaseGroups.Add(g);
                    if (Bases.Any(b => !b.CanLargofy) && Bases.Where(b => members.Contains(b.Pointer)).All(b => b.CanLargofy)) _largoableOnly.Add(g.Pointer);
                }
            }

            // components
            var compCount = new Dictionary<string, int>();
            var compsOf = new Dictionary<IntPtr, HashSet<string>>();
            foreach (var l in Largos)
            {
                var set = RootComponents(l.prefab);
                compsOf[l.Pointer] = set;
                foreach (var n in set) compCount[n] = compCount.TryGetValue(n, out var k) ? k + 1 : 1;
            }
            foreach (var kv in compCount) if (Largos.Count >= 5 && kv.Value >= UniversalShare * Largos.Count) CoreComponents.Add(kv.Key);
            foreach (var kv in _byParent)
            {
                if (kv.Value.Count < MinSamples) continue;
                var parent = kv.Value[0].BaseSlimes.FirstOrDefault(p => string.Equals(p.name, kv.Key, StringComparison.OrdinalIgnoreCase));
                var common = new HashSet<string>(compsOf[kv.Value[0].Pointer]);
                foreach (var l in kv.Value.Skip(1)) common.IntersectWith(compsOf[l.Pointer]);
                common.IntersectWith(RootComponents(parent != null ? parent.prefab : null));
                common.RemoveWhere(n => CoreComponents.Contains(n) || compCount[n] >= 0.5f * Largos.Count);
                if (common.Count > 0) _parentComponents[kv.Key] = common;
            }

            // body / face owners, zones, diet
            int zl = 0, zn = 0, sl = 0, sn = 0;
            var fav = new Dictionary<int, int>();
            foreach (var l in Largos)
            {
                var a = l.BaseSlimes[0]; var b = l.BaseSlimes[1];
                var la = App(l); var aa = App(a); var ba = App(b);
                int bo = BodyOwner(la, aa, ba, a, b);
                Count(_body, a.name, bo, 0); Count(_body, b.name, bo, 1);
                int fo = FaceOwner(la, aa, ba);
                Count(_face, a.name, fo, 0); Count(_face, b.name, fo, 1);
                Safe.Quiet(() =>
                {
                    var pl = LargoBlend.PaletteOf(la);
                    float da = Dist(pl, LargoBlend.PaletteOf(aa)), db = Dist(pl, LargoBlend.PaletteOf(ba));
                    if (Math.Min(da, db) > ColorMatch) { ColorOther++; return; }
                    if (Math.Abs(da - db) < 0.01f) return;   // both parents look alike
                    ColorExact++;
                    int w = da < db ? 0 : 1;
                    Count(_colors, a.name, w, 0); Count(_colors, b.name, w, 1);
                });
                Safe.Quiet(() => LearnParts(la, new[] { aa, ba }, bo));
                Safe.Quiet(() =>
                {
                    SlimeAppearance own = bo == 1 ? ba : aa, oth = bo == 1 ? aa : ba;
                    for (int i = 0; i < LargoParity.ClipNames.Length; i++)
                    {
                        var cl = LargoParity.Clip(la, i);
                        var e = LargoParity.Clip(own, i) ?? LargoParity.Clip(oth, i);
                        if (cl == null && e == null) continue;
                        Animations.Total++;
                        if (cl != null && e != null && cl.Pointer == e.Pointer) Animations.Own++;
                    }
                });
                Safe.Quiet(() =>
                {
                    var dyn = l.AppearancesDynamic;
                    if (dyn == null || dyn.Count == 0) return;
                    _dynLargos++;
                    var mine = Set(l.AppearancesDefault);
                    if (l.RadiantLargo0 != null) mine.Add(l.RadiantLargo0.Pointer);
                    if (l.RadiantLargo1 != null) mine.Add(l.RadiantLargo1.Pointer);
                    var par = Set(a.AppearancesDefault); par.UnionWith(Set(b.AppearancesDefault));
                    for (int i = 0; i < dyn.Count; i++)
                    {
                        var x = dyn[i];
                        if (x == null) continue;
                        if (mine.Contains(x.Pointer)) _dynOwn++; else if (par.Contains(x.Pointer)) _dynParent++; else _dynOther++;
                    }
                });
                Safe.Quiet(() =>
                {
                    var u = Set(a.NativeZones); u.UnionWith(Set(b.NativeZones));
                    var own = Set(l.NativeZones);
                    if (u.Count > 0 || own.Count > 0) { zn++; if (own.SetEquals(u)) zl++; }
                });
                Safe.Quiet(() =>
                {
                    var u = Set(a.showForZones); u.UnionWith(Set(b.showForZones));
                    var own = Set(l.showForZones);
                    if (u.Count > 0 || own.Count > 0) { sn++; if (own.SetEquals(u)) sl++; }
                });
                Safe.Quiet(() => { if (l.Diet != null) { int f = l.Diet.FavoriteProductionCount; fav[f] = fav.TryGetValue(f, out var k) ? k + 1 : 1; } });
            }
            foreach (var r in _parts.Values)
            {
                // drop next to a type only when no other drop type explains the same largos (Ringtail Ears: ears, not tails)
                var cand = r.With.Where(kv => kv.Value.Total >= PartMinSamples && kv.Value.Value <= 0.2f).Select(kv => kv.Key).ToList();
                foreach (var t in cand)
                {
                    var ts = r.Seen[t];
                    if (!cand.Any(u => u != t && ts.IsSubsetOf(r.Seen[u]) && (r.Seen[u].Count > ts.Count || (int)u < (int)t))) r.DropNext.Add(t);
                }
                if (r.Candidates.Count == 0) continue;
                var top = r.Candidates.OrderByDescending(kv => kv.Value.n).ThenBy(kv => kv.Key).First();
                if (top.Value.n >= MinSamples && top.Value.n >= 0.5f * r.Kept.Total) { r.Substitute = top.Value.st; r.SubstituteName = top.Key; r.SubstituteCount = top.Value.n; }
            }
            ZonesUnion = zn > 0 ? zl / (float)zn : -1f;
            ShowZonesUnion = sn > 0 ? sl / (float)sn : -1f;
            FavoriteProduction = -1;
            if (fav.Count > 0)
            {
                var top = fav.OrderByDescending(kv => kv.Value).First();
                if (top.Value >= 0.8f * fav.Values.Sum()) FavoriteProduction = top.Key;
            }
            ForgeLog.Msg($"[vanilla] learned from {Largos.Count} largos / {Bases.Count} base slimes: {LargoGroups.Count} largo groups, " +
                         $"{_parentGroups.Values.Sum(v => v.Count)} parent groups, {BaseGroups.Count} base groups, {PlortGroups.Count} plort groups, {CoreComponents.Count} core components; " +
                         $"one parent's colours {ColorExact}/{ColorExact + ColorOther}, {_parts.Values.Count(x => x.Substitute != null)} largo part versions");
        }

        /// <summary>Everything learned, as text (dev command <c>largoaudit learn</c>).</summary>
        public static string Describe()
        {
            Learn();
            var sb = new StringBuilder();
            string pct(float f) => f < 0f ? "?" : (f * 100f).ToString("0") + "%";
            sb.AppendLine($"snapshot {(HasSnapshot ? _snapshot.Count + " definitions" : "none (heuristic)")}; {Largos.Count} vanilla largos, {Bases.Count} vanilla base slimes");
            sb.AppendLine("groups of every largo: " + string.Join(", ", LargoGroups.Select(g => g.name)));
            sb.AppendLine("groups of every base slime: " + string.Join(", ", BaseGroups.Select(g => g.name + (_largoableOnly.Contains(g.Pointer) ? " (largoable only)" : ""))));
            sb.AppendLine("groups of every largoable plort: " + string.Join(", ", PlortGroups.Select(g => g.name)));
            foreach (var kv in _parentGroups.OrderBy(k => k.Key)) sb.AppendLine($"groups of {kv.Key} largos: " + string.Join(", ", kv.Value.Select(g => g.name)));
            sb.AppendLine("core largo components: " + string.Join(", ", CoreComponents.OrderBy(x => x)));
            foreach (var kv in _parentComponents.OrderBy(k => k.Key)) sb.AppendLine($"components of {kv.Key} largos: " + string.Join(", ", kv.Value.OrderBy(x => x)));
            sb.AppendLine($"zones = union of parents: {pct(ZonesUnion)}, showForZones: {pct(ShowZonesUnion)}, favourite production {FavoriteProduction}");
            sb.AppendLine($"animation overrides = body parent's, else the other's: {Animations} (applied: {AnimationsFromParents})");
            sb.AppendLine($"dynamic appearances: {_dynLargos} largos have them; entries own {_dynOwn}, parents' {_dynParent}, other {_dynOther}");
            sb.AppendLine("body / face given to vanilla largos (own / total):");
            foreach (var n in _body.Keys.Union(_face.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            {
                _body.TryGetValue(n, out var b); _face.TryGetValue(n, out var f);
                sb.AppendLine($"  {n,-12} body {b?.ToString() ?? "-",-6}{(b != null && b.Confident ? "*" : " ")} face {f?.ToString() ?? "-",-6}{(f != null && f.Confident ? "*" : " ")}");
            }
            sb.AppendLine("(* = confident, used by the largo engine when LargoBlend.LearnFromVanilla is on)");
            sb.AppendLine($"colours: {ColorExact} vanilla largos have one parent's palette, {ColorOther} neither (engine takes one parent's: {OneParentColors})");
            sb.AppendLine("colour rank (largos with its palette / total): " + string.Join(", ", _colors.OrderByDescending(kv => kv.Value.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value)));
            sb.AppendLine($"repaintable parts keeping the parent's colours: {PartColors} (part identity used: {(LearnedPartIdentity.HasValue ? LearnedPartIdentity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "default")})");
            sb.AppendLine("parts (kept / seen, clashes won, largo versions, drops):");
            foreach (var r in _parts.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) sb.AppendLine("  " + r);
            return sb.ToString();
        }
    }
}
