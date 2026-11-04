using System;
using System.Linq;
using Il2Cpp;
using UnityEngine;
using static CottonLibrary.Forge.DevConsole;

namespace CottonLibrary.Forge
{
    /// <summary>Dev console commands of the engine layer (modules, archetypes, anatomy, weather, market, drones, vacpack, HUD, presets, saves).</summary>
    static class EngineCommands
    {
        internal static void RegisterAll()
        {
            Register("modules", "engine modules (on/off/disabled, errors) and actor runtime load", _ =>
            {
                foreach (var l in ForgeModules.Describe()) Print(l);
                Print($"actor runtime: {ActorRuntime.Objects} objects, {ActorRuntime.Count} logics; anatomy applied {Anatomy.Applied}, declared {Anatomy.Declared}; gadget logics {GadgetKit.Started}; drones {HelperDrone.All.Count}");
            });
            Register("arch", "arch [list | <id> [intensity] | off [all | <radius>]] - archetypes; attach to / remove from the slime in front of the camera (off all / off <radius>: every slime / slimes near the player)", Arch);
            Register("anatomy", "anatomy <slime> - resolved anatomy (layers, parts, archetypes, voices; largos merged)", a =>
            {
                var d = Library.GetSlime(Arg(a, 1, ""));
                if (d == null) { Print("no slime " + Arg(a, 1)); return; }
                Print(Anatomy.Resolve(d)?.ToString() ?? "no anatomy");
                Print($"largo engine: processed {LargoEngine.Processed}, diets merged {LargoEngine.DietsMerged}, voices {LargoEngine.VoicesApplied}; mergers {string.Join(", ", LargoEngine.Mergers.Select(m => m.Id))}");
            });
            Register("weather", "active weather (states -> kinds), wind, exposed slimes", _ =>
            {
                if (!ForgeModules.IsRunning("weather")) Print("module weather is OFF (see 'modules'); the values below are stale");
                string exp = ForgeWeather.Exposed >= 0 ? ForgeWeather.Exposed.ToString() : "n/a (no weather or nothing reacts)";
                Print($"active {ForgeWeather.Active}; wind {ForgeWeather.WindIntensity:0.00} dir {ForgeWeather.WindDirection}; exposed slimes {exp}" +
                      (ForgeWeather.FailedDirectors > 0 ? $"; failed directors {ForgeWeather.FailedDirectors}" : ""));
                foreach (var s in ForgeWeather.States) Print("  state " + s);
                if (ForgeWeather.States.Count == 0) Print("  no running weather states (or no WeatherDirector in this region)");
            });
            Register("market", "market [plort] | market boom|crash <plort|all> <mult> <days> | market model <plort> <vanilla|volatile|glut|classic|off> | market refresh", Market);
            Register("drone", "drone sell|gather [radius] - helper drone beside the player (out of view); 'drone list', 'drone clear'", Drone);
            Register("layers", "actor / ground physics layers (learned from prefabs)", _ =>
            {
                Print("actor layers: " + World.ActorLayerNames());
                var g = new System.Collections.Generic.List<string>();
                for (int i = 0; i < 32; i++) if ((World.GroundMask & (1 << i)) == 0) g.Add(i.ToString());
                Print("not ground: " + string.Join(", ", g));
            });
            Register("firemode", "firemode [id | normal | builtins] - list / select vacpack fire modes", a =>
            {
                var id = Arg(a, 1);
                if (id == "builtins") { FireModes.RegisterBuiltins(); Print("built-in fire modes registered"); }
                else if (id != null) Print(FireModes.Select(id) ? "fire mode " + (FireModes.Current?.Label ?? "Normal") : "unknown mode " + id);
                var modes = new[] { "normal" }.Concat(FireModes.All.Select(m => m.Id));
                Print($"modes: {string.Join(", ", modes)}; current {FireModes.Current?.Id ?? "normal"}; key {ForgeConfig.FireModeKey.Value}");
            });
            Register("ammo", "ammo [stash|unstash|shoot <slot>] - vacpack slots and virtual ammo slots", a =>
            {
                var op = Arg(a, 1); var slot = Arg(a, 2, "");
                if (op == "stash") Print($"stashed {VirtualAmmo.Stash(slot)}");
                else if (op == "unstash") Print($"unstashed {VirtualAmmo.Unstash(slot)}");
                else if (op == "shoot") Print(VirtualAmmo.Shoot(slot) != null ? "shot" : "nothing to shoot");
                for (int i = 0; i < VacPack.SlotCount; i++) { var s = VacPack.Slot(i); Print($"  {(i == VacPack.SelectedIndex ? ">" : " ")}{i}: {s.id?.name ?? "-"} {s.count}/{s.max}"); }
                foreach (var v in VirtualAmmo.All) Print("  virtual " + v);
            });
            Register("hud", "hud [test | clear | off | on] - Forge HUD widgets", a =>
            {
                switch (Arg(a, 1))
                {
                    case "test":
                        ForgeHud.Add(new HudBar("test.hp", HudAnchor.MiddleLeft, "Health", () => { var s = World.PlayerStats(); return s.max > 0 ? s.hp / (float)s.max : 0f; }, new Color(0.9f, 0.3f, 0.35f, 0.9f)));
                        ForgeHud.Add(new HudCounter("test.time", HudAnchor.TopRight, () => $"Hour {World.Hour:0.0}  {ForgeWeather.Active}"));
                        ForgeHud.Toast("Forge HUD test");
                        break;
                    case "clear": foreach (var w in ForgeHud.Widgets.ToList()) if (w.Id.StartsWith("test.")) ForgeHud.Remove(w.Id); break;
                    case "off": ForgeHud.Enabled = false; break;
                    case "on": ForgeHud.Enabled = true; break;
                }
                Print($"hud {(ForgeHud.Enabled ? "on" : "off")}, game HUD visible {ForgeHud.GameHudVisible}, widgets: {string.Join(", ", ForgeHud.Widgets.Select(w => w.Id))}");
                Print("reserved by the game HUD (px @1080p): " + ForgeHud.DescribeReserved());
            });
            Register("preset", "preset <jelly|glass|prismatic|neon|metal|glitch|stone> [r g b] - sphere with a material preset in front of the camera; 'preset clear'", Preset);
            Register("save", "save [flush] - Forge per-save data (ForgeSave)", a =>
            {
                if (Arg(a, 1) == "flush") ForgeSave.Flush();
                var keys = ForgeSave.Keys();
                Print($"game '{ForgeSave.Game ?? "-"}', {keys.Count} keys");
                foreach (var k in keys.Take(40)) Print($"  {k} = {ForgeSave.Get(k)}");
            });
        }

        static GameObject SlimeInFront()
        {
            var at = World.InFrontOfCamera(4f, 0f);
            GameObject best = null; float bd = float.MaxValue;
            foreach (var a in World.ActorsNear(at, 6f, DietTools.IsSlime))
            {
                float d = (a.transform.position - at).sqrMagnitude;
                if (d < bd) { bd = d; best = a.gameObject; }
            }
            return best;
        }

        static void Arch(string[] a)
        {
            var id = Arg(a, 1, "list");
            if (id == "list") { foreach (var x in Archetypes.All) Print("  " + x); return; }
            if (id == "off")
            {
                // arch off           - slime in front of the camera
                // arch off all       - every slime with an archetype
                // arch off <radius>  - slimes within <radius> m of the player
                var scope = Arg(a, 2);
                var targets = new System.Collections.Generic.List<GameObject>();
                if (scope == "all") targets.AddRange(ActorRuntime.Hosts());
                else if (scope != null && float.TryParse(scope, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var radius))
                {
                    var p = World.PlayerPos;
                    foreach (var h in ActorRuntime.Hosts()) if ((h.transform.position - p).sqrMagnitude <= radius * radius) targets.Add(h);
                }
                else { var front = SlimeInFront(); if (front != null) targets.Add(front); }
                if (targets.Count == 0) { Print(scope == null ? "no slime in front of the camera (arch off all | arch off <radius>)" : "no slimes with archetypes " + (scope == "all" ? "" : "within " + scope + " m")); return; }
                int n = 0, slimes = 0;
                foreach (var t in targets)
                {
                    int before = n;
                    foreach (var l in ActorRuntime.On(t).ToList()) if (l is IArchetypeRuntime && ActorRuntime.Remove(t, l)) n++;
                    if (n > before) slimes++;
                }
                Print(targets.Count == 1 ? $"removed {n} archetype runtimes from {targets[0].name}" : $"removed {n} archetype runtimes from {slimes} slimes");
                return;
            }
            var go = SlimeInFront();
            if (go == null) { Print("no slime in front of the camera"); return; }
            var arch = Archetypes.Get(id);
            if (arch == null) { Print("unknown archetype " + id + " (arch list)"); return; }
            var rt = arch.Create(new SlimeActor(go), F(a, 2, 1f));
            if (rt == null) { Print("archetype returned no runtime"); return; }
            ActorRuntime.Add(go, rt);
            Print($"{arch.Id} x{rt.Intensity:0.##} attached to {go.name}");
        }

        static void Market(string[] a)
        {
            var op = Arg(a, 1);
            if (op == "boom" || op == "crash")
            {
                var who = Arg(a, 2, "all");
                var plort = who == "all" ? null : Idents.Find(who);
                if (who != "all" && plort == null) { Print("no plort " + who); return; }
                float mult = F(a, 3, 2f); int days = (int)F(a, 4, 1f);
                var e = op == "boom" ? PlortMarket.Boom(plort, mult, days) : PlortMarket.Crash(plort, mult, days);
                Print("event " + e);
                return;
            }
            if (op == "model")
            {
                var plort = Idents.Find(Arg(a, 2, ""));
                if (plort == null) { Print("no plort " + Arg(a, 2)); return; }
                IPriceModel m = Arg(a, 3, "off") switch
                {
                    "vanilla" => PriceModels.Vanilla(),
                    "volatile" => PriceModels.Volatile(0.3f, 3f),
                    "glut" => PriceModels.Glut(30f, 0.35f),
                    "classic" => PriceModels.Classic(),
                    _ => null
                };
                PlortMarket.SetModel(plort, m);
                PlortMarket.Refresh();
                Print($"{plort.name}: model {(m != null ? m.ToString() : "default")} (applies on the next market update)");
                return;
            }
            if (op == "refresh") { PlortMarket.Refresh(); Print("market update requested"); return; }
            var entries = PlortMarket.Entries();
            if (entries.Count == 0) { Print("no market (load a save)"); return; }
            foreach (var e in entries)
                if (op == null || (e.Plort != null && e.Plort.name.IndexOf(op, StringComparison.OrdinalIgnoreCase) >= 0)) Print("  " + e);
            foreach (var e in PlortMarket.Events) Print("  event " + e);
        }

        static void Drone(string[] a)
        {
            var op = Arg(a, 1, "list");
            float r = F(a, 2, 20f);
            switch (op)
            {
                case "sell": Beside(HelperDrone.Spawn(DronePrograms.SellPlorts(r), null, null, BesidePlayer())); break;
                case "gather": Beside(HelperDrone.Spawn(DronePrograms.Gather(t => DietTools.IsPlort(t) || (t != null && !DietTools.IsSlime(t)), r), null, null, BesidePlayer())); break;
                case "clear": foreach (var d in HelperDrone.All.ToList()) d.Dismiss(); break;
            }
            foreach (var d in HelperDrone.All) Print("  " + d);
        }

        /// <summary>Ground ~3 m to the right and 2 m ahead of the camera, so a console drone doesn't hover in the player's view.</summary>
        static Vector3 BesidePlayer()
        {
            var p = World.PlayerPos;
            var cam = Camera.main;
            if (cam == null) return p;
            var right = cam.transform.right; right.y = 0f;
            var fwd = cam.transform.forward; fwd.y = 0f;
            if (right.sqrMagnitude < 1e-4f || fwd.sqrMagnitude < 1e-4f) return p;
            var probe = p + right.normalized * 3f + fwd.normalized * 2f;
            return World.GroundBelow(probe + Vector3.up * 3f, out var hit, 10f) ? hit.point : probe;
        }

        static void Beside(HelperDrone d) { if (d != null) d.HoverHeight = 3.7f; }

        static GameObject _preset;

        static void Preset(string[] a)
        {
            if (_preset != null) UnityEngine.Object.Destroy(_preset);
            var id = Arg(a, 1, "");
            if (id == "" || id == "clear") { Print("presets: " + string.Join(", ", MaterialPresets.Ids)); return; }
            var c = new Color(F(a, 2, 0.9f), F(a, 3, 0.4f), F(a, 4, 0.7f));
            var m = MaterialPresets.Build(id, "ForgePreview_" + id + "_" + c.GetHashCode(), new PresetColors(c, c, c, c), true);
            if (m == null) { Print("preset failed: " + id); return; }
            _preset = Gfx.Sphere("ForgePresetPreview", null, m, World.InFrontOfCamera(3f, 0.8f), 1.2f);
            Print($"preset {id}: {Mat.Describe(m)}");
        }
    }
}
