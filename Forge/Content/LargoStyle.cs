using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2Cpp;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>Priorities of <see cref="LargoStyle.Body"/> / <see cref="LargoStyle.Face"/>: inside a largo the higher one wins.</summary>
    public static class LargoPriority
    {
        /// <summary>Never give this part to a largo (the other parent's part is used).</summary>
        public const int Never = -1000;
        public const int Low = -10;
        /// <summary>No preference (the engine decides).</summary>
        public const int Normal = 0;
        public const int High = 10;
        /// <summary>Always (only another Always or a <see cref="LargoRecipe"/> can override it).</summary>
        public const int Always = 1000;
    }

    /// <summary>How the colours of the two parents are combined into the largo palette.</summary>
    public enum LargoColorMode
    {
        /// <summary><see cref="Blend"/> when the parents have close hues (or one is grey), <see cref="Split"/> when they clash.</summary>
        Auto,
        /// <summary>Every palette colour is a perceptual (OKLCh) mix of both parents, weighted by their colour weights.</summary>
        Blend,
        /// <summary>The dominant parent's colours on top, the other parent's at the bottom (vertical gradient).</summary>
        Split,
        /// <summary>The dominant parent's palette with a hint (<see cref="LargoBlend.DominantHint"/>) of the other.</summary>
        Dominant,
        /// <summary>Palette of the first base slime (in Cotton's generation: the modded slime).</summary>
        First,
        /// <summary>Palette of the second base slime.</summary>
        Second,
        /// <summary>A fixed palette given by <see cref="LargoRecipe.Palette"/>.</summary>
        Custom,
    }

    /// <summary>Slime palette (the body material colours) used by the largo engine.</summary>
    public struct LargoPalette
    {
        public Color Top, Middle, Bottom, Ammo;

        public LargoPalette(Color top, Color middle, Color bottom, Color ammo) { Top = top; Middle = middle; Bottom = bottom; Ammo = ammo; }
        public LargoPalette(Color top, Color middle, Color bottom) : this(top, middle, bottom, middle) { }

        /// <summary>Palette of an appearance: the colours of its body material (what you see), else <c>_colorPalette</c>.</summary>
        public static LargoPalette Of(SlimeAppearance app) => LargoBlend.PaletteOf(app);

        /// <summary>Palette stored in a material (<c>_TopColor</c>, <c>_MiddleColor</c>, <c>_BottomColor</c>); missing colours use <paramref name="fallback"/>.</summary>
        public static LargoPalette Of(Material m, LargoPalette fallback) => new LargoPalette(
            m.GetCol("_TopColor", fallback.Top), m.GetCol("_MiddleColor", fallback.Middle), m.GetCol("_BottomColor", fallback.Bottom), fallback.Ammo);

        /// <summary>Perceptual mix (<see cref="ColorMix.Mix"/>) of every colour: <paramref name="t"/> = 0 gives <paramref name="a"/>, 1 gives <paramref name="b"/>.</summary>
        public static LargoPalette Mix(LargoPalette a, LargoPalette b, float t) => new LargoPalette(
            ColorMix.Mix(a.Top, b.Top, t), ColorMix.Mix(a.Middle, b.Middle, t), ColorMix.Mix(a.Bottom, b.Bottom, t), MixAmmo(a.Ammo, b.Ammo, t));

        internal static Color MixAmmo(Color a, Color b, float t)
        {
            if (a.a <= 0.001f) return t > 0f ? b : a; // many vanilla palettes have no ammo colour (0,0,0,0)
            if (b.a <= 0.001f) return t < 1f ? a : b;
            return ColorMix.Mix(a, b, t);
        }

        /// <summary>Writes the colours into the palette of a material.</summary>
        public void ApplyTo(Material m) { if (m == null) return; m.Palette(Top, Middle, Bottom); }

        public override string ToString() => $"{ColorMix.Hex(Top)} {ColorMix.Hex(Middle)} {ColorMix.Hex(Bottom)}";
    }

    /// <summary>
    /// How a slime behaves inside the largos it is part of. One style per slime (by <c>SlimeDefinition.name</c>), shared by every
    /// mod: register it before the largos are generated (before <c>SaveDirectorLoaded</c> ends, e.g. right before
    /// <c>Library.CreateSlimeDef</c>).
    /// <code>
    /// LargoStyle.Of("Rad").Body(LargoPriority.High).Colors(1.4f);          // glowing body wins, colours a bit stronger
    /// LargoStyle.Of("Mosaic").Body(LargoPriority.Low).Drop("MosaicShell");  // never lend the body, no shell on largos
    /// </code>
    /// Without a style the engine decides by itself (special bodies and faces win, perceptual colour blend).
    /// </summary>
    public sealed class LargoStyle
    {
        static readonly Dictionary<string, LargoStyle> _all = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Slime name (<c>SlimeDefinition.name</c>).</summary>
        public string Slime { get; }
        /// <summary>Body priority (<see cref="LargoPriority"/>); the parent with the higher priority gives the body.</summary>
        public int BodyPriority { get; private set; } = LargoPriority.Normal;
        /// <summary>Face priority; ignored when the body draws the face itself (Saber).</summary>
        public int FacePriority { get; private set; } = LargoPriority.Normal;
        /// <summary>Colour weight (1 = equal share); null = built-in value (<see cref="LargoBlend.ColorWeights"/>) or 1.</summary>
        public float? ColorWeight { get; private set; }
        /// <summary>Preferred colour mode, null = no preference.</summary>
        public LargoColorMode? ColorMode { get; private set; }
        /// <summary>How much of their own colours the parts (ears, wings, spikes ...) of this slime keep, 0..1; null = <see cref="LargoBlend.PartIdentity"/>.</summary>
        public float? PartIdentity { get; private set; }
        /// <summary>Vanilla slime whose Pink largo is used as template (prefab, effect settings, radiant variants), null = automatic.</summary>
        public string TemplateSlime { get; private set; }

        readonly HashSet<string> _drop = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _own = new(StringComparer.OrdinalIgnoreCase);
        internal Action<LargoBuild> BuildHook;
        internal Action<SlimeDefinition, SlimeDefinition> CreatedHook;

        LargoStyle(string slime) { Slime = slime; }

        /// <summary>The style of a slime (created on first use).</summary>
        public static LargoStyle Of(string slime)
        {
            if (string.IsNullOrEmpty(slime)) throw new ArgumentException("slime name is empty", nameof(slime));
            if (!_all.TryGetValue(slime, out var s)) _all[slime] = s = new LargoStyle(slime);
            return s;
        }
        public static LargoStyle Of(SlimeDefinition slime) => Of(slime != null ? slime.name : null);

        /// <summary>The style of a slime, or null when none was registered.</summary>
        public static LargoStyle Find(string slime) => slime != null && _all.TryGetValue(slime, out var s) ? s : null;
        public static LargoStyle Find(SlimeDefinition slime) => slime != null ? Find(slime.name) : null;
        public static IReadOnlyCollection<LargoStyle> All => _all.Values;

        /// <summary>Body priority. <c>Body()</c> = High: this slime lends its body (material, shape, effects) to its largos.</summary>
        public LargoStyle Body(int priority = LargoPriority.High) { BodyPriority = priority; return this; }
        /// <summary>Face priority. <c>Face()</c> = High: this slime lends its face (eyes, mouth, expressions).</summary>
        public LargoStyle Face(int priority = LargoPriority.High) { FacePriority = priority; return this; }
        /// <summary>Colour weight: 2 = this slime's colours count twice as much in the mix, 0.5 = half.</summary>
        public LargoStyle Colors(float weight) { ColorWeight = Mathf.Max(0.01f, weight); return this; }
        /// <summary>Preferred colour mode (a <see cref="LargoRecipe"/> or the other parent's heavier style can override it).</summary>
        public LargoStyle Colors(LargoColorMode mode) { ColorMode = mode; return this; }
        /// <summary>How much of their own colours this slime's parts keep in largos (0 = fully recoloured, 1 = untouched).</summary>
        public LargoStyle PartColors(float identity) { PartIdentity = Mathf.Clamp01(identity); return this; }
        /// <summary>Use the vanilla Pink + <paramref name="vanillaSlime"/> largo as template (e.g. "Boom", "Angler").</summary>
        public LargoStyle Template(string vanillaSlime) { TemplateSlime = vanillaSlime; return this; }
        /// <summary>Structures (element names) of this slime that are not carried into its largos.</summary>
        public LargoStyle Drop(params string[] elements) { foreach (var e in elements ?? Array.Empty<string>()) if (!string.IsNullOrEmpty(e)) _drop.Add(e); return this; }
        /// <summary>Structures of this slime that keep their own colours in largos ("*" = every part).</summary>
        public LargoStyle KeepOwnColors(params string[] elements) { foreach (var e in elements ?? Array.Empty<string>()) if (!string.IsNullOrEmpty(e)) _own.Add(e); return this; }
        /// <summary>Called for every appearance built for a largo of this slime (default and radiant), before it is stored.</summary>
        public LargoStyle OnBuild(Action<LargoBuild> build) { BuildHook += build; return this; }
        /// <summary>Called once per created largo of this slime: (largo, other parent).</summary>
        public LargoStyle OnCreated(Action<SlimeDefinition, SlimeDefinition> created) { CreatedHook += created; return this; }

        public bool Drops(string element) => element != null && _drop.Contains(element);
        public bool KeepsOwnColors(string element) => element != null && (_own.Contains(element) || _own.Contains("*"));

        public override string ToString()
        {
            var sb = new StringBuilder(Slime);
            if (BodyPriority != 0) sb.Append(" body ").Append(BodyPriority);
            if (FacePriority != 0) sb.Append(" face ").Append(FacePriority);
            if (ColorWeight.HasValue) sb.Append(" weight ").Append(ColorWeight.Value.ToString("0.##"));
            if (ColorMode.HasValue) sb.Append(" mode ").Append(ColorMode.Value);
            if (PartIdentity.HasValue) sb.Append(" parts ").Append(PartIdentity.Value.ToString("0.##"));
            if (TemplateSlime != null) sb.Append(" template ").Append(TemplateSlime);
            if (_drop.Count > 0) sb.Append(" drop ").Append(string.Join(",", _drop));
            if (_own.Count > 0) sb.Append(" own ").Append(string.Join(",", _own));
            if (BuildHook != null) sb.Append(" +onBuild");
            if (CreatedHook != null) sb.Append(" +onCreated");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Exact look of one pair of slimes (order does not matter). Wins over both <see cref="LargoStyle"/>s and the Cotton flags.
    /// <code>
    /// LargoRecipe.For("Rad", "Phosphor").Body("Phosphor").Face("Rad").Colors(LargoColorMode.Split).Balance("Rad", 0.7f);
    /// LargoRecipe.For("Rad", "Boom").Palette(Mat.Hex("#7CFF3A"), Mat.Hex("#3FBF1A"), Mat.Hex("#FF6A00"));
    /// </code>
    /// </summary>
    public sealed class LargoRecipe
    {
        static readonly Dictionary<string, LargoRecipe> _all = new(StringComparer.OrdinalIgnoreCase);
        static string Key(string a, string b)
        {
            a = (a ?? "").ToLowerInvariant(); b = (b ?? "").ToLowerInvariant();
            return string.CompareOrdinal(a, b) <= 0 ? a + "+" + b : b + "+" + a;
        }

        public string A { get; }
        public string B { get; }
        /// <summary>Parent that gives the body / the face / the palette (slime name), null = not fixed.</summary>
        public string BodyFrom { get; private set; }
        public string FaceFrom { get; private set; }
        public string ColorsFrom { get; private set; }
        public LargoColorMode? Mode { get; private set; }
        /// <summary>Share (0..1) of <see cref="BalanceSlime"/>'s colours in Blend / Split / Dominant.</summary>
        public float? BalanceShare { get; private set; }
        public string BalanceSlime { get; private set; }
        public LargoPalette? CustomPalette { get; private set; }
        public string TemplateSlime { get; private set; }
        /// <summary>Own icon of the largo (<see cref="Icon"/>), null = generated by <see cref="SlimeIcons"/>.</summary>
        public Sprite IconSprite { get; private set; }
        internal Action<LargoBuild> BuildHook;

        LargoRecipe(string a, string b) { A = a; B = b; }

        /// <summary>The recipe of a pair (created on first use).</summary>
        public static LargoRecipe For(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) throw new ArgumentException("slime name is empty");
            var k = Key(a, b);
            if (!_all.TryGetValue(k, out var r)) _all[k] = r = new LargoRecipe(a, b);
            return r;
        }
        public static LargoRecipe For(SlimeDefinition a, SlimeDefinition b) => For(a != null ? a.name : null, b != null ? b.name : null);
        /// <summary>The recipe of a pair, or null.</summary>
        public static LargoRecipe Find(string a, string b) => a != null && b != null && _all.TryGetValue(Key(a, b), out var r) ? r : null;
        public static IReadOnlyCollection<LargoRecipe> All => _all.Values;

        public LargoRecipe Body(string slime) { BodyFrom = slime; return this; }
        public LargoRecipe Face(string slime) { FaceFrom = slime; return this; }
        public LargoRecipe Colors(LargoColorMode mode) { Mode = mode; return this; }
        /// <summary>Exact palette of one parent.</summary>
        public LargoRecipe ColorsOf(string slime) { ColorsFrom = slime; Mode = null; return this; }
        /// <summary>Share (0..1) of <paramref name="slime"/>'s colours in the mix (0.5 = equal).</summary>
        public LargoRecipe Balance(string slime, float share) { BalanceSlime = slime; BalanceShare = Mathf.Clamp01(share); return this; }
        /// <summary>Fixed palette (mode <see cref="LargoColorMode.Custom"/>); ammo defaults to the middle colour.</summary>
        public LargoRecipe Palette(Color top, Color middle, Color bottom, Color? ammo = null)
        {
            CustomPalette = new LargoPalette(top, middle, bottom, ammo ?? middle);
            Mode = LargoColorMode.Custom;
            return this;
        }
        /// <summary>Use the vanilla Pink + <paramref name="vanillaSlime"/> largo as template.</summary>
        public LargoRecipe Template(string vanillaSlime) { TemplateSlime = vanillaSlime; return this; }
        /// <summary>Own icon of this largo instead of the generated one.</summary>
        public LargoRecipe Icon(Sprite icon) { IconSprite = icon; return this; }
        public LargoRecipe OnBuild(Action<LargoBuild> build) { BuildHook += build; return this; }

        public override string ToString()
        {
            var parts = new List<string> { A + "+" + B };
            if (BodyFrom != null) parts.Add("body " + BodyFrom);
            if (FaceFrom != null) parts.Add("face " + FaceFrom);
            if (ColorsFrom != null) parts.Add("colors of " + ColorsFrom);
            if (Mode.HasValue) parts.Add("mode " + Mode.Value);
            if (BalanceShare.HasValue) parts.Add($"balance {BalanceSlime} {BalanceShare.Value:0.##}");
            if (CustomPalette.HasValue) parts.Add("palette " + CustomPalette.Value);
            if (TemplateSlime != null) parts.Add("template " + TemplateSlime);
            if (IconSprite != null) parts.Add("icon " + IconSprite.name);
            if (BuildHook != null) parts.Add("+onBuild");
            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// One largo appearance being built (the default one or a radiant variant), passed to <see cref="LargoStyle.OnBuild"/>,
    /// <see cref="LargoRecipe.OnBuild"/> and <see cref="LargoBlend.Building"/>. <see cref="Structures"/> can still be edited;
    /// everything else is already applied to <see cref="Appearance"/>.
    /// </summary>
    public sealed class LargoBuild
    {
        /// <summary>The largo (null when called through <c>Library.MergeStructures</c> without a definition).</summary>
        public SlimeDefinition Largo { get; internal set; }
        public SlimeDefinition First { get; internal set; }
        public SlimeDefinition Second { get; internal set; }
        /// <summary>Parent appearances used for this variant (a radiant variant uses the radiant appearance of one parent).</summary>
        public SlimeAppearance FirstAppearance { get; internal set; }
        public SlimeAppearance SecondAppearance { get; internal set; }
        /// <summary>The largo appearance being built (null in <c>Library.MergeStructures</c>).</summary>
        public SlimeAppearance Appearance { get; internal set; }
        /// <summary>Vanilla largo used as template.</summary>
        public SlimeDefinition Template { get; internal set; }
        public Library.LargoSettings Settings { get; internal set; }
        /// <summary>-1 = default appearance, 0 / 1 = radiant variant of the first / second parent.</summary>
        public int RadiantSide { get; internal set; } = -1;
        public bool IsRadiant => RadiantSide >= 0;
        /// <summary>0 = first parent, 1 = second parent.</summary>
        public int BodySide { get; internal set; }
        public int FaceSide { get; internal set; }
        public SlimeDefinition BodyFrom => BodySide == 0 ? First : Second;
        public SlimeDefinition FaceFrom => FaceSide == 0 ? First : Second;
        /// <summary>Colour mode actually used (Auto resolved to Blend or Split).</summary>
        public LargoColorMode ColorMode { get; internal set; }
        /// <summary>Share of the second parent's colours (0..1).</summary>
        public float Balance { get; internal set; }
        /// <summary>Parent whose colours dominate (Split / Dominant): 0 or 1.</summary>
        public int DominantSide { get; internal set; }
        public LargoPalette Palette { get; internal set; }
        public LargoPalette FirstPalette { get; internal set; }
        public LargoPalette SecondPalette { get; internal set; }
        /// <summary>Largo body material (a copy owned by the largo, safe to edit).</summary>
        public Material BodyMaterial { get; internal set; }
        /// <summary>Structures of the appearance; add, remove or replace entries here.</summary>
        public List<SlimeAppearanceStructure> Structures { get; internal set; } = new();
        public LargoStyle FirstStyle { get; internal set; }
        public LargoStyle SecondStyle { get; internal set; }
        public LargoRecipe Recipe { get; internal set; }
        /// <summary>Why the body / face / colours were chosen (diagnostics).</summary>
        public string Reason { get; internal set; } = "";

        /// <summary>The other parent.</summary>
        public SlimeDefinition Other(SlimeDefinition parent) => parent != null && First != null && parent.Pointer == First.Pointer ? Second : First;
        /// <summary>True when one of the parents has this name.</summary>
        public bool Has(string slime) => slime != null && ((First != null && string.Equals(First.name, slime, StringComparison.OrdinalIgnoreCase)) || (Second != null && string.Equals(Second.name, slime, StringComparison.OrdinalIgnoreCase)));
        /// <summary>First structure with this element name, or null.</summary>
        public SlimeAppearanceStructure Structure(string element) => Structures.FirstOrDefault(s => s?.Element != null && string.Equals(s.Element.Name, element, StringComparison.OrdinalIgnoreCase));
        /// <summary>A copy of <paramref name="source"/> painted with the largo palette, keeping <paramref name="identity"/> (0..1) of its own colours.</summary>
        public Material Recolored(Material source, float identity = 0f, string name = null)
        {
            if (source == null) return null;
            var m = Mat.Copy(source, name ?? LargoBlend.CopyName(this, source, ""));
            LargoPalette.Mix(Palette, LargoPalette.Of(source, Palette), Mathf.Clamp01(identity)).ApplyTo(m);
            return m;
        }

        public string Describe()
        {
            string n(SlimeDefinition d) => d != null ? d.name : "?";
            return $"{(Largo != null ? Largo.name : n(First) + n(Second))}{(IsRadiant ? " [radiant " + n(RadiantSide == 0 ? First : Second) + "]" : "")}: " +
                   $"template {n(Template)}, body {n(BodyFrom)}, face {n(FaceFrom)}, colours {ColorMode} {Balance:0.00} ({Palette}), {Structures.Count} structures" +
                   (string.IsNullOrEmpty(Reason) ? "" : " - " + Reason);
        }
    }
}
