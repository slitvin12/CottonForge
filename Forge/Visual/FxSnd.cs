using System;
using System.Collections.Generic;
using CottonLibrary;
using Il2Cpp;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CottonLibrary.Forge
{
    /// <summary>SR2 particle effect prefabs by name (asset prefabs only, not live scene objects).</summary>
    public static class Fx
    {
        static Dictionary<string, GameObject> _fx;
        static readonly Dictionary<string, GameObject> _kept = new();
        static GameObject _keepRoot;

        /// <summary>FX prefab by GameObject name (root or child with a ParticleSystem), or null.</summary>
        public static GameObject Get(string name)
        {
            if (_fx == null || !_fx.TryGetValue(name, out var g) || g == null) Index();
            return _fx.TryGetValue(name, out var r) && r != null ? r : null;
        }

        /// <summary>All indexed FX names (for dumps).</summary>
        public static IEnumerable<string> Names { get { if (_fx == null) Index(); return _fx.Keys; } }

        static void Index()
        {
            _fx = new Dictionary<string, GameObject>();
            foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>())
            {
                if (ps == null) continue;
                var go = ps.gameObject;
                if (go.scene.IsValid()) continue;
                var root = go.transform.root.gameObject;
                if (!_fx.ContainsKey(root.name)) _fx[root.name] = root;
                if (!_fx.ContainsKey(go.name)) _fx[go.name] = go;
            }
        }

        /// <summary>Private inactive copy of an FX prefab that survives the unloading of the bundle it came from.</summary>
        public static GameObject Kept(string name)
        {
            if (_kept.TryGetValue(name, out var k) && k != null) return k;
            var src = Get(name);
            if (src == null) return null;
            if (_keepRoot == null)
            {
                _keepRoot = new GameObject("ForgeFxKeep");
                _keepRoot.SetActive(false);
                Object.DontDestroyOnLoad(_keepRoot);
                _keepRoot.hideFlags = HideFlags.HideAndDontSave;
            }
            k = Object.Instantiate(src, _keepRoot.transform);
            k.name = name;
            _kept[name] = k;
            return k;
        }

        /// <summary>Spawns and plays an FX at a position through SR2's FX pool.</summary>
        public static GameObject Play(string name, Vector3 pos, float scale = 1f)
        {
            var p = Get(name);
            if (p == null) { ForgeLog.WarnOnce("fx:" + name, "[fx] not found: " + name); return null; }
            try
            {
                var go = FXHelpers.SpawnAndPlayFX(p, pos, Quaternion.identity);
                if (go != null && Math.Abs(scale - 1f) > 0.01f) go.transform.localScale = Vector3.one * scale;
                return go;
            }
            catch (Exception e) { ForgeLog.WarnOnce("fxp:" + name, $"[fx] {name}: {e.Message}"); return null; }
        }

        /// <summary>Spawns an FX parented to an object.</summary>
        public static GameObject Attach(string name, GameObject parent, Vector3 localPos)
        {
            var p = Get(name);
            if (p == null || parent == null) return null;
            try
            {
                var go = FXHelpers.SpawnAndPlayFX(p, parent);
                if (go != null) go.transform.localPosition = localPos;
                return go;
            }
            catch (Exception e) { ForgeLog.WarnOnce("fxa:" + name, $"[fx] {name}: {e.Message}"); return null; }
        }

        /// <summary>Stops and recycles an FX instance.</summary>
        public static void Stop(GameObject fx)
        {
            if (fx == null) return;
            try { FXHelpers.RecycleAndStopFX(fx); } catch { Object.Destroy(fx); }
        }
    }

    /// <summary>
    /// Sounds through SR2's SECTR audio system (the game's volume sliders apply). Cues are built from a stock template cue
    /// (Pink slime voice) with your clips (<see cref="ModAssets.Clip"/>).
    /// </summary>
    public static class Snd
    {
        static readonly Dictionary<string, SECTR_AudioCue> _cues = new();
        static SECTR_AudioCue _tpl;

        static SECTR_AudioCue Template()
        {
            if (_tpl != null) return _tpl;
            try { _tpl = Library.GetSlime("Pink")?.Sounds?._voiceFunClipDefinition?._audioCue; } catch { }
            if (_tpl == null)
                foreach (var c in Resources.FindObjectsOfTypeAll<SECTR_AudioCue>())
                    if (c != null && c.AudioClips != null && c.AudioClips.Count > 0 && !c.Loops) { _tpl = c; break; }
            return _tpl;
        }

        /// <summary>Cached cue <paramref name="key"/> from clips (random clip per play, slight pitch variation for one-shots).</summary>
        public static SECTR_AudioCue Cue(string key, bool loop, float volume, float maxDistance, params AudioClip[] clips)
        {
            if (_cues.TryGetValue(key, out var c) && c != null) return c;
            var tpl = Template();
            if (tpl == null) return null;
            var list = new Il2CppSystem.Collections.Generic.List<SECTR_AudioCue.ClipData>();
            foreach (var clip in clips) if (clip != null) list.Add(new SECTR_AudioCue.ClipData(clip));
            if (list.Count == 0) return null;
            c = Object.Instantiate(tpl);
            c.name = "Forge_" + key;
            c.hideFlags = HideFlags.DontUnloadUnusedAsset;
            try { c.Template = null; } catch { }
            c.AudioClips = list;
            c.Loops = loop;
            c.Volume = new Vector2(volume, volume);
            c.Pitch = loop ? new Vector2(1f, 1f) : new Vector2(0.94f, 1.06f);
            if (maxDistance > 0f) c.MaxDistance = maxDistance;
            _cues[key] = c;
            return c;
        }

        /// <summary>Cue from embedded WAV files of a mod.</summary>
        public static SECTR_AudioCue Cue(ModAssets assets, string key, bool loop, float volume, float maxDistance, params string[] wavNames)
        {
            if (_cues.TryGetValue(key, out var c) && c != null) return c;
            var clips = new List<AudioClip>();
            foreach (var n in wavNames) { var clip = assets.Clip(n); if (clip != null) clips.Add(clip); }
            return Cue(key, loop, volume, maxDistance, clips.ToArray());
        }

        /// <summary>Stock cue by name (e.g. to reuse an SR2 sound).</summary>
        public static SECTR_AudioCue Stock(string name)
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<SECTR_AudioCue>()) if (c != null && c.name == name) return c;
            return null;
        }

        public static void Play(SECTR_AudioCue cue, Vector3 pos)
        {
            if (cue == null) return;
            try { SECTR_AudioSystem.Play(cue, pos, false); } catch (Exception e) { ForgeLog.WarnOnce("snd:" + cue.name, "[snd] " + e.Message); }
        }

        /// <summary>Looping positional source parented to a GameObject (start/stop with <see cref="SetPlaying"/>).</summary>
        public static SECTR_PointSource Loop(GameObject host, SECTR_AudioCue cue, string name = "ForgeLoop")
        {
            if (cue == null || host == null) return null;
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(host.transform, false);
            var src = go.AddComponent<SECTR_PointSource>();
            src.Cue = cue;
            src.Loop = true;
            go.SetActive(true);
            return src;
        }

        public static void SetPlaying(SECTR_PointSource src, bool on)
        {
            if (src == null) return;
            try
            {
                if (on && !src.IsPlaying) src.Play();
                else if (!on && src.IsPlaying) src.Stop(false);
            }
            catch { }
        }
    }
}
