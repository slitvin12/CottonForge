using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Slime.SquashWobble;
using UnityEngine;
using static CottonLibrary.Forge.DevConsole;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Look-development and testing commands of the dev console: teleport, posing and freezing slimes, live material tuning,
    /// texture import/export, dumps. Registered with the built-in commands; <c>help</c> lists them.
    /// </summary>
    static class DevTools
    {
        internal static void RegisterAll()
        {
            Register("locs", "locs [filter] - regions and named locations for goto", a =>
            {
                var f = Arg(a, 1, "");
                foreach (var l in World.Locations()) if (l.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) Print(l);
            });
            Register("goto", "goto <location|region> [x y z] - teleport (also to another region)", a =>
            {
                Vector3? p = a.Length >= 5 ? new Vector3(F(a, 2, 0f), F(a, 3, 0f), F(a, 4, 0f)) : null;
                Print(World.Teleport(Arg(a, 1, ""), p) is string s ? "goto " + s : "goto: not found (see locs)");
            });
            Register("hp", "player health and radiation", _ => { var s = World.PlayerStats(); Print($"hp {s.hp}/{s.max} rad {s.rad:0.#}"); });
            Register("rad", "rad <amount> - add (or with a minus remove) player radiation", a => World.Rads(F(a, 1, 50f)));
            Register("radhud", "radhud [x y] [recovery/s] - show SR1's rad meter above the health bar", a =>
            {
                RadHud.Enable(F(a, 3, RadHud.RecoveryPerSecond), new Vector2(F(a, 1, RadHud.Offset.x), F(a, 2, RadHud.Offset.y)));
                RadHud.Place();
            });
            Register("pose", "pose <type> [dist] [seconds] [dy] [yaw] - nearest actor in front of the camera, frozen (screenshots)", Pose);
            Register("lineup", "lineup <type,type,...> [dist] [seconds] - nearest actor of each type in a row, frozen", Lineup);
            Register("tree", "tree <type> [depth] - transform tree of the nearest actor", a =>
            {
                var go = Need(a); if (go == null) return;
                foreach (var l in Diag.Hierarchy(go.transform, false, (int)F(a, 2, 3f)).Split('\n')) if (l.Length > 0) Print(l.TrimEnd());
            });
            Register("hier", "hier <type> - full hierarchy with component details of the nearest actor -> hier_<type>.txt", a =>
            {
                var go = Need(a); if (go == null) return;
                Print("written " + Diag.Write("hier_" + a[1] + ".txt", Diag.Hierarchy(go.transform, true, 12)));
            });
            Register("dump", "dump <slime> - definition, diet, appearance structures, meshes, materials -> dump_<slime>.txt", a =>
            {
                var d = Idents.Find(Arg(a, 1, ""))?.TryCast<SlimeDefinition>() ?? SlimeRegistry.Find(Arg(a, 1, ""))?.Def;
                if (d == null) { Print("slime not found: " + Arg(a, 1)); return; }
                Print("written " + Diag.Write("dump_" + d.name + ".txt", Diag.Slime(d)));
            });
            Register("palette", "palette <slime> - appearance palettes", a =>
            {
                var d = Idents.Find(Arg(a, 1, ""))?.TryCast<SlimeDefinition>() ?? SlimeRegistry.Find(Arg(a, 1, ""))?.Def;
                if (d?.AppearancesDefault == null) { Print("slime not found: " + Arg(a, 1)); return; }
                string c(Color x) => $"({x.r:0.00},{x.g:0.00},{x.b:0.00})";
                foreach (var app in d.AppearancesDefault)
                {
                    if (app == null) continue;
                    var p = app._colorPalette;
                    Print($"{d.name}/{app.name}: top {c(p.Top)} mid {c(p.Middle)} bot {c(p.Bottom)} ammo {c(p.Ammo)} splat {c(app._splatColor)}");
                }
            });
            Register("track", "track <type> [seconds] - log position / speed / ground contact of the nearest actor", a =>
            {
                var go = Need(a); if (go == null) return;
                var t = go.transform; var rb = go.GetComponent<Rigidbody>();
                float until = Time.unscaledTime + F(a, 2, 4f);
                Scheduler.Every(0.25f, () =>
                {
                    if (t == null) return false;
                    var v = rb != null ? rb.linearVelocity : Vector3.zero;
                    Print($"track {t.name} {Time.time:0.00} pos {t.position} hv {new Vector2(v.x, v.z).magnitude:0.0} vy {v.y:0.0} ground {World.Grounded(t, t.lossyScale.x)}");
                    return Time.unscaledTime < until;
                }, "track");
            });
            Register("wobble", "wobble <type> [x y z] - kick SR2's squash & stretch of the nearest actor", a =>
            {
                var go = Need(a); if (go == null) return;
                var rb = go.GetComponentInChildren<RubberBoneEffectShader>();
                if (rb == null) { Print("no RubberBoneEffectShader on " + a[1]); return; }
                rb.AddImpulseForce(new Vector3(F(a, 2, 0f), F(a, 3, 3f), F(a, 4, 0f)));
            });
            Register("rinfo", "rinfo <type> - renderers and materials of the nearest actor", a =>
            {
                var go = Need(a); if (go == null) return;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        Print($"{r.gameObject.name} [{r.GetIl2CppType().Name} en={r.enabled} vis={r.isVisible}] #{i} {Mat.Describe(mats[i])}");
                }
            });
            Register("layer", "layer <type> <material|off> [inflate] [facets] - extra skinned layer (shell/coat) on the loaded actors of a type", a =>
            {
                var matName = Arg(a, 2, "off");
                var m = matName == "off" ? null : Mat.Find(matName);
                if (matName != "off" && m == null) { Print("no material " + matName); return; }
                int n = 0;
                foreach (var go in World.AllOf(Arg(a, 1, "")))
                    if (MeshKit.AddLayer(go, "dev", m, F(a, 3, 0.02f), (int)F(a, 4, 0f)) != null || m == null) n++;
                Print($"layer {matName} on {n} actors");
            });
            Register("vac", "vac [on] - hide the vacpack (and anything within 2 m of the camera) for screenshots; 'vac on' shows it again", Vac);

            // ---- live material tuning: <material> is a name ('*' = prefix), see Mat.Live
            Register("props", "props <material> - every shader property and value -> props_<material>.txt", a =>
            {
                var m = Mat.Live(Arg(a, 1, "")).FirstOrDefault() ?? Mat.Find(Arg(a, 1, ""));
                if (m == null) { Print("no material " + Arg(a, 1)); return; }
                Print(Mat.Describe(m));
                Print("written " + Diag.Write("props_" + a[1].TrimEnd('*') + ".txt", Mat.Dump(m)));
            });
            Register("flt", "flt <material> <prop> <value>", a => Tune(a, 4, m => m.SetFloat(a[2], F(a, 3, 0f))));
            Register("col", "col <material> <prop> r g b [a] - (HDR values allowed)", a => Tune(a, 6, m => m.SetColor(a[2], new Color(F(a, 3, 0f), F(a, 4, 0f), F(a, 5, 0f), F(a, 6, 1f)))));
            Register("vec", "vec <material> <prop> x y z w", a => Tune(a, 7, m => m.SetVector(a[2], new Vector4(F(a, 3, 0f), F(a, 4, 0f), F(a, 5, 0f), F(a, 6, 0f)))));
            Register("kw", "kw <material> <keyword> on|off", a => Tune(a, 4, m => m.Kw(a[2], a[3] == "on")));
            Register("rq", "rq <material> <render queue> (-1 = shader default)", a => Tune(a, 3, m => m.renderQueue = (int)F(a, 2, -1f)));
            Register("texst", "texst <material> <prop> scaleX scaleY offsetX offsetY", a => Tune(a, 7, m =>
            {
                m.SetTextureScale(a[2], new Vector2(F(a, 3, 1f), F(a, 4, 1f)));
                m.SetTextureOffset(a[2], new Vector2(F(a, 5, 0f), F(a, 6, 0f)));
            }));
            Register("tex", "tex <material> <prop> <game texture | file.png in UserData\\CottonLibrary> - swap a texture live", a =>
            {
                if (a.Length < 4) { Print("tex <material> <prop> <texture>"); return; }
                Texture t = a[3].EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? Gfx.FromPng(File.Exists(Path.Combine(Dir, a[3])) ? File.ReadAllBytes(Path.Combine(Dir, a[3])) : null, "Forge_file_" + a[3], a.Length > 4 && a[4] == "linear")
                    : Gfx.GameTex(a[3]);
                if (t == null) { Print("texture not found: " + a[3]); return; }
                Tune(a, 4, m => m.SetTexture(a[2], t));
                Print($"tex {t.name} {t.width}x{t.height}");
            });
            Register("texdump", "texdump <material|texture> [prop] - save a texture as PNG into UserData\\CottonLibrary", a =>
            {
                var prop = Arg(a, 2, "_MainTex");
                Texture tex = null;
                foreach (var m in Mat.Live(Arg(a, 1, ""))) if (m.HasProperty(prop)) { tex = m.GetTexture(prop); if (tex != null) break; }
                tex ??= Gfx.GameTex(Arg(a, 1, ""));
                if (tex == null) { Print($"texdump: nothing for {Arg(a, 1)} {prop}"); return; }
                var file = Path.Combine(Dir, "tex_" + tex.name + ".png");
                Print(Gfx.SavePng(tex, file) ? $"texdump {tex.name} {tex.width}x{tex.height} -> {file}" : "texdump failed");
            });
            Register("gallery", "gallery <filter | mat,mat,...> [max] - spheres with those materials in front of the camera; 'gallery clear'", Gallery);
            Register("cfg", "cfg [category] [entry] [value] - list / show / change MelonPreferences live; 'cfg reload' re-reads the file", Cfg);
            Register("walls", "walls [refresh] - corral wall colliders and slimes passing through them", a =>
            {
                CorralWalls.Refresh(Arg(a, 1, "") == "refresh");
                Print($"corral walls {CorralWalls.Walls.Count}, slimes passing {CorralWalls.Passing}");
            });
            Register("mods", "loaded mods and plugins (Compat), multiplayer", _ =>
            {
                foreach (var m in Compat.Mods()) Print(m);
                Print($"multiplayer (SR2MP) {Compat.Multiplayer}, SR2E {Compat.SR2E}");
            });
        }

        static readonly List<GameObject> _gallery = new();

        static void Gallery(string[] a)
        {
            foreach (var g in _gallery) if (g != null) UnityEngine.Object.Destroy(g);
            _gallery.Clear();
            var what = Arg(a, 1, "");
            if (what == "" || what == "clear") { Print("gallery cleared"); return; }
            var cam = Camera.main;
            if (cam == null) return;
            var mats = new List<Material>();
            if (what.Contains(','))
                foreach (var n in what.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var m = Mat.Live(n).FirstOrDefault() ?? Mat.Find(n);
                    if (m != null) mats.Add(m); else Print("gallery: no material " + n);
                }
            else
            {
                int max = (int)F(a, 2, 24f);
                var seen = new HashSet<string>();
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m != null && m.name.IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0 && seen.Add(m.name) && mats.Count < max) mats.Add(m);
            }
            var t = cam.transform;
            var fwd = t.forward; fwd.y = 0f; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            const int perRow = 6; const float step = 1.15f;
            for (int i = 0; i < mats.Count; i++)
            {
                int row = i / perRow, col = i % perRow, inRow = Math.Min(perRow, mats.Count - row * perRow);
                var pos = t.position + fwd * 4f + right * ((col - (inRow - 1) * 0.5f) * step) + Vector3.up * (0.3f - row * step);
                var go = Gfx.Sphere("ForgeGallery_" + mats[i].name, null, mats[i], pos, 0.9f);
                _gallery.Add(go);
                Print($"{i,2} {Mat.Describe(mats[i])}");
            }
            Print($"gallery {mats.Count} material(s), left to right, top to bottom");
        }

        static void Cfg(string[] a)
        {
            string cat = Arg(a, 1, ""), entry = Arg(a, 2, "");
            if (cat == "reload") { LiveConfig.ReloadFromDisk(); Print("cfg reloaded from MelonPreferences.cfg"); return; }
            if (cat == "") { Print(string.Join(", ", LiveConfig.Categories())); return; }
            if (entry == "") { foreach (var l in LiveConfig.Entries(cat)) Print(l); return; }
            if (a.Length < 4) { Print($"{cat}.{entry} = {LiveConfig.Get(cat, entry) ?? "<no such entry>"}"); return; }
            var err = LiveConfig.Set(cat, entry, string.Join(" ", a.Skip(3)));
            Print(err ?? $"{cat}.{entry} = {LiveConfig.Get(cat, entry)} (saved)");
        }

        static GameObject Need(string[] a)
        {
            var go = World.Nearest(Arg(a, 1, ""));
            if (go == null) Print($"no loaded actor of type '{Arg(a, 1)}'");
            return go;
        }

        static void Tune(string[] a, int minArgs, Action<Material> set)
        {
            if (a.Length < minArgs) { Print($"{a[0]}: not enough arguments (help)"); return; }
            var list = Mat.Live(a[1]);
            if (list.Count == 0) { Print("no material " + a[1]); return; }
            foreach (var m in list) set(m);
            Print($"{a[0]} applied to {list.Count} material(s)");
        }

        static void Pose(string[] a)
        {
            var cam = Camera.main; var go = Need(a);
            if (cam == null || go == null) return;
            var fwd = cam.transform.forward; var up = cam.transform.up;
            var pos = cam.transform.position + fwd * F(a, 2, 2.6f) + up * F(a, 4, -0.45f);
            World.Freeze(go, F(a, 3, 10f));
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(-fwd, up) * Quaternion.Euler(0f, F(a, 5, 0f), 0f);
            Print($"pose {a[1]} at {pos}");
        }

        static void Lineup(string[] a)
        {
            var cam = Camera.main;
            if (cam == null || a.Length < 2) return;
            var p = cam.transform.position;
            var fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var list = a[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => World.Nearest(n, p)).Where(g => g != null).ToList();
            float dist = F(a, 2, 4f), seconds = F(a, 3, 8f), spacing = 1.7f, start = -(list.Count - 1) * spacing * 0.5f;
            for (int i = 0; i < list.Count; i++)
            {
                var go = list[i];
                var pos = p + fwd * dist + right * (start + i * spacing);
                if (World.GroundBelow(pos + Vector3.up * 5f, out var hit)) pos = hit.point + Vector3.up * 0.55f;
                World.Freeze(go, seconds);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            }
            Print($"lineup {list.Count} actors");
        }

        static readonly List<Renderer> _hiddenVac = new();

        static void Vac(string[] a)
        {
            if (Arg(a, 1) == "on") { foreach (var r in _hiddenVac) if (r != null) r.enabled = true; Print($"vac shown ({_hiddenVac.Count})"); _hiddenVac.Clear(); return; }
            var cam = Camera.main; if (cam == null) return;
            foreach (var r in cam.transform.root.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                if ((r.transform.position - cam.transform.position).sqrMagnitude > 4f) continue;
                r.enabled = false; _hiddenVac.Add(r);
            }
            Print($"vac hidden renderers: {_hiddenVac.Count}");
        }
    }
}
