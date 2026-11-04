using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CottonLibrary;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using Object = UnityEngine.Object;

namespace CottonLibrary.Forge
{
    /// <summary>Cotton's food groups as flags for <see cref="SlimeBuilder.Eats(FoodGroups)"/>.</summary>
    [Flags]
    public enum FoodGroups { None = 0, Veggies = 1, Fruits = 2, Meat = 4, Nectar = 8, AllFood = 16 }

    /// <summary>
    /// Declarative slime: describe it, Forge creates it through Cotton with every known Cotton workaround applied.
    /// <code>
    /// Slime("Rad").Colors(Mat.Hex("#6BFF5C"), Mat.Hex("#1AE02E"), Mat.Hex("#088C17"))
    ///     .Eats(FoodGroups.Veggies).Favorite("OcaOca", "Beet").Toys("PowerCell")
    ///     .Plort(36, 9).Largo()
    ///     .Spawn(Library.SpawnLocations.EmberValley, 0.08f)
    ///     .Behaviour&lt;RadAura&gt;()
    ///     .Customize(b =&gt; b.Body.Col("_TopColor", Color.green));
    /// </code>
    /// What creation does on top of plain Cotton: clones every material shared with the template (Cotton leaves the last
    /// structure shared, so recolouring it also recoloured Pink), fixes the vac/HUD palette (Cotton reads it from the still pink
    /// material), clears the Pink full art and radiant variant, sets the diet without Cotton's duplicate group bug, adds the
    /// plort to the plort group once, creates the pedia entry when the CSV has the keys, registers spawns with a real share of
    /// the zone's slime spawns and attaches behaviours to spawned actors (not to prefabs, which Cotton copies for every largo).
    /// </summary>
    public sealed class SlimeBuilder
    {
        /// <summary>Largo settings used by <see cref="Largo(Library.LargoSettings)"/> without arguments.</summary>
        /// <remarks>Fork: includes <c>Auto</c> (the smart largo engine decides; the Keep* flags are the fallback with SmartLargos off).</remarks>
        public const Library.LargoSettings DefaultLargoSettings = Library.LargoSettings.KeepSecondBody | Library.LargoSettings.KeepSecondFace | Library.LargoSettings.MergeColors | Library.LargoSettings.Auto;

        // ------------------------------------------------------------------ identity
        /// <summary>Internal SR2 name (definition name, prefab "slime" + Name, plort Name + "Plort"). Never change it after release: saves use it.</summary>
        public string Name { get; }
        /// <summary>Localization key part (<c>l.Key_slime</c>), lower-case <see cref="Name"/> by default.</summary>
        public string KeyName { get; private set; }
        /// <summary>Save reference id of the slime.</summary>
        public string RefId => "SlimeDefinition." + Name;
        /// <summary>Save reference id of the plort.</summary>
        public string PlortRefId => "IdentifiableType." + Name + "Plort";
        /// <summary>Embedded assets of the declaring mod.</summary>
        public ModAssets Assets { get; }
        /// <summary>Assembly of the declaring mod (CSV keys, assets).</summary>
        public Assembly ModAssembly { get; }

        // ------------------------------------------------------------------ results
        /// <summary>The created definition (null before creation or when disabled).</summary>
        public SlimeDefinition Def { get; private set; }
        /// <summary>The created plort (null without plort).</summary>
        public IdentifiableType PlortType { get; private set; }
        /// <summary>Default appearance of <see cref="Def"/>.</summary>
        public SlimeAppearance Appearance => Def != null && Def.AppearancesDefault != null && Def.AppearancesDefault.Length > 0 ? Def.AppearancesDefault[0] : null;
        /// <summary>Body material (structure 0) of <see cref="Appearance"/>.</summary>
        public Material Body => Looks.BodyMaterial(Appearance);
        /// <summary>Icon used for the slime.</summary>
        public Sprite IconSprite { get; private set; }
        public bool IsCreated => Def != null;
        /// <summary>True when <see cref="Enabled(Func{bool})"/> returned false.</summary>
        public bool IsDisabled { get; private set; }
        /// <summary>Every largo that has this slime as a parent (after largo generation).</summary>
        public IEnumerable<SlimeDefinition> Largos => Def != null ? CottonLibrary.Forge.Largos.Of(Def) : Enumerable.Empty<SlimeDefinition>();

        // ------------------------------------------------------------------ spec
        public Color32 Top { get; private set; } = new Color32(200, 200, 200, 255);
        public Color32 Middle { get; private set; } = new Color32(160, 160, 160, 255);
        public Color32 Bottom { get; private set; } = new Color32(110, 110, 110, 255);
        public Color32 AmmoColor { get; private set; } = new Color32(160, 160, 160, 255);
        public bool HasPlort { get; private set; } = true;
        public float PlortValue { get; private set; } = 30f;
        public float PlortSaturation { get; private set; } = 10f;
        public bool Largoable { get; private set; }
        public Library.LargoSettings LargoSettings { get; private set; } = DefaultLargoSettings;
        public Library.SpawnLocations Zones { get; private set; }
        /// <summary>Current spawn share (0 = no natural spawns).</summary>
        public float SpawnShare => _share != null ? Safe.Run(Name + " share", _share, 0f) : 0f;
        public string TemplateName { get; private set; } = "Pink";
        public bool KeepsRadiant { get; private set; }

        bool _colors;
        string _iconName, _plortIconName, _pediaArtName;
        Sprite _icon, _plortIcon, _pediaArt;
        FoodGroups _groupFlags;
        readonly List<IdentifiableTypeGroup> _groups = new();
        readonly List<string[]> _favHints = new(), _extraHints = new();
        readonly List<Func<IdentifiableType>> _favLazy = new(), _extraLazy = new();
        string[] _toyHints = Array.Empty<string>();
        Func<float> _share;
        Func<bool> _enabled;
        Action<SlimeBuilder> _customize;
        Action<SlimeDefinition> _customizeLargo;
        internal readonly List<(Action<GameObject> add, bool largos)> Spawned = new();
        bool _linked;

        /// <summary>
        /// New slime spec. Inside a <see cref="ForgeMod{M}"/> use its <c>Slime(name)</c> instead; standalone use: create it in your
        /// mod's <c>SaveDirectorLoaded</c>, call <see cref="Create"/>, then <see cref="Link"/> after all your slimes exist.
        /// </summary>
        public SlimeBuilder(string name, Assembly modAssembly = null)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace)) throw new ArgumentException("Slime name must be a non-empty identifier without spaces", nameof(name));
            Name = name;
            KeyName = name.ToLowerInvariant();
            ModAssembly = modAssembly ?? Assembly.GetCallingAssembly();
            Assets = ModAssets.Of(ModAssembly);
        }

        // ------------------------------------------------------------------ fluent spec
        /// <summary>Localization key part (default: lower-case name).</summary>
        public SlimeBuilder Key(string key) { KeyName = key; return this; }

        /// <summary>Body gradient (top, middle, bottom) and vac/HUD colour (default: middle).</summary>
        public SlimeBuilder Colors(Color32 top, Color32 middle, Color32 bottom, Color32? ammo = null)
        {
            Top = top; Middle = middle; Bottom = bottom; AmmoColor = ammo ?? middle; _colors = true; return this;
        }

        /// <summary>Colours as hex strings ("#RRGGBB").</summary>
        public SlimeBuilder Colors(string top, string middle, string bottom, string ammo = null) =>
            Colors(Mat.Hex(top), Mat.Hex(middle), Mat.Hex(bottom), ammo != null ? Mat.Hex(ammo) : (Color32?)null);

        /// <summary>Slime icon: embedded PNG name (default <c>iconSlime&lt;Name&gt;.png</c>).</summary>
        public SlimeBuilder Icon(string asset) { _iconName = asset; return this; }
        public SlimeBuilder Icon(Sprite sprite) { _icon = sprite; return this; }
        /// <summary>Plort icon: embedded PNG name (default <c>iconPlort&lt;Name&gt;.png</c>).</summary>
        public SlimeBuilder PlortIcon(string asset) { _plortIconName = asset; return this; }
        public SlimeBuilder PlortIcon(Sprite sprite) { _plortIcon = sprite; return this; }
        /// <summary>Big slimepedia picture (default <c>pediaSlime&lt;Name&gt;.png</c>, else the icon).</summary>
        public SlimeBuilder PediaArt(string asset) { _pediaArtName = asset; return this; }
        public SlimeBuilder PediaArt(Sprite sprite) { _pediaArt = sprite; return this; }

        /// <summary>Vanilla slime whose appearance and prefab are copied (default "Pink"; the definition is always a Pink clone, Cotton does that).</summary>
        public SlimeBuilder Template(string slimeName) { TemplateName = slimeName; return this; }

        /// <summary>Has a plort with this market value and saturation (default 30 / 10).</summary>
        public SlimeBuilder Plort(float value, float saturation = 10f) { HasPlort = true; PlortValue = value; PlortSaturation = saturation; return this; }
        /// <summary>No plort (e.g. a slime that is "debugged" instead of fed).</summary>
        public SlimeBuilder NoPlort() { HasPlort = false; return this; }

        /// <summary>Food groups the slime eats.</summary>
        public SlimeBuilder Eats(FoodGroups groups) { _groupFlags |= groups; return this; }
        /// <summary>Food groups the slime eats.</summary>
        public SlimeBuilder Eats(params IdentifiableTypeGroup[] groups) { _groups.AddRange(groups.Where(g => g != null)); return this; }

        /// <summary>Favourite food: the first type matching one of the hints inside the slime's food groups (<see cref="Idents.FindByHints"/>).</summary>
        public SlimeBuilder Favorite(params string[] hints) { _favHints.Add(hints); return this; }
        /// <summary>Favourite resolved after all slimes of the mod exist (e.g. another slime's plort).</summary>
        public SlimeBuilder Favorite(Func<IdentifiableType> food) { _favLazy.Add(food); return this; }
        /// <summary>Extra food outside the groups: the first type matching one of the hints (any group).</summary>
        public SlimeBuilder AlsoEats(params string[] hints) { _extraHints.Add(hints); return this; }
        /// <summary>Extra food resolved after all slimes of the mod exist: <c>.AlsoEats(() =&gt; rad.PlortType)</c>.</summary>
        public SlimeBuilder AlsoEats(Func<IdentifiableType> food) { _extraLazy.Add(food); return this; }
        /// <summary>Favourite toy: the first <c>ToyDefinition</c> matching one of the hints.</summary>
        public SlimeBuilder Toys(params string[] hints) { _toyHints = hints ?? Array.Empty<string>(); return this; }

        /// <summary>Cotton generates largos with every other largoable slime.</summary>
        public SlimeBuilder Largo(Library.LargoSettings settings = DefaultLargoSettings) { Largoable = true; LargoSettings = settings; return this; }
        /// <summary>Largoable with a look in its largos: <c>.Largo(s =&gt; s.Body(LargoPriority.High).Colors(1.5f))</c> (see <see cref="LargoStyle"/>).</summary>
        public SlimeBuilder Largo(Action<LargoStyle> style, Library.LargoSettings settings = DefaultLargoSettings)
        {
            Largoable = true; LargoSettings = settings;
            style?.Invoke(LargoStyle.Of(Name));
            return this;
        }
        /// <summary>The <see cref="LargoStyle"/> of this slime (how it looks in largos).</summary>
        public LargoStyle LargoLook => LargoStyle.Of(Name);
        public SlimeBuilder NoLargo() { Largoable = false; return this; }

        /// <summary>Natural spawns: about <paramref name="share"/> (0..0.9) of the wild slime spawns of the zones.</summary>
        public SlimeBuilder Spawn(Library.SpawnLocations zones, float share) => Spawn(zones, () => share);
        /// <summary>Natural spawns with a share read when spawners wake up (e.g. from a config entry).</summary>
        public SlimeBuilder Spawn(Library.SpawnLocations zones, Func<float> share) { Zones = zones; _share = share; return this; }
        public SlimeBuilder Spawn(Library.SpawnLocations zones, MelonPreferences_Entry<float> share) => Spawn(zones, () => share.Value);

        /// <summary>Skip creation when this returns false (read once, at creation). Changing it later needs a restart.</summary>
        public SlimeBuilder Enabled(Func<bool> enabled) { _enabled = enabled; return this; }
        public SlimeBuilder Enabled(MelonPreferences_Entry<bool> entry) => Enabled(() => entry.Value);

        /// <summary>Keep the radiant variant inherited from Pink (cleared by default: it would be a radiant pink slime).</summary>
        public SlimeBuilder KeepRadiant(bool keep = true) { KeepsRadiant = keep; return this; }

        string _vFun, _vFear, _vDamage, _vJump;
        /// <summary>
        /// Own voice lines from WAV files embedded in the mod (names without ".wav", '|' separates variants; null keeps the
        /// template's line). See <see cref="CottonLibrary.Forge.Voices"/>.
        /// </summary>
        public SlimeBuilder Voices(string fun, string fear = null, string damage = null, string jump = null)
        {
            _vFun = fun; _vFear = fear; _vDamage = damage; _vJump = jump;
            return this;
        }

        /// <summary>Your own setup after the standard one (materials, structures, faces, sounds ...). Can be called several times.</summary>
        public SlimeBuilder Customize(Action<SlimeBuilder> a) { _customize += a; return this; }
        /// <summary>Runs once for every largo with this slime as a parent, right after largo generation (GC paused).</summary>
        public SlimeBuilder CustomizeLargo(Action<SlimeDefinition> a) { _customizeLargo += a; return this; }

        /// <summary>Adds the component to every spawned actor of this slime (and its largos). Injected types are registered automatically.</summary>
        public SlimeBuilder Behaviour<T>(bool largos = true) where T : Component
        {
            Behaviours.Register<T>();
            Spawned.Add((go => { if (go.GetComponent<T>() == null) go.AddComponent<T>(); }, largos));
            return this;
        }

        /// <summary>Called for every spawned actor of this slime (and its largos) in <c>SlimeEat.Awake</c>.</summary>
        public SlimeBuilder OnSpawn(Action<GameObject> a, bool largos = true) { if (a != null) Spawned.Add((a, largos)); return this; }

        // ------------------------------------------------------------------ creation
        /// <summary>Creates the slime (once). Must run in <c>SaveDirectorLoaded</c> (Cotton's groups and Pink exist then). Returns null when disabled.</summary>
        public SlimeDefinition Create()
        {
            if (Def != null || IsDisabled) return Def;
            if (_enabled != null && !Safe.Run(Name + " enabled check", _enabled, true))
            {
                IsDisabled = true;
                ForgeLog.Msg($"{Name} slime disabled in config");
                return null;
            }
            var pink = Library.GetSlime("Pink") ?? throw new InvalidOperationException("Pink slime not found: create slimes in SaveDirectorLoaded");
            var tpl = TemplateName == "Pink" ? pink : Library.GetSlime(TemplateName);
            if (tpl == null) { ForgeLog.Warn($"{Name}: template slime {TemplateName} not found, using Pink"); tpl = pink; }
            if (!_colors) ForgeLog.Warn($"{Name}: no Colors(...) given, the slime is grey");
            // standalone builder users (no ForgeMod) still get key lookup for names/pedia
            if (ModAssembly != null && !Loc.Keys(ModAssembly).Any()) Loc.IndexKeys(ModAssembly);
            Gc.Window("create " + Name, () => Build(tpl));
            SlimeRegistry.Add(this);
            ForgeLog.Msg($"Created {Name} slime{(PlortType != null ? " + plort" : "")}{(Largoable ? ", largoable" : "")}");
            return Def;
        }

        Sprite LoadSprite(Sprite given, string asset, string fallbackAsset)
        {
            if (given != null) return given;
            var n = asset ?? fallbackAsset;
            return n != null && Assets.Has(n.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? n : n + ".png") ? Assets.Sprite(n) : null;
        }

        void Build(SlimeDefinition tpl)
        {
            IconSprite = LoadSprite(_icon, _iconName, "iconSlime" + Name);
            if (IconSprite == null) { ForgeLog.Warn($"{Name}: no icon (iconSlime{Name}.png), using the template's"); IconSprite = tpl.icon; }

            // ---- plort
            if (HasPlort)
            {
                var plortIcon = LoadSprite(_plortIcon, _plortIconName, "iconPlort" + Name);
                if (plortIcon == null) { ForgeLog.Warn($"{Name}: no plort icon (iconPlort{Name}.png)"); plortIcon = Library.GetPlort("PinkPlort")?.icon; }
                PlortType = Library.CreatePlortType(Name, Middle, plortIcon, PlortRefId, PlortValue, PlortSaturation);
                var plortObj = Library.CreatePrefab("plort" + Name, Library.GetPlort("PinkPlort").prefab);
                PlortType.localizedName = LocName("l." + KeyName + "_plort", Name + " Plort");
                PlortType.SetObjectPrefab(plortObj);
                plortObj.SetObjectIdent(PlortType);
                Library.SetPlortColor(Top, Middle, Bottom, plortObj);
                Idents.AddToGroup("VaccableNonLiquids", PlortType);
                Idents.AddToGroup(Library.plorts, PlortType);
            }

            // ---- definition, prefab
            var tplApp = tpl.AppearancesDefault[0];
            Def = Library.CreateSlimeDef(Name, AmmoColor, IconSprite, Object.Instantiate(tplApp), Name + "Default", RefId, Largoable, LargoSettings);
            var def = Def;
            def.localizedName = LocName("l." + KeyName + "_slime", Name + " Slime");
            var obj = Library.CreatePrefab("slime" + Name, tpl.prefab);
            def.SetObjectPrefab(obj);
            obj.SetObjectIdent(def);

            // ---- appearance: unshare materials, colours, palette
            var app = def.AppearancesDefault[0];
            LargoParity.Repoint(obj, tpl, def, tplApp, app);   // prefab components still pointing at the template
            int cloned = Looks.UnshareMaterials(app, tplApp, Name);
            def.SetSlimeColor(Top, Middle, Bottom);
            if (app.Structures.Length > 0 && app.Structures[0].DefaultMaterials.Length > 0) app.SetPalette(app.Structures[0].DefaultMaterials[0], def);
            app._splatColor = Middle;
            app.SetSplatColor();
            app._icon = IconSprite;

            // ---- diet
            var groups = new List<IdentifiableTypeGroup>();
            void G(FoodGroups f, IdentifiableTypeGroup g) { if ((_groupFlags & f) != 0 && g != null && !groups.Any(x => x.Pointer == g.Pointer)) groups.Add(g); }
            G(FoodGroups.Veggies, Library.veggies); G(FoodGroups.Fruits, Library.fruits); G(FoodGroups.Meat, Library.meat);
            G(FoodGroups.Nectar, Library.nectar); G(FoodGroups.AllFood, Library.food);
            foreach (var g in _groups) if (!groups.Any(x => x.Pointer == g.Pointer)) groups.Add(g);
            def.Diet.MajorFoodIdentifiableTypeGroups = Gc.Array(groups);
            if (PlortType != null) def.AddProduceIdent(PlortType);
            def.Diet.FavoriteProductionCount = 2;
            foreach (var hints in _favHints)
            {
                IdentifiableType fav = null;
                foreach (var g in groups) { fav = Idents.FindByHints(hints, IdentKind.Food, g); if (fav != null) break; }
                fav ??= Idents.FindByHints(hints, IdentKind.Food);
                if (fav != null) { def.AddFavorite(fav); ForgeLog.Msg($"  {Name} favourite food: {fav.name}"); }
                else ForgeLog.Warn($"  {Name}: no favourite food matches {string.Join("/", hints)}");
            }
            foreach (var hints in _extraHints)
            {
                var extra = Idents.FindByHints(hints, IdentKind.Any);
                if (extra != null) def.AddExtraEatIdent(extra);
                else ForgeLog.Warn($"  {Name}: no extra food matches {string.Join("/", hints)}");
            }
            var toy = _toyHints.Length > 0 ? Idents.FindByHints(_toyHints, IdentKind.Toy) : null;
            def.FavoriteToyIdents = toy != null ? Gc.Array(new List<ToyDefinition> { toy.Cast<ToyDefinition>() }) : Gc.Array(new List<ToyDefinition>());
            if (toy != null) ForgeLog.Msg($"  {Name} favourite toy: {toy.name}");
            def.RefreshEatmap();
            Idents.AddToGroup("VaccableNonLiquids", def);
            Safe.Run($"{Name} groups", () => { LargoParity.JoinBaseGroups(def); });   // groups of every vanilla base slime

            // ---- voices
            if (_vFun != null || _vFear != null || _vDamage != null || _vJump != null)
                CottonLibrary.Forge.Voices.Apply(def, Assets, _vFun, _vFear, _vDamage, _vJump);

            // ---- user customization
            if (_customize != null)
                foreach (Action<SlimeBuilder> a in _customize.GetInvocationList())
                    Safe.Run($"{Name} Customize", () => a(this));

            // ---- Pink leftovers (after Customize: it may have changed materials)
            Safe.Run($"{Name} palette", () =>
            {
                var pal = app._colorPalette;
                pal.Top = Top; pal.Middle = Middle; pal.Bottom = Bottom; pal.Ammo = AmmoColor;
                app._colorPalette = pal;
                app.ColorPaletteNew = pal;
            });
            Safe.Run($"{Name} full art", () =>
            {
                def._fullArt = new AssetReferenceT<Sprite>("");
                def._requiresFullArt = false;
                app._fullArt = new AssetReferenceT<Sprite>("");
            });
            if (!KeepsRadiant) Looks.ClearRadiant(def);
            CottonLibrary.Forge.PediaArt.Register(def, LoadSprite(_pediaArt, _pediaArtName, "pediaSlime" + Name) ?? IconSprite);

            // ---- slimepedia
            string k = KeyName + "_slime";
            if (Loc.Has(ModAssembly, "m.intro." + k))
                Safe.Run($"{Name} pedia", () => Library.CreatePediaEntryForSlime(def, k,
                    Loc.String("m.intro." + k, "Pedia"), Loc.String("m.slimeology." + k, "Pedia"),
                    Loc.String("m.risks." + k, "Pedia"), Loc.String("m.plortonomics." + k, "Pedia")));
            else ForgeLog.Debug($"{Name}: no m.intro.{k} in the CSV, no pedia entry");

            // ---- runtime hooks
            foreach (var (add, _) in Spawned) Behaviours.Attach(def, add);
            if (_share != null && Zones != 0) Spawns.Add(def, Zones, _share);
            ForgeLog.Debug($"{Name}: {cloned} shared materials cloned, groups {string.Join(",", groups.Select(g => g.name))}");
        }

        LocalizedString LocName(string key, string fallback) =>
            Loc.Has(ModAssembly, key) || Loc.Has(key) ? Loc.String(key) : Loc.Static(fallback, key);

        /// <summary>
        /// Second pass after all slimes of the mod are created: lazy favourites / extra foods (another slime's plort ...).
        /// <see cref="ForgeMod{M}"/> calls it; standalone users call it once after their last <see cref="Create"/>.
        /// </summary>
        public void Link()
        {
            if (_linked || Def == null) return;
            _linked = true;
            bool changed = false;
            foreach (var f in _favLazy)
            {
                var t = Safe.Run($"{Name} favourite", f);
                if (t != null) { Def.AddFavorite(t); changed = true; ForgeLog.Msg($"  {Name} favourite food: {t.name}"); }
            }
            foreach (var f in _extraLazy)
            {
                var t = Safe.Run($"{Name} extra food", f);
                if (t != null) { Def.AddExtraEatIdent(t); changed = true; ForgeLog.Msg($"  {Name} also eats: {t.name}"); }
            }
            if (changed) Safe.Run($"{Name} eatmap", () => Def.RefreshEatmap());
        }

        internal void RunCustomizeLargo(SlimeDefinition largo)
        {
            if (_customizeLargo == null) return;
            foreach (Action<SlimeDefinition> a in _customizeLargo.GetInvocationList())
                Safe.Run($"{Name} CustomizeLargo {largo.name}", () => a(largo));
        }

        public override string ToString() => $"SlimeBuilder({Name}{(Def != null ? ", created" : IsDisabled ? ", disabled" : "")})";
    }
}
