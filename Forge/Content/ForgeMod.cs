using System;
using System.Collections.Generic;
using System.Linq;
using CottonLibrary;
using MelonLoader;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Base class for content mods. It is a <c>CottonModInstance&lt;M&gt;</c> (Cotton registers and saves everything as usual)
    /// with a fixed, safe life cycle:
    /// <list type="number">
    /// <item><c>OnEarlyInitializeMelon</c>: the mod's <c>localized.csv</c> (embedded) is loaded into Cotton (do NOT call
    /// <c>Library.AddLanguages</c> yourself; override <see cref="LocalizationFile"/> to rename or disable).</item>
    /// <item><c>SaveDirectorLoaded</c> (main menu, once, GC paused): <see cref="DeclareContent"/> -> every <see cref="Slime"/> is
    /// created -> cross references linked -> diets refreshed -> largo names registered -> <see cref="OnContentCreated"/>.</item>
    /// <item>Cotton generates the largos; Forge repairs them, then calls <c>AutoLargosLoaded()</c> on every Cotton mod.</item>
    /// <item><c>OnSavedGameLoaded</c>: <see cref="OnFirstGameLoaded"/> once, <see cref="OnGameLoaded"/> every time.</item>
    /// </list>
    /// <code>
    /// [assembly: MelonInfo(typeof(MyMod), "My Slimes", "1.0.0", "me")]
    /// [assembly: MelonGame("MonomiPark", "SlimeRancher2")]
    /// [assembly: MelonAdditionalDependencies("CottonLibrary")]
    /// public class MyMod : ForgeMod&lt;MyMod&gt;
    /// {
    ///     protected override void DeclareContent()
    ///     {
    ///         Slime("Rad").Colors("#6BFF5C", "#1AE02E", "#088C17").Eats(FoodGroups.Veggies).Plort(36, 9).Largo()
    ///             .Spawn(Library.SpawnLocations.EmberValley, 0.08f);
    ///     }
    /// }
    /// </code>
    /// </summary>
    public abstract class ForgeMod<M> : CottonModInstance<M> where M : CottonMod
    {
        readonly List<SlimeBuilder> _slimes = new();
        bool _contentDone, _firstLoadDone;

        /// <summary>Embedded assets of this mod.</summary>
        public ModAssets Assets => ModAssets.Of(GetType().Assembly);
        /// <summary>Slimes declared by this mod (also the disabled ones).</summary>
        public IReadOnlyList<SlimeBuilder> Slimes => _slimes;
        /// <summary>True after <see cref="DeclareContent"/> ran and the slimes were created.</summary>
        public bool ContentCreated => _contentDone;

        /// <summary>Embedded CSV (without ".csv") loaded into Cotton at start-up; null = none. Default "localized".</summary>
        protected virtual string LocalizationFile => "localized";

        /// <summary>Declares a slime (or returns the one already declared with that name). Call from <see cref="DeclareContent"/>.</summary>
        protected SlimeBuilder Slime(string name)
        {
            var b = _slimes.FirstOrDefault(x => x.Name == name);
            if (b != null) return b;
            b = new SlimeBuilder(name, GetType().Assembly);
            _slimes.Add(b);
            return b;
        }

        /// <summary>A declared slime of this mod by name, or null.</summary>
        public SlimeBuilder GetSlime(string name) => _slimes.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Declare slimes (<see cref="Slime"/>), enable hidden foods (<see cref="Foods.EnableHidden"/>) etc. Runs once in the
        /// main menu inside Cotton's <c>SaveDirectorLoaded</c>: Cotton's groups (<c>Library.veggies</c> ...) and vanilla slimes exist.
        /// </summary>
        protected virtual void DeclareContent() { }

        /// <summary>After all slimes of this mod exist (before largo generation). Extra setup that needs the definitions.</summary>
        protected virtual void OnContentCreated() { }

        /// <summary>First time a save is loaded in this session (largos are repaired and customized already).</summary>
        protected virtual void OnFirstGameLoaded() { }

        /// <summary>Every time a save is loaded.</summary>
        protected virtual void OnGameLoaded() { }

        public sealed override void OnEarlyInitializeMelon()
        {
            base.OnEarlyInitializeMelon();
            var file = LocalizationFile;
            if (file == null) return;
            int n = Safe.Run(Info.Name + " localization", () => Loc.AddLanguages(GetType().Assembly, file), 0);
            if (n > 0) LoggerInstance.Msg($"[Cotton] {n} localization keys loaded from {file}.csv");
        }

        public sealed override void SaveDirectorLoaded()
        {
            base.SaveDirectorLoaded();
            if (_contentDone) return;
            _contentDone = true;
            var asm = GetType().Assembly;
            if (!Loc.Keys(asm).Any()) Loc.IndexKeys(asm, LocalizationFile ?? "localized");
            Gc.Window(Info.Name + " content", () =>
            {
                Safe.Run(Info.Name + ".DeclareContent", DeclareContent);
                foreach (var b in _slimes)
                {
                    try { b.Create(); }
                    catch (Exception e) { LoggerInstance.Error($"Creating slime {b.Name} failed: {e}"); }
                }
                foreach (var b in _slimes) Safe.Run($"link {b.Name}", b.Link);
                if (Foods.DietsDirty)
                {
                    Foods.DietsDirty = false;
                    int n = Idents.RefreshBaseDiets();
                    ForgeLog.Msg($"  refreshed {n} base slime diets (new foods)");
                }
                Loc.PreRegisterLargos(asm);
                Safe.Run(Info.Name + ".OnContentCreated", OnContentCreated);
            });
            int made = _slimes.Count(b => b.IsCreated), off = _slimes.Count(b => b.IsDisabled);
            LoggerInstance.Msg($"[Cotton] content ready: {made} slime(s){(off > 0 ? $", {off} disabled" : "")}");
        }

        public sealed override void OnSavedGameLoaded()
        {
            base.OnSavedGameLoaded();
            if (!_firstLoadDone)
            {
                _firstLoadDone = true;
                Safe.Run(Info.Name + ".OnFirstGameLoaded", OnFirstGameLoaded);
            }
            Safe.Run(Info.Name + ".OnGameLoaded", OnGameLoaded);
        }
    }
}
