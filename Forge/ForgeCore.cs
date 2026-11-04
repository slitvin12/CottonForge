using System;
using HarmonyLib;
using MelonLoader;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Start-up and per-frame driver of the Forge layer, called by <see cref="CottonMain"/> (one MelonMod per assembly).
    /// Forge = the declarative content API (<see cref="ForgeMod{M}"/>, <see cref="SlimeBuilder"/>), the helpers every
    /// content mod ends up writing (GC safety, materials, faces, FX, sounds, spawns, localization, dev console) and the
    /// repairs that hook game methods (<c>Forge/Fixes</c>).
    /// </summary>
    public static class ForgeCore
    {
        /// <summary>Version of this Cotton fork (also in <c>MelonInfo</c>).</summary>
        public const string Version = "0.3.2";
        /// <summary>Upstream Cotton release this fork is based on.</summary>
        public const string BasedOn = "0.3.1";
        /// <summary>
        /// Build id from <c>AssemblyInformationalVersion</c>, e.g. "0.3.2+4c040c4.dirty" (git revision, ".dirty" = built with
        /// uncommitted changes). AssemblyVersion stays 1.0.0.0 for binary compatibility with mods built against Cotton.
        /// </summary>
        public static string Build
        {
            get
            {
                try
                {
                    var a = typeof(ForgeCore).Assembly;
                    var info = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(a, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
                    return $"{info?.InformationalVersion ?? Version}, assembly {a.GetName().Version}";
                }
                catch { return Version; }
            }
        }
        /// <summary>Patch classes applied / switched off (Prepare returned false) / failed.</summary>
        public static int PatchesApplied, PatchesOff, PatchesFailed;
        internal static MelonLogger.Instance Logger;
        static bool _init;

        internal static void Init(MelonMod host)
        {
            if (_init) return;
            _init = true;
            Logger = host.LoggerInstance;
            ForgeConfig.Init();
            PatchAll(host.HarmonyInstance);
            Safe.Run("ForgeModules.InitAll", ForgeModules.InitAll);
            ForgeLog.Msg($"CottonForge {Version} (fork of Cotton Library {BasedOn}; build {Build}): patches {PatchesApplied} on, {PatchesOff} off, {PatchesFailed} failed; safe GC {ForgeConfig.SafeGc.Value}; dev console {ForgeConfig.DevConsole.Value}");
        }

        /// <summary>
        /// Same classes as Harmony's PatchAll (class-level [HarmonyPatch]), but each one separately: one target that changed
        /// in a game update must not disable all other patches (PatchAll stops at the first exception).
        /// </summary>
        static void PatchAll(HarmonyLib.Harmony harmony)
        {
            Type[] types;
            try { types = typeof(ForgeCore).Assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
            foreach (var t in types)
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try
                {
                    var r = harmony.CreateClassProcessor(t).Patch();
                    if (r == null || r.Count == 0) PatchesOff++; else PatchesApplied++;
                }
                catch (Exception e)
                {
                    PatchesFailed++;
                    ForgeLog.Error($"Patch {t.FullName} failed: {e.InnerException?.Message ?? e.Message}");
                }
            }
        }

        internal static void Tick()
        {
            Safe.Run("Mat.Tick", () => Mat.Tick());
            Safe.Run("Faces.Tick", Faces.Tick);
            Safe.Run("PediaArt.Tick", PediaArt.Tick);
            Scheduler.Tick();
            ForgeModules.Tick();
            if (RadHud.Enabled) Safe.Run("RadHud.Tick", RadHud.Tick);
            if (LiveConfig.Count > 0) Safe.Run("LiveConfig.Tick", LiveConfig.Tick);
            if (ForgeConfig.DevConsole.Value) Safe.Run("DevConsole.Tick", DevConsole.Tick);
        }
    }
}
