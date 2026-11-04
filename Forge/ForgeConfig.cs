using MelonLoader;

namespace CottonLibrary.Forge
{
    /// <summary>Settings of CottonForge (fork of Cotton Library), category <c>[CottonLibrary]</c> in <c>UserData\MelonPreferences.cfg</c>.</summary>
    public static class ForgeConfig
    {
        public static MelonPreferences_Category Category;

        public static MelonPreferences_Entry<bool> SafeGc;
        public static MelonPreferences_Entry<bool> FixDuplicateSlimes;
        public static MelonPreferences_Entry<bool> FixLargoAI;
        public static MelonPreferences_Entry<float> LocalizationInterval;
        public static MelonPreferences_Entry<bool> CallAutoLargosLoaded;
        public static MelonPreferences_Entry<bool> DevConsole;
        public static MelonPreferences_Entry<bool> Verbose;
        public static MelonPreferences_Entry<string> DisabledModules;
        public static MelonPreferences_Entry<string> FireModeKey;
        public static MelonPreferences_Entry<bool> SmartLargos;
        public static MelonPreferences_Entry<bool> LargoRadiants;
        public static MelonPreferences_Entry<bool> LargoParity;
        public static MelonPreferences_Entry<bool> LargoIcons;

        internal static void Init()
        {
            if (Category != null) return;
            Category = MelonPreferences.CreateCategory("CottonLibrary", "CottonForge"); // id kept: existing settings stay
            SafeGc = Category.CreateEntry("SafeGc", true, "Pause GC during content generation",
                "Pauses the IL2CPP GC while Cotton mods create slimes and largos (one frame in the main menu). Prevents rare coreclr 0xc0000005 crashes at startup.");
            FixDuplicateSlimes = Category.CreateEntry("FixDuplicateSlimes", true, "Fix duplicate slime definitions",
                "Drops duplicate SlimeDefinitions.Slimes entries before RefreshIndexes (thousands of 'same key' errors otherwise).");
            FixLargoAI = Category.CreateEntry("FixLargoAI", true, "Fix largo AI",
                "Repairs SlimeModules and the SubbehaviourPlexer behaviour list of every Cotton largo (largos stand still and spam NullReferenceException otherwise).");
            LocalizationInterval = Category.CreateEntry("LocalizationInterval", 2f, "Localization refresh interval (s)",
                "How often modded strings are re-applied (LocalizationRepair). A locale change is applied immediately.");
            CallAutoLargosLoaded = Category.CreateEntry("CallAutoLargosLoaded", true, "Call AutoLargosLoaded",
                "Calls CottonMod.AutoLargosLoaded on every Cotton mod right after largo generation (never called in Cotton 0.3.1).");
            DevConsole = Category.CreateEntry("DevConsole", false, "Dev console",
                "Reads commands from UserData\\CottonLibrary\\cmd.txt every 0.5 s (help, slimes, spawn, time, census, ...).");
            Verbose = Category.CreateEntry("Verbose", false, "Verbose log", "Extra diagnostics in the MelonLoader log.");
            DisabledModules = Category.CreateEntry("DisabledModules", "", "Disabled engine modules",
                "Comma separated ids of Forge engine modules that must not run: runtime, weather, market, vacpack, hud, matanim (see the 'modules' dev command).");
            FireModeKey = Category.CreateEntry("FireModeKey", "B", "Fire mode key",
                "Keyboard key (UnityEngine.InputSystem.Key name) that cycles vacpack fire modes registered by mods. Empty = no key.");
            SmartLargos = Category.CreateEntry("SmartLargos", true, "Smart largo looks",
                "Generated largos use the fork's engine: the vanilla Pink+X largo of a parent as template, the more distinctive body and face, perceptual colour mix, parts of both parents without duplicates, parents never recoloured. Off = Cotton's original merge.");
            LargoRadiants = Category.CreateEntry("LargoRadiants", true, "Radiant largo variants",
                "With SmartLargos: real radiant variants for generated largos (radiant parent's palette and aura) instead of the template's stale PinkRock ones.");
            LargoParity = Category.CreateEntry("LargoParity", true, "Largo parity with vanilla",
                "With SmartLargos: generated largos get what vanilla largos have - prefab references to themselves (not the template), parent components with remapped references, the groups of every vanilla largo, no template pedia art, zones and diet like vanilla. Learned from the game data at runtime.");
            LargoIcons = Category.CreateEntry("LargoIcons", true, "Largo icons",
                "With SmartLargos: an own icon for every generated largo (the template's or a parent's icon repainted to the largo colours, plus the other parent in a corner) instead of the template's icon.");
        }
    }
}
