using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Il2Cpp;
using MelonLoader.Utils;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// File-driven dev console (config <c>DevConsole = true</c>): write commands, one per line, into
    /// <c>UserData\CottonLibrary\cmd.txt</c>; every 0.5 s they run on the main thread, the file is deleted and the output goes to
    /// the MelonLoader log and <c>UserData\CottonLibrary\out.txt</c>. Works without focusing the game (scripts, AI agents).
    /// Mods add their own commands with <see cref="Register"/>.
    /// </summary>
    public static class DevConsole
    {
        sealed class Cmd { public string Help; public Action<string[]> Run; }
        static readonly SortedDictionary<string, Cmd> _cmds = new(StringComparer.OrdinalIgnoreCase);
        static float _next;
        static StringBuilder _out;

        /// <summary><c>UserData\CottonLibrary</c> (created on demand).</summary>
        public static string Dir
        {
            get { var d = Path.Combine(MelonEnvironment.UserDataDirectory, "CottonLibrary"); Directory.CreateDirectory(d); return d; }
        }

        /// <summary>
        /// Adds a command. <paramref name="run"/> gets the words of the line (<c>args[0]</c> = command name). Use
        /// <see cref="Print"/> for output and <see cref="F"/> / <see cref="Arg"/> to read arguments.
        /// </summary>
        public static void Register(string name, string help, Action<string[]> run)
        {
            if (string.IsNullOrWhiteSpace(name) || run == null) return;
            _cmds[name.Trim()] = new Cmd { Help = help ?? "", Run = run };
        }

        /// <summary>Command output (log + out.txt).</summary>
        public static void Print(string line)
        {
            ForgeLog.Msg("[cmd] " + line);
            _out?.AppendLine(line);
        }

        /// <summary>Float argument <paramref name="i"/> (invariant culture) or <paramref name="def"/>.</summary>
        public static float F(string[] a, int i, float def) =>
            a.Length > i && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>String argument <paramref name="i"/> or <paramref name="def"/>.</summary>
        public static string Arg(string[] a, int i, string def = null) => a.Length > i ? a[i] : def;

        /// <summary>Runs one command line now (also usable from your own UI / hotkeys).</summary>
        public static void Execute(string line)
        {
            var a = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return;
            EnsureBuiltins();
            if (!_cmds.TryGetValue(a[0], out var c)) { Print($"unknown command '{a[0]}' (help)"); return; }
            try { c.Run(a); }
            catch (Exception e) { Print($"{a[0]} failed: {e}"); }
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            string path;
            try { path = Path.Combine(Dir, "cmd.txt"); } catch { return; }
            if (!File.Exists(path)) return;
            string[] lines;
            try { lines = File.ReadAllLines(path); File.Delete(path); } catch { return; }
            _out = new StringBuilder();
            foreach (var raw in lines)
            {
                var l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                Print("> " + l);
                Execute(l);
            }
            try { File.WriteAllText(Path.Combine(Dir, "out.txt"), _out.ToString()); } catch { }
            _out = null;
        }

        static bool _builtins;

        static void EnsureBuiltins()
        {
            if (_builtins) return;
            _builtins = true;
            Register("help", "help [command | prefix] - all commands, or the help of one command (or of the commands starting with the prefix)", a =>
            {
                var q = a.Length > 1 ? a[1] : null;
                if (q != null && _cmds.TryGetValue(q, out var one)) { Print($"{q}: {one.Help}"); return; }
                int n = 0;
                foreach (var kv in _cmds)
                    if (q == null || kv.Key.StartsWith(q, StringComparison.OrdinalIgnoreCase)) { Print($"{kv.Key,-10} {kv.Value.Help}"); n++; }
                if (n == 0) Print($"no command '{q}' (help)");
            });
            Register("info", "versions, GC, fixes", _ => Info());
            Register("slimes", "Forge slimes and their state", _ => Slimes());
            Register("spawn", "spawn <type> [count] [dist] - any identifiable type (slime, food, plort) in front of the camera", Spawn);
            Register("kill", "kill [radius] - despawn Forge slimes and their largos near the camera (default 20 m)", a => Kill(F(a, 1, 20f)));
            Register("time", "time <hour> - fast forward to the next such hour", a =>
            {
                var td = SceneContext.Instance.TimeDirector;
                td.FastForwardTo(td.GetNextHour(F(a, 1, 12f)));
            });
            Register("census", "Forge actors nearby, spawner members, natural spawns", _ => Census());
            Register("diet", "diet <slime> - groups, favourites, produce, eat map size", a => Diet(Arg(a, 1, "")));
            Register("largos", "largos <slime> - largos of a slime", a => LargosOf(Arg(a, 1, "")));
            Register("mats", "runtime materials made by Mat (count, destroyed, revived)", _ => Print($"materials {Mat.Made.Count()}, destroyed {Mat.Destroyed}, revived {Mat.Revived}; faces blocked {Faces.Blocked}, healed {Faces.Healed}"));
            Register("revive", "re-create destroyed runtime materials now", _ => { Mat.Tick(true); Print("revived total " + Mat.Revived); });
            Register("fx", "fx <name> [scale] [dist] - play a game FX prefab in front of the camera; 'fx list [filter]' lists names", a =>
            {
                if (Arg(a, 1) == "list") { var f = Arg(a, 2, ""); Print(string.Join(", ", Fx.Names.Where(n => n.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(n => n))); return; }
                var go = Fx.Play(Arg(a, 1, ""), World.InFrontOfCamera(F(a, 3, 4f), 0.5f), F(a, 2, 1f));
                Print(go != null ? "fx " + go.name : "fx not found: " + Arg(a, 1));
            });
            Register("loc", "language code and modded string count; 'loc reload' re-applies all strings", a =>
            {
                if (Arg(a, 1) == "reload") CottonLibrary.Patches.LocalizationRepair.CheckNow();
                Print($"language {CottonLibrary.Patches.LocalizationRepair.Code ?? "?"}, strings re-applied {CottonLibrary.Patches.LocalizationRepair.Applied}");
            });
            Register("dmgsources", "names of damage sources (for Dmg.Player)", _ => Print(string.Join(", ", Dmg.SourceNames())));
            Register("report", "write UserData\\CottonLibrary\\report.txt", _ => Report());
            DevTools.RegisterAll();
            EngineCommands.RegisterAll();
            LargoBlend.RegisterCommands();
            LargoAudit.RegisterCommands();
        }

        static void Info()
        {
            Print($"CottonForge {ForgeCore.Version} (fork of Cotton Library {ForgeCore.BasedOn}; build {ForgeCore.Build}), patches {ForgeCore.PatchesApplied} on / {ForgeCore.PatchesOff} off / {ForgeCore.PatchesFailed} failed, Unity {Application.unityVersion}");
            Print($"GC paused {Gc.Paused}, incremental {Gc.Incremental()}");
            Print($"fixes: duplicate slimes removed {SlimeIndexDedup.Removed}, group duplicates skipped {ExtentionLibrary.GroupDuplicatesSkipped}, largo AI lists {Largos.FixedPlexers}, largo modules {Largos.FixedModules}");
            Print($"behaviours mapped for {Behaviours.Mapped} definitions; spawners modified {Spawns.Modified.Count}, re-added {Spawns.ReAdded}");
        }

        static void Slimes()
        {
            if (SlimeRegistry.All.Count == 0) { Print("no Forge slimes"); return; }
            foreach (var b in SlimeRegistry.All)
                Print($"{b.Name,-14} key={b.KeyName} mod={b.ModAssembly.GetName().Name} plort={(b.PlortType != null ? b.PlortType.name : "-")} largos={b.Largos.Count()} share={b.SpawnShare:0.###} zones={b.Zones}");
        }

        static IdentifiableType Type(string name) => SlimeRegistry.Find(name)?.Def ?? Idents.Find(name) ?? Idents.FindByHints(new[] { name }, IdentKind.Any) ?? Idents.FindByHints(new[] { name }, IdentKind.Slime);

        static void Spawn(string[] a)
        {
            var t = Type(Arg(a, 1, ""));
            if (t == null) { Print("type not found: " + Arg(a, 1)); return; }
            int n = Mathf.Clamp((int)F(a, 2, 1f), 1, 50);
            var at = World.InFrontOfCamera(F(a, 3, 4f));
            int ok = 0;
            for (int i = 0; i < n; i++)
                if (World.Spawn(t, at + new Vector3(World.Rand(-1f, 1f), i * 0.4f, World.Rand(-1f, 1f))) != null) ok++;
            Print($"spawned {ok}x {t.name}");
        }

        static bool IsForgeActor(IdentifiableType t)
        {
            var d = t.As<SlimeDefinition>();
            if (d == null) return false;
            if (SlimeRegistry.IsForge(d)) return true;
            return d.IsLargo && d.BaseSlimes != null && d.BaseSlimes.Any(SlimeRegistry.IsForge);
        }

        static void Kill(float radius)
        {
            var cam = Camera.main;
            if (cam == null) return;
            int n = 0;
            foreach (var id in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
            {
                if (id == null || id.identType == null || !IsForgeActor(id.identType)) continue;
                if ((id.transform.position - cam.transform.position).sqrMagnitude > radius * radius) continue;
                World.Despawn(id.gameObject, "Cotton.DevConsole"); n++;
            }
            Print($"despawned {n}");
        }

        static void Census()
        {
            var pl = World.PlayerPos;
            var byType = new SortedDictionary<string, (int n, float min)>();
            foreach (var ia in UnityEngine.Object.FindObjectsOfType<IdentifiableActor>())
            {
                var t = ia.identType;
                if (t == null || !IsForgeActor(t)) continue;
                float d = Vector3.Distance(pl, ia.transform.position);
                byType.TryGetValue(t.name, out var v);
                byType[t.name] = (v.n + 1, v.n == 0 ? d : Mathf.Min(v.min, d));
            }
            Print($"census at {pl}: {byType.Count} Forge types loaded");
            foreach (var kv in byType) Print($"  actor {kv.Key} x{kv.Value.n} nearest {kv.Value.min:0} m");
            foreach (var kv in Spawns.AddedPerScene) Print($"  spawner members added in {kv.Key}: {kv.Value}");
            Print($"  spawners modified {Spawns.Modified.Count}, re-added after reset {Spawns.ReAdded}");
            foreach (var kv in Spawns.Natural) Print($"  natural spawns {kv.Key} x{kv.Value}");
            foreach (var kv in Foods.WildAdded) Print($"  wild food {kv.Key} x{kv.Value}");
        }

        static string Names(IEnumerable<IdentifiableType> l) => l == null ? "-" : string.Join(", ", l.Where(x => x != null).Select(x => x.name));

        static void Diet(string name)
        {
            var d = Type(name).As<SlimeDefinition>();
            if (d == null || d.Diet == null) { Print("slime not found: " + name); return; }
            var diet = d.Diet;
            Print($"{d.name}: groups [{string.Join(", ", (diet.MajorFoodIdentifiableTypeGroups?.ToManagedList() ?? new List<IdentifiableTypeGroup>()).Where(g => g != null).Select(g => g.name))}]");
            Print($"  favourites [{Names(diet.FavoriteIdents)}], extra [{Names(diet.AdditionalFoodIdents)}], produce [{Names(diet.ProduceIdents)}]");
            Print($"  eat map entries {diet.EatMap?.Count ?? 0}, favourite toys [{Names(d.FavoriteToyIdents?.ToManagedList().Cast<IdentifiableType>())}]");
        }

        static void LargosOf(string name)
        {
            var d = Type(name).As<SlimeDefinition>();
            if (d == null) { Print("slime not found: " + name); return; }
            var l = Largos.Of(d).ToList();
            Print($"{d.name}: {l.Count} largos");
            foreach (var x in l) Print($"  {x.name} '{Idents.DisplayName(x)}'");
        }

        static void Report()
        {
            var sb = new StringBuilder();
            var old = _out;
            _out = sb;
            try { Info(); Slimes(); } finally { _out = old; }
            var file = Path.Combine(Dir, "report.txt");
            File.WriteAllText(file, sb.ToString());
            Print("written " + file);
        }
    }
}
