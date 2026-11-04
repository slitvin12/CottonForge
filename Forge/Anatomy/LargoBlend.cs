using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using ET = Il2Cpp.SlimeAppearanceElement.ElementType;
using MI = Il2Cpp.SlimeAppearanceElement.MatInheritType;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Smart largo looks, used by <c>Library.CreateCompleteLargo</c> (every Cotton largo) when <c>SmartLargos</c> is on.
    /// <list type="bullet">
    /// <item>Template: the vanilla Pink + X largo of one parent (prefab, effect settings, radiant variants) instead of always PinkRock,
    /// so Boom / Angler / Hyper ... largos keep the parts and behaviour their parent brings.</item>
    /// <item>Body: the more distinctive one (own mesh, own shader, glow, twin, sloomber ...), or as chosen by
    /// <see cref="LargoStyle"/> / <see cref="LargoRecipe"/> / Cotton flags. The other parent's body effects (boom cracks, hyper,
    /// twin, sloomber stars, phosphor glow) are transplanted onto it.</item>
    /// <item>Face: the more distinctive face, always the body's own when the body draws the face (Saber).</item>
    /// <item>Colours: as the vanilla largos (<see cref="LearnFromVanilla"/>): the palette of one parent, by the ranking learned from
    /// them (<see cref="VanillaSlimes.PreferColors"/>); otherwise a perceptual OKLCh mix (<see cref="ColorMix"/>, no grey-brown middle),
    /// clashing hues split top / bottom. Parts keep <see cref="PartIdentity"/> of their own colours (or what vanilla largos do).</item>
    /// <item>Parts of both parents, without duplicates (one pair of ears, one tail, one pair of wings ...; learned from the vanilla
    /// largos: who wins a clash, parts they drop, largo versions such as FlutterWingsLargo); 0-material structures
    /// (Phosphor's ShineRotator) are kept; parents' materials are never modified.</item>
    /// <item>Radiant variants built from the radiant parent (its palette and aura) instead of the template's stale PinkRock ones.</item>
    /// </list>
    /// Any exception falls back to Cotton's original merge for that largo.
    /// </summary>
    public static class LargoBlend
    {
        public const string BodyShader = "SR/AMP/Slime/Body";

        /// <summary>Config <c>SmartLargos</c> (default on).</summary>
        public static bool Enabled => ForgeConfig.SmartLargos == null || ForgeConfig.SmartLargos.Value;
        /// <summary>Config <c>LargoRadiants</c> (default on).</summary>
        public static bool BuildRadiants => ForgeConfig.LargoRadiants == null || ForgeConfig.LargoRadiants.Value;

        /// <summary>Colour mode when nothing else decides.</summary>
        public static LargoColorMode DefaultColorMode = LargoColorMode.Auto;
        /// <summary>How much of their own colours parts keep (0 = painted with the largo palette, 1 = untouched).</summary>
        public static float PartIdentity = 0.35f;
        /// <summary>Auto mode: hue distance (degrees) from which the parents' colours are split instead of blended.</summary>
        public static float AutoSplitHue = 40f;
        /// <summary>Share of the other parent in <see cref="LargoColorMode.Dominant"/>.</summary>
        public static float DominantHint = 0.15f;
        /// <summary>Colour weight multiplier of the radiant parent in radiant variants.</summary>
        public static float RadiantWeight = 4f;
        /// <summary>Body, face, colours, parts: follow what the vanilla largos do (<see cref="VanillaSlimes"/>) when the data is clear.</summary>
        public static bool LearnFromVanilla = true;

        /// <summary>Element types a largo has only once (the body owner's wins): no double ears, tails, wings ...</summary>
        public static readonly HashSet<ET> UniqueSlots = new() { ET.EARS, ET.TAIL, ET.WINGS, ET.WHISKERS, ET.ANTENNAE, ET.GLASSES, ET.FOREHEAD, ET.TOP };

        /// <summary>Built-in colour weights of vanilla slimes (a <see cref="LargoStyle.Colors(float)"/> overrides them).</summary>
        public static readonly Dictionary<string, float> ColorWeights = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Saber"] = 1.6f, ["Hyper"] = 1.5f, ["Phosphor"] = 1.3f, ["Gold"] = 2f, ["Shadow"] = 1.6f, ["Flutter"] = 0.6f,
        };

        /// <summary>Which parent's vanilla Pink largo becomes the template (first match wins; Rock = Cotton's PinkRock).</summary>
        public static readonly List<string> TemplateOrder = new()
        {
            "Boom", "Tangle", "Dervish", "Hunter", "Batty", "Flutter", "Ringtail", "Angler", "Saber", "Hyper", "Sloomber",
            "Phosphor", "Crystal", "Honey", "Cotton", "Tabby", "Twin", "Rock",
        };

        /// <summary>Every largo appearance built (default and radiant), after the styles and recipes ran.</summary>
        public static event Action<LargoBuild> Building;
        /// <summary>Every largo created by <c>CreateCompleteLargo</c> (after components were merged).</summary>
        public static event Action<SlimeDefinition> Created;

        public static int Built, RadiantVariants, Fallbacks;
        static bool _dry;   // Preview(structures): no material copies, no recolouring, no hooks

        /// <summary>What the engine did with one largo (dev command <c>blend</c>).</summary>
        public sealed class Info
        {
            public string Largo, Template, Body, Face, Mode, Palette, Radiants = "none", Reason, Error, Parity, Icon;
            public float Balance;
            public bool Legacy;
            public override string ToString() => (Legacy
                ? $"{Largo}: Cotton's merge (template {Template}){(Error != null ? " - smart look failed: " + Error : "")}"
                : $"{Largo}: template {Template}, body {Body}, face {Face}, colours {Mode} {Balance:0.00} [{Palette}], radiant {Radiants}" +
                  (string.IsNullOrEmpty(Reason) ? "" : " (" + Reason + ")")) +
                (Icon != null ? "; icon " + Icon : "") + (Parity != null ? "; parity: " + Parity : "");
        }
        static readonly Dictionary<string, Info> _info = new(StringComparer.OrdinalIgnoreCase);
        public static Info InfoOf(string largo) => largo != null && _info.TryGetValue(largo, out var i) ? i : null;
        public static Info InfoOf(SlimeDefinition largo) => largo != null ? InfoOf(largo.name) : null;
        public static IReadOnlyCollection<Info> All => _info.Values;

        // ------------------------------------------------------------------ small helpers

        static SlimeDefinition _pink;
        static SlimeDefinition Pink { get { if (_pink == null) _pink = Library.GetSlime("Pink"); return _pink; } }

        static bool Same(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase a, Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase b) =>
            (object)a != null && (object)b != null && a.Pointer == b.Pointer;
        static bool IsNull(object o) => o == null || (o is UnityEngine.Object u && u == null);

        static IEnumerable<SlimeAppearanceStructure> StructuresOf(SlimeAppearance app)
        {
            if (app == null) yield break;
            var arr = app.Structures;
            if (arr == null) yield break;
            for (int i = 0; i < arr.Length; i++)
            {
                var s = arr[i];
                if (s != null && s.Element != null) yield return s;
            }
        }

        /// <summary>The first BODY structure of an appearance (not always Structures[0]: Angler lists its lure first).</summary>
        public static SlimeAppearanceStructure BodyOf(SlimeAppearance app) => StructuresOf(app).FirstOrDefault(s => s.Element.Type == ET.BODY);

        internal static Material Main(SlimeAppearanceStructure st)
        {
            var m = st != null ? st.DefaultMaterials : null;
            return m != null && m.Length > 0 ? m[0] : null;
        }

        internal static List<Material> Mats(SlimeAppearanceStructure st)
        {
            var res = new List<Material>();
            var m = st != null ? st.DefaultMaterials : null;
            if (m != null) for (int i = 0; i < m.Length; i++) res.Add(m[i]);
            return res;
        }

        static bool Kw(Material m, string k)
        {
            if (m == null) return false;
            try { return m.IsKeywordEnabled(k); }
            catch
            {
                try { var kw = m.shaderKeywords; if (kw != null) for (int i = 0; i < kw.Length; i++) if (kw[i] == k) return true; } catch { }
                return false;
            }
        }
        internal static bool HasPalette(Material m) => m != null && (m.HasProperty("_TopColor") || m.HasProperty("_MiddleColor") || m.HasProperty("_BottomColor"));
        static string ShaderName(Material m) { var sh = m != null ? m.shader : null; return sh != null ? sh.name ?? "" : ""; }
        internal static bool IsAura(Material m) => ShaderName(m).Contains("RadiantAura");
        static bool IsSlimeShader(Material m) => ShaderName(m).StartsWith("SR/AMP/Slime", StringComparison.Ordinal);

        static SlimeAppearance Default(SlimeDefinition d)
        {
            var arr = d != null ? d.AppearancesDefault : null;
            return arr != null && arr.Length > 0 ? arr[0] : null;
        }

        /// <summary>Colours of an appearance: its body material (what you see), else <c>_colorPalette</c>.</summary>
        public static LargoPalette PaletteOf(SlimeAppearance app)
        {
            if (app == null) return new LargoPalette(Color.gray, Color.gray, Color.gray, new Color(0f, 0f, 0f, 0f));
            var p = app._colorPalette;
            var pal = new LargoPalette(p.Top, p.Middle, p.Bottom, p.Ammo);
            var m = Main(BodyOf(app));
            return m != null && HasPalette(m) ? LargoPalette.Of(m, pal) : pal;
        }

        /// <summary>How distinctive a body is (own mesh 100, own shader 50, glow / matcap 40, twin / sloomber / hyper 30 ...).</summary>
        public static int BodyScore(SlimeAppearance app)
        {
            var st = BodyOf(app);
            if (st == null) return -1000;
            int s = 0;
            if (!(st.Element.Name ?? "").StartsWith("DefaultBody", StringComparison.OrdinalIgnoreCase)) s += 100;
            var m = Main(st);
            if (m != null)
            {
                if (ShaderName(m) != BodyShader) s += 50;
                if (Kw(m, "_ENABLEPHOSCORE_ON") || Kw(m, "_ENABLEMATCAP_ON")) s += 40;
                if (Kw(m, "_BODYCOLORING_SLOOMBER") || Kw(m, "_ENABLETWINEFFECT_ON") || Kw(m, "_ENABLEHYPEREFFECT_ON")) s += 30;
                if (Kw(m, "_BODYCOLORING_MULTI")) s += 25;
                if (Kw(m, "_ENABLEBOOMCRACKS_ON")) s += 20;
            }
            return s;
        }

        /// <summary>How distinctive a face is (anything but the standard face: 10).</summary>
        public static int FaceScore(SlimeAppearance app)
        {
            if (app == null) return -1000;
            var f = app._face;
            return f != null && !(f.name ?? "").Contains("Standard") ? 10 : 0;
        }

        static float WeightOf(SlimeDefinition d, LargoStyle st)
        {
            if (st != null && st.ColorWeight.HasValue) return st.ColorWeight.Value;
            return d != null && ColorWeights.TryGetValue(d.name, out var w) ? w : 1f;
        }

        /// <summary>Fills the Cotton flags the original merge needs when only Auto flags were given.</summary>
        public static Library.LargoSettings LegacySettings(Library.LargoSettings s)
        {
            if ((s & (Library.LargoSettings.KeepFirstBody | Library.LargoSettings.KeepSecondBody)) == 0) s |= Library.LargoSettings.KeepSecondBody;
            if ((s & (Library.LargoSettings.KeepFirstFace | Library.LargoSettings.KeepSecondFace)) == 0) s |= Library.LargoSettings.KeepSecondFace;
            if ((s & (Library.LargoSettings.KeepFirstColor | Library.LargoSettings.KeepSecondColor | Library.LargoSettings.MergeColors)) == 0) s |= Library.LargoSettings.MergeColors;
            return s;
        }

        // ------------------------------------------------------------------ template

        // vanilla = existed before mods ran (VanillaSlimes snapshot; without it: no Cotton HideAndDontSave clone, no "Modded" id)
        static bool IsVanillaLargo(SlimeDefinition d) =>
            d != null && d.IsLargo && VanillaSlimes.IsVanilla(d) && !_info.ContainsKey(d.name) && d.prefab != null && Default(d) != null;

        /// <summary>The vanilla Pink + <paramref name="x"/> largo, or null.</summary>
        public static SlimeDefinition VanillaLargoWith(SlimeDefinition x)
        {
            var pink = Pink;
            if (x == null || pink == null || Same(x, pink)) return null;
            SlimeDefinition l = null;
            try
            {
                var defs = Library.slimeDefinitions;
                if (defs != null) l = defs.GetLargoByBaseSlimes(pink, x) ?? defs.GetLargoByBaseSlimes(x, pink);
            }
            catch { }
            if (!IsVanillaLargo(l)) l = Library.GetLargo("Pink" + x.name);
            return IsVanillaLargo(l) ? l : null;
        }

        /// <summary>
        /// Template of a new largo: the recipe / style template, else the vanilla Pink largo of the parent that comes first in
        /// <see cref="TemplateOrder"/> (also the vanilla template of a <see cref="SlimeBuilder"/> slime), else PinkRock (Cotton's).
        /// </summary>
        public static SlimeDefinition PickTemplate(SlimeDefinition one, SlimeDefinition two)
        {
            var fallback = Library.GetLargo("PinkRock");
            if (!Enabled || one == null || two == null) return fallback;
            return Safe.Run("largo template " + one.name + two.name, () => PickTemplateCore(one, two) ?? fallback, fallback);
        }

        static SlimeDefinition PickTemplateCore(SlimeDefinition one, SlimeDefinition two)
        {
            var forced = LargoRecipe.Find(one.name, two.name)?.TemplateSlime ?? LargoStyle.Find(two)?.TemplateSlime ?? LargoStyle.Find(one)?.TemplateSlime;
            if (forced != null)
            {
                var l = VanillaLargoWith(Library.GetSlime(forced));
                if (l != null) return l;
                ForgeLog.WarnOnce("largotpl:" + forced, $"Largo template {forced}: no vanilla Pink+{forced} largo, choosing automatically");
            }
            var cands = new List<string>();
            void Add(SlimeDefinition d)
            {
                cands.Add(d.name);
                var b = SlimeRegistry.Of(d);
                if (b != null && !string.IsNullOrEmpty(b.TemplateName) && !b.TemplateName.Equals("Pink", StringComparison.OrdinalIgnoreCase)) cands.Add(b.TemplateName);
            }
            Add(two); Add(one);
            foreach (var t in TemplateOrder)
                if (cands.Any(c => c.Equals(t, StringComparison.OrdinalIgnoreCase)))
                {
                    var l = VanillaLargoWith(Library.GetSlime(t));
                    if (l != null) return l;
                }
            return null;
        }

        // ------------------------------------------------------------------ decisions

        sealed class Plan
        {
            public readonly SlimeDefinition[] Def = new SlimeDefinition[2];
            public readonly SlimeAppearance[] App = new SlimeAppearance[2];
            public readonly LargoStyle[] Style = new LargoStyle[2];
            public readonly float[] Weight = { 1f, 1f };
            public LargoRecipe Recipe;
            public Library.LargoSettings Settings;
            public SlimeDefinition Template;
            public int Body = 1, Face = 1, DominantPref = -1;
            public LargoColorMode Mode = LargoColorMode.Auto;
            public float? Share;
            public bool LearnedColors;
            public readonly List<string> Why = new();
        }

        static int SideOf(Plan p, string slime)
        {
            if (string.IsNullOrEmpty(slime)) return -1;
            for (int i = 0; i < 2; i++)
                if (p.Def[i] != null && string.Equals(p.Def[i].name, slime, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        static Plan MakePlan(SlimeDefinition one, SlimeDefinition two, SlimeAppearance a, SlimeAppearance b, SlimeDefinition template, Library.LargoSettings s)
        {
            var p = new Plan { Settings = s, Template = template };
            p.Def[0] = one; p.Def[1] = two; p.App[0] = a; p.App[1] = b;
            p.Style[0] = LargoStyle.Find(one); p.Style[1] = LargoStyle.Find(two);
            if (one != null && two != null) p.Recipe = LargoRecipe.Find(one.name, two.name);
            p.Body = ChooseBody(p);
            p.Face = ChooseFace(p);
            ChooseColors(p);
            return p;
        }

        static int ChooseBody(Plan p)
        {
            bool h0 = BodyOf(p.App[0]) != null, h1 = BodyOf(p.App[1]) != null;
            if (h0 != h1) { p.Why.Add("body: the other parent has none"); return h0 ? 0 : 1; }
            int r = SideOf(p, p.Recipe?.BodyFrom);
            if (r >= 0) { p.Why.Add("body: recipe"); return r; }
            int s0 = p.Style[0]?.BodyPriority ?? 0, s1 = p.Style[1]?.BodyPriority ?? 0;
            if (s0 != s1) { p.Why.Add("body: style"); return s0 > s1 ? 0 : 1; }
            var f = p.Settings;
            if ((f & Library.LargoSettings.AutoBody) == 0)
            {
                bool k0 = (f & Library.LargoSettings.KeepFirstBody) != 0, k1 = (f & Library.LargoSettings.KeepSecondBody) != 0;
                if (k0 != k1) { p.Why.Add("body: flags"); return k0 ? 0 : 1; }
            }
            int a = BodyScore(p.App[0]), b = BodyScore(p.App[1]);
            if (LearnFromVanilla)
            {
                int v = VanillaSlimes.PreferBody(p.Def[0], p.Def[1], a == b, out var why);
                if (v >= 0) { p.Why.Add(why); return v; }
            }
            if (a != b) { p.Why.Add($"body: score {a}/{b}"); return a > b ? 0 : 1; }
            return 1;
        }

        static int ChooseFace(Plan p)
        {
            var body = BodyOf(p.App[p.Body]);
            if (body != null && body.SupportsFaces) { p.Why.Add("face: drawn by the body"); return p.Body; }
            int r = SideOf(p, p.Recipe?.FaceFrom);
            if (r >= 0) { p.Why.Add("face: recipe"); return r; }
            int s0 = p.Style[0]?.FacePriority ?? 0, s1 = p.Style[1]?.FacePriority ?? 0;
            if (s0 != s1) { p.Why.Add("face: style"); return s0 > s1 ? 0 : 1; }
            var f = p.Settings;
            if ((f & Library.LargoSettings.AutoFace) == 0)
            {
                bool k0 = (f & Library.LargoSettings.KeepFirstFace) != 0, k1 = (f & Library.LargoSettings.KeepSecondFace) != 0;
                if (k0 != k1) { p.Why.Add("face: flags"); return k0 ? 0 : 1; }
            }
            int a = FaceScore(p.App[0]), b = FaceScore(p.App[1]);
            if (LearnFromVanilla)
            {
                int v = VanillaSlimes.PreferFace(p.Def[0], p.Def[1], a == b, out var why);
                if (v >= 0) { p.Why.Add(why); return v; }
            }
            if (a != b) return a > b ? 0 : 1;
            return p.Body;
        }

        static LargoColorMode FromStyle(LargoColorMode m, int side) =>
            m == LargoColorMode.First ? (side == 0 ? LargoColorMode.First : LargoColorMode.Second) :
            m == LargoColorMode.Second ? (side == 0 ? LargoColorMode.Second : LargoColorMode.First) : m;

        static void ChooseColors(Plan p)
        {
            p.Weight[0] = WeightOf(p.Def[0], p.Style[0]);
            p.Weight[1] = WeightOf(p.Def[1], p.Style[1]);
            var rec = p.Recipe;
            if (rec != null && rec.BalanceShare.HasValue)
            {
                int bs = SideOf(p, rec.BalanceSlime);
                if (bs >= 0) p.Share = bs == 1 ? rec.BalanceShare.Value : 1f - rec.BalanceShare.Value;
            }
            int cf = SideOf(p, rec?.ColorsFrom);
            if (cf >= 0) { p.Mode = cf == 0 ? LargoColorMode.First : LargoColorMode.Second; p.Why.Add("colours: recipe"); return; }
            if (rec != null && rec.Mode.HasValue) { p.Mode = rec.Mode.Value; p.Why.Add("colours: recipe"); return; }
            var m0 = p.Style[0]?.ColorMode; var m1 = p.Style[1]?.ColorMode;
            if (m0.HasValue || m1.HasValue)
            {
                int side = m0.HasValue && m1.HasValue ? (m0.Value == m1.Value ? -1 : (p.Weight[0] > p.Weight[1] ? 0 : 1)) : (m0.HasValue ? 0 : 1);
                p.Mode = side < 0 ? FromStyle(m0.Value, 0) : FromStyle(side == 0 ? m0.Value : m1.Value, side);
                p.DominantPref = side;
                p.Why.Add("colours: style");
                return;
            }
            var f = p.Settings;
            if ((f & Library.LargoSettings.AutoColors) == 0)
            {
                bool k0 = (f & Library.LargoSettings.KeepFirstColor) != 0, k1 = (f & Library.LargoSettings.KeepSecondColor) != 0;
                if (k0 != k1) { p.Mode = k0 ? LargoColorMode.First : LargoColorMode.Second; p.Why.Add("colours: flags"); return; }
            }
            // vanilla largos take one parent's palette, by a strict ranking (Pink, Hyper, Sloomber ... over Saber, Twin)
            if (LearnFromVanilla && DefaultColorMode == LargoColorMode.Auto && !p.Share.HasValue && VanillaSlimes.OneParentColors)
            {
                int v = VanillaSlimes.PreferColors(p.Def[0], p.Def[1], p.Weight[0], p.Weight[1], out var why);
                if (v < 0) { v = p.Body; why += ", tie: the body's"; }
                p.Mode = v == 0 ? LargoColorMode.First : LargoColorMode.Second;
                p.LearnedColors = true;
                p.Why.Add(why);
                return;
            }
            p.Mode = DefaultColorMode;
        }

        static bool Clash(LargoPalette a, LargoPalette b) =>
            !ColorMix.IsGrey(a.Middle) && !ColorMix.IsGrey(b.Middle) && ColorMix.HueDistance(a.Middle, b.Middle) >= AutoSplitHue;

        static void Decide(Plan p, LargoBuild c, SlimeAppearance a, SlimeAppearance b, int radiantSide)
        {
            c.First = p.Def[0]; c.Second = p.Def[1]; c.FirstAppearance = a; c.SecondAppearance = b;
            c.Template = p.Template; c.Settings = p.Settings; c.RadiantSide = radiantSide;
            c.BodySide = p.Body; c.FaceSide = p.Face; c.FirstStyle = p.Style[0]; c.SecondStyle = p.Style[1]; c.Recipe = p.Recipe;
            var pal = new[] { PaletteOf(a), PaletteOf(b) };
            var w = new[] { p.Weight[0], p.Weight[1] };
            if (radiantSide >= 0) w[radiantSide] *= RadiantWeight;
            float share = p.Share ?? (w[0] + w[1] > 0f ? w[1] / (w[0] + w[1]) : 0.5f);
            if (radiantSide >= 0 && p.Share.HasValue) share = Mathf.Lerp(share, radiantSide, 0.5f);
            int dom = p.DominantPref >= 0 ? p.DominantPref : share > 0.501f ? 1 : share < 0.499f ? 0 : p.Body;
            var mode = p.Mode;
            if (p.LearnedColors && radiantSide >= 0) mode = radiantSide == 0 ? LargoColorMode.First : LargoColorMode.Second;   // radiant variant: the radiant parent's colours
            if (mode == LargoColorMode.Custom && (p.Recipe == null || !p.Recipe.CustomPalette.HasValue)) mode = LargoColorMode.Auto;
            if (mode == LargoColorMode.Auto) mode = Clash(pal[0], pal[1]) ? LargoColorMode.Split : LargoColorMode.Blend;
            LargoPalette res;
            switch (mode)
            {
                case LargoColorMode.First: res = pal[0]; share = 0f; break;
                case LargoColorMode.Second: res = pal[1]; share = 1f; break;
                case LargoColorMode.Custom: res = p.Recipe.CustomPalette.Value; break;
                case LargoColorMode.Dominant: res = LargoPalette.Mix(pal[dom], pal[1 - dom], DominantHint); share = dom == 0 ? DominantHint : 1f - DominantHint; break;
                case LargoColorMode.Split:
                {
                    var d = pal[dom]; var o = pal[1 - dom];
                    float s = dom == 0 ? share : 1f - share;
                    res = new LargoPalette(ColorMix.Mix(d.Top, o.Top, s * 0.5f), ColorMix.Mix(d.Middle, o.Middle, s),
                        ColorMix.Mix(d.Bottom, o.Bottom, 0.5f + s * 0.5f), LargoPalette.MixAmmo(d.Ammo, o.Ammo, s));
                    break;
                }
                default: res = LargoPalette.Mix(pal[0], pal[1], share); break;
            }
            c.FirstPalette = pal[0]; c.SecondPalette = pal[1];
            c.Palette = res; c.ColorMode = mode; c.Balance = share; c.DominantSide = dom;
            c.Reason = string.Join("; ", p.Why);
        }

        // ------------------------------------------------------------------ body effects

        sealed class Fx
        {
            public string Name, Keyword, Probe, Blocker;
            public string[] Off = Array.Empty<string>(), F = Array.Empty<string>(), T = Array.Empty<string>(), C = Array.Empty<string>(), V = Array.Empty<string>();
        }

        static readonly Fx[] Effects =
        {
            new Fx { Name = "boom", Keyword = "_ENABLEBOOMCRACKS_ON", Probe = "_Cracks", T = new[] { "_Cracks", "_CrackNoise" }, C = new[] { "_CrackColor" },
                F = new[] { "_EnableBoomCracks", "_CrackNoiseSpeed", "_CrackAmount", "_CrackHueShift", "_CrackColorRange" } },
            new Fx { Name = "hyper", Keyword = "_ENABLEHYPEREFFECT_ON", Probe = "_HyperFresnelNoise",
                T = new[] { "_HyperFresnelNoise", "_HyperSparksNoise", "_HyperDashGradient" }, C = new[] { "_HyperFresnelColor" },
                F = new[] { "_EnableHyperEffect", "_HyperBodyColorAlbedoIntensity", "_HyperEmissionIntensity", "_HyperPolarUVTilingX", "_HyperPolarUVTilingY",
                    "_HyperSecondaryUVTilingX", "_HyperSecondaryUVTilingY", "_HyperFresnelIntensity", "_HyperFresnelPower", "_HyperFresnelSoftGlowIntensity",
                    "_HyperFresnelHardEdgeStep", "_HyperFresnelHardEdgeIntensity", "_HyperFresnelSparksNoiseIntensity", "_HyperFresnelSecondaryNoiseIntensity",
                    "_BodyHyperDash", "_HyperDashSparksIntensity", "_HyperDashSparksTilingX", "_HyperDashSparksTilingY", "_HyperDashSparksSpeed",
                    "_HyperDashNoiseThreshold", "_HyperDashGlowIntensity" } },
            new Fx { Name = "twin", Keyword = "_ENABLETWINEFFECT_ON", Probe = "_TwinTopColor", C = new[] { "_TwinTopColor", "_TwinMiddleColor", "_TwinBottomColor" },
                V = new[] { "_TwinVortexOffset" }, F = new[] { "_EnableTwinEffect", "_FreezeTwinSplit", "_TwinEffectOffset", "_TwinEffectSize", "_TwinLineDivisionIntensity", "_TwinGlowIntensity" } },
            new Fx { Name = "sloomber", Keyword = "_BODYCOLORING_SLOOMBER", Probe = "_SloomberStarMask", Blocker = "_BODYCOLORING_MULTI", Off = new[] { "_BODYCOLORING_DEFAULT" },
                T = new[] { "_SloomberStarMask", "_SloomberColorOverlay" }, C = new[] { "_SloomberTopColor", "_SloomberBottomColor" }, F = new[] { "_BodyColoring" } },
            new Fx { Name = "phosphor", Keyword = "_ENABLEPHOSCORE_ON", Probe = "_GlowTop", C = new[] { "_GlowTop" },
                F = new[] { "_EnablePhosCore", "_GlowSpeed", "_GlowMin", "_GlowMax", "_GlowStrength" } },
        };

        /// <summary>Copies one shader effect (keyword + its properties) from <paramref name="src"/> onto <paramref name="dst"/>.</summary>
        static bool Transplant(Material dst, Material src, Fx fx)
        {
            if (dst == null || src == null || !Kw(src, fx.Keyword) || Kw(dst, fx.Keyword)) return false;
            if (fx.Probe != null && !dst.HasProperty(fx.Probe)) return false;
            if (fx.Blocker != null && Kw(dst, fx.Blocker)) return false;
            foreach (var p in fx.T) if (src.HasProperty(p)) dst.Tx(p, src.GetTexture(p));
            foreach (var p in fx.F) if (src.HasProperty(p)) dst.Flt(p, src.GetFloat(p));
            foreach (var p in fx.C) if (src.HasProperty(p)) dst.Col(p, src.GetColor(p));
            foreach (var p in fx.V) if (src.HasProperty(p)) dst.Vec(p, src.GetVector(p));
            foreach (var k in fx.Off) dst.Kw(k, false);
            dst.Kw(fx.Keyword, true);
            return true;
        }

        // ------------------------------------------------------------------ structures

        static SlimeAppearanceStructure CopyShared(SlimeAppearanceStructure st)
        {
            var copy = new SlimeAppearanceStructure(st);
            copy.DefaultMaterials = Gc.Array(Mats(st));
            return copy;
        }

        static bool AddFace(List<SlimeAppearanceStructure> list, SlimeAppearance app)
        {
            bool any = false;
            foreach (var st in StructuresOf(app))
                if (st.Element.Type == ET.FACE) { list.Add(CopyShared(st)); any = true; }
            return any;
        }

        static HashSet<ET> PartTypes(SlimeAppearance app)
        {
            var s = new HashSet<ET>();
            foreach (var st in StructuresOf(app)) if (st.Element.Type != ET.BODY && st.Element.Type != ET.FACE) s.Add(st.Element.Type);
            return s;
        }

        // Unique slots both parents fill: the side whose part the vanilla largos keep in such clashes (Bat Ears beat Tabby Ears).
        static Dictionary<ET, int> LearnedClashes(SlimeAppearance[] apps, List<string> notes)
        {
            var res = new Dictionary<ET, int>();
            var best = new[] { new Dictionary<ET, float>(), new Dictionary<ET, float>() };
            for (int s = 0; s < 2; s++)
                foreach (var st in StructuresOf(apps[s]))
                {
                    var t = st.Element.Type;
                    if (!UniqueSlots.Contains(t)) continue;
                    var r = VanillaSlimes.PartOf(st.Element.Name);
                    float v = r != null && r.Clash.Total >= VanillaSlimes.PartMinSamples ? r.Clash.Value : -1f;
                    best[s][t] = best[s].TryGetValue(t, out var cur) ? Math.Max(cur, v) : v;
                }
            foreach (var kv in best[0])
            {
                if (!best[1].TryGetValue(kv.Key, out var b)) continue;
                float a = kv.Value;
                int w = -1;
                if (a >= 0f && b >= 0f) { if (Math.Abs(a - b) >= 0.5f) w = a > b ? 0 : 1; }
                else if (a >= 0f) { if (a >= 0.75f) w = 0; else if (a <= 0.25f) w = 1; }
                else if (b >= 0f) { if (b >= 0.75f) w = 1; else if (b <= 0.25f) w = 0; }
                if (w >= 0) { res[kv.Key] = w; notes.Add($"{kv.Key} of the {(w == 0 ? "first" : "second")} (clashes)"); }
            }
            return res;
        }

        static void BuildStructures(LargoBuild c, string name)
        {
            var apps = new[] { c.FirstAppearance, c.SecondAppearance };
            var list = c.Structures;
            var owner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var slots = new Dictionary<ET, int>();
            int bs = c.BodySide, os = 1 - bs;
            var parentBody = new[] { Main(BodyOf(apps[0])), Main(BodyOf(apps[1])) };
            bool faceOnBody = false;
            var fx = new List<string>();
            // learned from the vanilla largos: largo versions of parts, parts dropped next to others, clash winners
            bool learn = LearnFromVanilla;
            var vanilla = new List<string>();
            var types = new[] { PartTypes(apps[0]), PartTypes(apps[1]) };
            var clash = learn ? LearnedClashes(apps, vanilla) : new Dictionary<ET, int>();

            foreach (var orig in StructuresOf(apps[bs]))
            {
                if (orig.Element.Type != ET.BODY) continue;
                var st = orig;
                var bsub = learn ? VanillaSlimes.PartOf(orig.Element.Name)?.Substitute : null;
                if (bsub != null && bsub.Element != null && bsub.Element.Type == ET.BODY) { st = bsub; vanilla.Add("body " + (bsub.Element.Name ?? "?")); }
                var copy = new SlimeAppearanceStructure(st);
                var mats = Mats(st);
                if (!_dry && mats.Count > 0 && mats[0] != null)
                {
                    var m = Mat.Copy(mats[0], CopyName(c, mats[0], c.BodyMaterial != null ? list.Count.ToString() : ""));
                    c.Palette.ApplyTo(m);
                    foreach (var e in Effects) if (Transplant(m, parentBody[os], e)) fx.Add(e.Name);
                    mats[0] = m;
                    if (c.BodyMaterial == null) c.BodyMaterial = m;
                }
                copy.DefaultMaterials = Gc.Array(mats);
                if (st.SupportsFaces) faceOnBody = true;
                list.Add(copy);
                owner[orig.Element.Name ?? ""] = bs;
                owner[st.Element.Name ?? ""] = bs;
            }
            if (fx.Count > 0) c.Reason += (c.Reason.Length > 0 ? "; " : "") + "effects " + string.Join(",", fx);

            if (!faceOnBody && !AddFace(list, apps[c.FaceSide]) && !AddFace(list, apps[1 - c.FaceSide]))
                AddFace(list, Default(c.Template));

            foreach (int side in new[] { bs, os })
            {
                var style = side == 0 ? c.FirstStyle : c.SecondStyle;
                foreach (var st in StructuresOf(apps[side]))
                {
                    var t = st.Element.Type;
                    if (t == ET.BODY || t == ET.FACE) continue;
                    var en = st.Element.Name ?? "";
                    if (style != null && style.Drops(en)) continue;
                    if (owner.TryGetValue(en, out var o) && o != side) continue;
                    var rule = learn ? VanillaSlimes.PartOf(en) : null;
                    if (rule != null && rule.Dropped(types[1 - side], out var why)) { vanilla.Add(why); continue; }
                    bool unique = UniqueSlots.Contains(t);
                    if (unique && clash.TryGetValue(t, out var w) && w != side) continue;
                    if (unique && slots.TryGetValue(t, out var so) && so != side) continue;
                    owner[en] = side;
                    if (unique) slots[t] = side;
                    var use = st;
                    var sub = rule != null ? rule.Substitute : null;
                    if (sub != null && sub.Element != null && sub.Element.Type != ET.BODY && sub.Element.Type != ET.FACE)
                    {
                        use = sub;
                        owner[sub.Element.Name ?? ""] = side;
                        vanilla.Add(en + " -> " + sub.Element.Name);
                    }
                    list.Add(Part(c, use, style, parentBody[side], name));
                }
            }
            if (vanilla.Count > 0) c.Reason += (c.Reason.Length > 0 ? "; " : "") + "vanilla parts: " + string.Join(", ", vanilla);
        }

        // Copies keep the source name (as Cotton's clones did): mods that find parts by material name ("Mosaic...") still work.
        internal static string CopyName(LargoBuild c, Material src, string extra)
        {
            var n = src != null ? src.name ?? "Material" : "Material";
            int k = n.IndexOf("(Clone)", StringComparison.Ordinal); if (k > 0) n = n.Substring(0, k).TrimEnd();
            return n + "_largo" + (c != null && c.IsRadiant ? "R" + (c.RadiantSide + 1) : "") + extra;
        }

        static SlimeAppearanceStructure Part(LargoBuild c, SlimeAppearanceStructure st, LargoStyle style, Material parentBody, string name)
        {
            var copy = new SlimeAppearanceStructure(st);
            if (_dry) return copy;
            var src = Mats(st);
            var inh = st.Element.MaterialInheritance;
            var en = st.Element.Name ?? "Part";
            float id = style != null && style.KeepsOwnColors(en) ? 1f : (style?.PartIdentity ?? (LearnFromVanilla ? VanillaSlimes.LearnedPartIdentity : null) ?? PartIdentity);
            var mats = new List<Material>(src.Count);
            for (int i = 0; i < src.Count; i++)
            {
                var m = src[i];
                if (m == null || inh == MI.NONE || IsAura(m) || !HasPalette(m)) { mats.Add(m); continue; }
                bool own = id >= 0.999f;
                if (inh == MI.BODY && i == 0 && c.BodyMaterial != null && Same(m, parentBody)) { mats.Add(c.BodyMaterial); continue; }
                var k = Mat.Copy(m, CopyName(c, m, ""));
                if (own) { mats.Add(k); continue; } // own colours, but a largo-owned copy (safe to edit)
                var ownPal = LargoPalette.Of(m, c.Palette);
                if (inh == MI.COLORS_BOTTOM) k.Col("_BottomColor", ColorMix.Mix(c.Palette.Bottom, ownPal.Bottom, id));
                else LargoPalette.Mix(c.Palette, ownPal, inh == MI.BODY ? 0f : id).ApplyTo(k);
                mats.Add(k);
            }
            copy.DefaultMaterials = Gc.Array(mats);
            return copy;
        }

        static void AddAura(List<SlimeAppearanceStructure> list, SlimeAppearance radiant, SlimeAppearance fallback)
        {
            Material strong = null, light = null;
            void Scan(SlimeAppearance app)
            {
                foreach (var st in StructuresOf(app))
                {
                    var mats = st.DefaultMaterials;
                    if (mats == null) continue;
                    for (int i = 1; i < mats.Length; i++)
                        if (IsAura(mats[i]))
                        {
                            if (st.Element.Type == ET.BODY) { if (strong == null) strong = mats[i]; }
                            else if (light == null) light = mats[i];
                        }
                }
            }
            Scan(radiant);
            if (strong == null || light == null) Scan(fallback);
            if (strong == null) strong = Mat.Find("RadiantAura_strong");
            if (light == null) light = Mat.Find("RadiantAura_light") ?? strong;
            foreach (var st in list)
            {
                var t = st.Element.Type;
                if (t == ET.FACE || t == ET.AURA || t == ET.HYPERAURA || t == ET.SLOOMBERAURA || t == ET.GLASSES || t == ET.CORE) continue;
                var mats = st.DefaultMaterials;
                if (mats == null || mats.Length == 0 || !IsSlimeShader(mats[0])) continue;
                bool has = false;
                for (int i = 0; i < mats.Length; i++) if (IsAura(mats[i])) { has = true; break; }
                var aura = t == ET.BODY ? strong : light;
                if (!has && aura != null) st.DefaultMaterials = Gc.Append(mats, aura);
            }
            if (!list.Any(s => s.Element.Type == ET.AURA))
                foreach (var st in StructuresOf(fallback))
                    if (st.Element.Type == ET.AURA) list.Add(CopyShared(st));
        }

        // ------------------------------------------------------------------ appearance

        static void Feature<T>(Func<SlimeAppearance, T> get, Action<SlimeAppearance, T> set, SlimeAppearance app, SlimeAppearance a, SlimeAppearance b) where T : class
        {
            T pa = get(a), pb = get(b), cur = get(app);
            if (IsNull(pa) && IsNull(pb)) { if (!IsNull(cur)) set(app, null); }
            else if (IsNull(cur)) set(app, !IsNull(pa) ? pa : pb);
        }

        static void ApplyLook(LargoBuild c, SlimeAppearance app, SlimeAppearance defA, SlimeAppearance defB)
        {
            var apps = new[] { c.FirstAppearance, c.SecondAppearance };
            Safe.Run("largo face", () =>
            {
                var f = apps[c.FaceSide]._face;
                if (f == null) f = apps[1 - c.FaceSide]._face;
                if (f != null) app._face = f;
            });
            Safe.Run("largo palette", () =>
            {
                var p = app._colorPalette;
                p.Top = c.Palette.Top; p.Middle = c.Palette.Middle; p.Bottom = c.Palette.Bottom; p.Ammo = c.Palette.Ammo;
                app._colorPalette = p;
                Safe.Quiet(() => app.ColorPaletteNew = p);
                app._splatColor = c.ColorMode == LargoColorMode.Custom ? c.Palette.Middle : ColorMix.Mix(apps[0]._splatColor, apps[1]._splatColor, c.Balance);
                Safe.Quiet(app.SetSplatColor);
            });
            Safe.Run("largo effect appearances", () =>
            {
                Feature(x => x._glintAppearance, (x, v) => x._glintAppearance = v, app, defA, defB);
                Feature(x => x._tornadoAppearance, (x, v) => x._tornadoAppearance = v, app, defA, defB);
                Feature(x => x._vineAppearance, (x, v) => x._vineAppearance = v, app, defA, defB);
                Feature(x => x._crystalAppearance, (x, v) => x._crystalAppearance = v, app, defA, defB);
                Feature(x => x._explosionAppearance, (x, v) => x._explosionAppearance = v, app, defA, defB);
                Feature(x => x._stoneFormAppearance, (x, v) => x._stoneFormAppearance = v, app, defA, defB);
                Feature(x => x._roarAppearance, (x, v) => x._roarAppearance = v, app, defA, defB);
                Feature(x => x._deathAppearance, (x, v) => x._deathAppearance = v, app, defA, defB);
                Feature(x => x._shockedAppearance, (x, v) => x._shockedAppearance = v, app, defA, defB);
                Feature(x => x._qubitAppearance, (x, v) => x._qubitAppearance = v, app, defA, defB);
            });
            if (c.RadiantSide >= 0) Safe.Quiet(() => app._radiantColor = apps[c.RadiantSide]._radiantColor);
            app._dependentAppearances = Gc.Array(new List<SlimeAppearance> { apps[0], apps[1] });
        }

        static void Hooks(LargoBuild c)
        {
            if (_dry) return;
            if (c.FirstStyle?.BuildHook != null) Safe.Run("LargoStyle " + c.FirstStyle.Slime, () => c.FirstStyle.BuildHook(c));
            if (c.SecondStyle?.BuildHook != null) Safe.Run("LargoStyle " + c.SecondStyle.Slime, () => c.SecondStyle.BuildHook(c));
            if (c.Recipe?.BuildHook != null) Safe.Run("LargoRecipe " + c.Recipe.A + "+" + c.Recipe.B, () => c.Recipe.BuildHook(c));
            var ev = Building;
            if (ev != null) Safe.Run("LargoBlend.Building", () => ev(c));
        }

        static LargoBuild BuildVariant(Plan p, SlimeDefinition largo, SlimeAppearance target, SlimeAppearance a, SlimeAppearance b, int radiantSide, string name, SlimeAppearance auraFallback)
        {
            var c = new LargoBuild { Largo = largo, Appearance = target };
            Decide(p, c, a, b, radiantSide);
            BuildStructures(c, name);
            if (radiantSide >= 0) AddAura(c.Structures, radiantSide == 0 ? a : b, auraFallback);
            if (target != null) ApplyLook(c, target, p.App[0], p.App[1]);
            Hooks(c);
            if (target != null) target._structures = Gc.Array(c.Structures.Where(s => s != null).ToList());
            return c;
        }

        // ------------------------------------------------------------------ radiant variants

        /// <summary>The radiant appearance of a base slime, null when it has none (or only the one inherited from Pink).</summary>
        public static SlimeAppearance RadiantOf(SlimeDefinition d)
        {
            var r = d != null ? d.RadiantBase : null;
            if (r == null || Same(r, Default(d))) return null;
            var pink = Pink;
            if (pink != null && !Same(d, pink) && Same(r, pink.RadiantBase)) return null;
            return r;
        }

        static int RadiantSideOf(SlimeDefinition tpl, SlimeAppearance tApp)
        {
            var bases = tpl != null ? tpl.BaseSlimes : null;
            if (bases == null || tApp == null) return -1;
            var dep = tApp._dependentAppearances;
            for (int k = 0; k < bases.Length && k < 2; k++)
            {
                var rb = bases[k] != null ? bases[k].RadiantBase : null;
                if (rb != null && dep != null) for (int i = 0; i < dep.Length; i++) if (Same(dep[i], rb)) return k;
            }
            var n = tApp.name ?? "";
            for (int k = 0; k < bases.Length && k < 2; k++)
                if (bases[k] != null && n.IndexOf(bases[k].name + "Radiant", StringComparison.OrdinalIgnoreCase) >= 0) return k;
            return -1;
        }

        /// <summary>Which base slime (0 / 1) is radiant in <c>RadiantLargo0</c> of a largo (read from the template, default 0).</summary>
        static int Field0Side(SlimeDefinition tpl)
        {
            int k = RadiantSideOf(tpl, tpl != null ? tpl.RadiantLargo0 : null);
            if (k >= 0) return k;
            k = RadiantSideOf(tpl, tpl != null ? tpl.RadiantLargo1 : null);
            return k >= 0 ? 1 - k : 0;
        }

        static string BuildRadiantVariants(Plan p, SlimeDefinition largo, SlimeAppearance app0)
        {
            var tpl = p.Template;
            var rad = new[] { RadiantOf(p.Def[0]), RadiantOf(p.Def[1]) };
            int f0 = Field0Side(tpl);
            var v = new SlimeAppearance[2];
            var anyRadiantApp = tpl != null && tpl.RadiantLargo0 != null ? tpl.RadiantLargo0 : Library.GetLargo("PinkRock")?.RadiantLargo0;
            for (int s = 0; s < 2; s++)
            {
                if (rad[s] == null) continue;
                var tApp = tpl != null ? (s == f0 ? tpl.RadiantLargo0 : tpl.RadiantLargo1) : null;
                var va = UnityEngine.Object.Instantiate(tApp != null ? tApp : app0);
                UnityEngine.Object.DontDestroyOnLoad(va);
                va.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                var ra = s == 0 ? rad[0] : p.App[0];
                var rb = s == 1 ? rad[1] : p.App[1];
                va.name = ra.name + rb.name;
                va._appearType = s == f0 ? SlimeAppearance.AppearanceType.RADIANT_LARGO_0 : SlimeAppearance.AppearanceType.RADIANT_LARGO_1;
                BuildVariant(p, largo, va, ra, rb, s, largo.name + "_Radiant" + (s + 1), tApp ?? anyRadiantApp);
                v[s] = va;
                RadiantVariants++;
            }
            SlimeAppearance r0 = v[f0] ?? v[1 - f0], r1 = v[1 - f0] ?? v[f0];
            var list = new List<SlimeAppearance> { app0 };
            if (r0 != null) list.Add(r0);
            if (r1 != null && !Same(r1, r0)) list.Add(r1);
            app0._appearType = SlimeAppearance.AppearanceType.DEFAULT;
            largo.AppearancesDefault = Gc.Array(list);
            largo.RadiantBase = null;
            largo.RadiantLargo0 = r0;
            largo.RadiantLargo1 = r1;
            var names = new List<string>();
            for (int s = 0; s < 2; s++) if (v[s] != null) names.Add(p.Def[s].name);
            return names.Count == 0 ? "none" : string.Join("+", names);
        }

        // ------------------------------------------------------------------ entry points (CreateCompleteLargo)

        /// <summary>
        /// Builds the appearance(s) of a largo created by <c>CreateCompleteLargo</c> (<c>AppearancesDefault[0]</c> must already be an
        /// own copy of the template's). Returns false when the caller must use Cotton's original merge.
        /// </summary>
        public static bool Apply(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two, SlimeDefinition template, Library.LargoSettings settings)
        {
            if (!Enabled || largo == null || one == null || two == null) return false;
            var info = new Info { Largo = largo.name, Template = template != null ? template.name : "?" };
            try
            {
                var a = Default(one); var b = Default(two);
                var app = Default(largo);
                if (a == null || b == null || app == null) throw new InvalidOperationException("missing appearance");
                var p = MakePlan(one, two, a, b, template, settings);
                var c = BuildVariant(p, largo, app, a, b, -1, largo.name, null);
                Safe.Quiet(() => largo.color = c.ColorMode == LargoColorMode.First ? one.color : c.ColorMode == LargoColorMode.Second ? two.color : ColorMix.Mix(one.color, two.color, c.Balance));
                info.Body = c.BodyFrom != null ? c.BodyFrom.name : "?";
                info.Face = c.FaceFrom != null ? c.FaceFrom.name : "?";
                info.Mode = c.ColorMode.ToString(); info.Balance = c.Balance; info.Palette = c.Palette.ToString(); info.Reason = c.Reason;
                if (BuildRadiants)
                {
                    try { info.Radiants = BuildRadiantVariants(p, largo, app); }
                    catch (Exception e)
                    {
                        info.Radiants = "failed";
                        ForgeLog.WarnOnce("blendrad:" + largo.name, $"Radiant variants of {largo.name}: {e.GetType().Name}: {e.Message}");
                    }
                }
                else info.Radiants = "template's (LargoRadiants off)";
                _info[largo.name] = info;
                Built++;
                ForgeLog.Debug("[largo] " + info);
                return true;
            }
            catch (Exception e)
            {
                Fallbacks++;
                info.Legacy = true; info.Error = e.GetType().Name + ": " + e.Message;
                _info[largo.name] = info;
                ForgeLog.WarnOnce("blend:" + largo.name, $"Smart largo look of {largo.name} failed ({info.Error}), using Cotton's merge");
                return false;
            }
        }

        /// <summary>Registers the largo appearances with the appearance director (radiant variants first, the default last).</summary>
        public static void Register(SlimeDefinition largo)
        {
            var dir = Library.mainAppearanceDirector;
            var apps = largo != null ? largo.AppearancesDefault : null;
            if (dir == null || apps == null || apps.Length == 0) return;
            int last = BuildRadiants ? apps.Length - 1 : 0;
            for (int i = last; i >= 0; i--)
            {
                var a = apps[i];
                if (a == null) continue;
                Safe.Run("register appearance " + a.name, () =>
                {
                    dir.RegisterDependentAppearances(largo, a);
                    dir.UpdateChosenSlimeAppearance(largo, a);
                });
            }
        }

        /// <summary>Runs <see cref="LargoStyle.OnCreated"/> of both parents and <see cref="Created"/>.</summary>
        public static void Finish(SlimeDefinition largo, SlimeDefinition one, SlimeDefinition two)
        {
            if (largo == null) return;
            var s1 = LargoStyle.Find(one); var s2 = LargoStyle.Find(two);
            if (s1?.CreatedHook != null) Safe.Run("LargoStyle " + s1.Slime + " OnCreated", () => s1.CreatedHook(largo, two));
            if (s2?.CreatedHook != null && !ReferenceEquals(s1, s2)) Safe.Run("LargoStyle " + s2.Slime + " OnCreated", () => s2.CreatedHook(largo, one));
            var ev = Created;
            if (ev != null) Safe.Run("LargoBlend.Created", () => ev(largo));
        }

        static SlimeDefinition DefOf(SlimeAppearance app)
        {
            if (app == null) return null;
            try
            {
                var defs = Library.slimeDefinitions?.Slimes;
                if (defs == null) return null;
                foreach (var d in defs)
                {
                    var ad = d != null ? d.AppearancesDefault : null;
                    if (ad == null) continue;
                    for (int i = 0; i < ad.Length; i++) if (Same(ad[i], app)) return d;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Structures of a largo of two appearances (what <c>Library.MergeStructures</c> returns in the fork). Parents are untouched;
        /// the palette is applied to the materials only (set the appearance palette yourself).
        /// </summary>
        public static Il2CppReferenceArray<SlimeAppearanceStructure> MergeStructures(SlimeAppearance a, SlimeAppearance b, Library.LargoSettings settings)
        {
            var p = MakePlan(DefOf(a), DefOf(b), a, b, null, settings);
            var c = BuildVariant(p, null, null, a, b, -1, (a != null ? a.name : "A") + (b != null ? b.name : "B"), null);
            return Gc.Array(c.Structures.Where(s => s != null).ToList());
        }

        /// <summary>
        /// Decisions for a pair without building anything (dev command / tools). <paramref name="structures"/>: also the list of
        /// structures (dry run: parents' materials, nothing copied or recoloured, no hooks; <c>largoaudit vanilla</c>).
        /// </summary>
        public static LargoBuild Preview(SlimeDefinition one, SlimeDefinition two, Library.LargoSettings settings = Library.LargoSettings.Auto, bool structures = false)
        {
            var tpl = PickTemplate(one, two);
            var p = MakePlan(one, two, Default(one), Default(two), tpl, settings);
            if (structures)
            {
                _dry = true;
                try { return BuildVariant(p, null, null, p.App[0], p.App[1], -1, (one != null ? one.name : "A") + (two != null ? two.name : "B"), null); }
                finally { _dry = false; }
            }
            var c = new LargoBuild();
            Decide(p, c, p.App[0], p.App[1], -1);
            return c;
        }

        // ------------------------------------------------------------------ dev console

        internal static void RegisterCommands()
        {
            DevConsole.Register("blend", "blend [<largo> | <slimeA> <slimeB>] - smart largo look: template, body / face owner, colour mode and palette (no args: settings, styles, recipes)", a =>
            {
                var x = DevConsole.Arg(a, 1); var y = DevConsole.Arg(a, 2);
                if (x == null)
                {
                    DevConsole.Print($"smart largos {(Enabled ? "ON" : "OFF")}, radiant variants {(BuildRadiants ? "ON" : "OFF")}; built {Built}, radiant variants {RadiantVariants}, fallbacks {Fallbacks}");
                    DevConsole.Print($"default mode {DefaultColorMode}, part identity {PartIdentity:0.00}, split from {AutoSplitHue:0} deg, radiant weight x{RadiantWeight:0.#}");
                    foreach (var s in LargoStyle.All) DevConsole.Print("  style " + s);
                    foreach (var r in LargoRecipe.All) DevConsole.Print("  recipe " + r);
                    return;
                }
                if (y == null)
                {
                    var info = InfoOf(x);
                    DevConsole.Print(info != null ? info.ToString() : $"no record for {x} (vanilla largo, not generated yet, or SmartLargos off)");
                    return;
                }
                var one = Library.GetSlime(x); var two = Library.GetSlime(y);
                if (one == null || two == null) { DevConsole.Print("unknown slime " + (one == null ? x : y)); return; }
                var l = Largos.Find(one, two);
                if (l != null) DevConsole.Print(InfoOf(l)?.ToString() ?? l.name + ": no record (vanilla largo?)");
                DevConsole.Print("preview (Auto flags): " + Preview(one, two).Describe());
            });
            DevConsole.Register("blends", "every largo built by the smart largo engine", _ =>
            {
                foreach (var i in _info.Values) DevConsole.Print(i.ToString());
                DevConsole.Print($"{Built} built, {RadiantVariants} radiant variants, {Fallbacks} fallbacks");
            });
        }
    }
}
