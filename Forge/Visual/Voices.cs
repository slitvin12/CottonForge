using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Audio;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Own voice lines for a slime (fun/awe, fear/alarm, damage, jump/high jump). The definition gets a private copy of its
    /// <c>SlimeSounds</c>; each replaced line is a copy of the stock clip definition whose cue keeps the stock volume, pitch,
    /// distance and mixer, only the clips change. Lines left null keep the template's voice.
    /// </summary>
    public static class Voices
    {
        /// <summary>Clips for one slime; every array may hold several variants (one is picked per play).</summary>
        public sealed class Set
        {
            /// <summary>Happy / awe voice.</summary>
            public AudioClip[] Fun;
            /// <summary>Fear / alarm voice.</summary>
            public AudioClip[] Fear;
            /// <summary>Hurt voice.</summary>
            public AudioClip[] Damage;
            /// <summary>Jump / high jump voice.</summary>
            public AudioClip[] Jump;
        }

        /// <summary>Copy of a stock cue with other clips (volume, pitch, distance, mixer stay). Cached by <paramref name="key"/>.</summary>
        public static SECTR_AudioCue CueLike(SECTR_AudioCue template, string key, params AudioClip[] clips)
        {
            if (template == null || clips == null) return null;
            if (_cues.TryGetValue(key, out var have) && have != null) return have;
            var list = new Il2CppSystem.Collections.Generic.List<SECTR_AudioCue.ClipData>();
            foreach (var clip in clips) if (clip != null) list.Add(new SECTR_AudioCue.ClipData(clip));
            if (list.Count == 0) return null;
            var c = Object.Instantiate(template);
            c.name = "Forge_" + key;
            c.hideFlags = HideFlags.DontUnloadUnusedAsset;
            try { c.Template = null; } catch { }
            c.AudioClips = list;
            _cues[key] = c;
            return c;
        }

        static readonly Dictionary<string, SECTR_AudioCue> _cues = new();

        static AudioClipDefinition Line(AudioClipDefinition src, string key, AudioClip[] clips)
        {
            if (src == null || clips == null || clips.Length == 0) return src;
            var cue = CueLike(src._audioCue, key, clips);
            if (cue == null) return src;
            var d = Object.Instantiate(src);
            d.name = "Forge_" + key;
            d.hideFlags = HideFlags.DontUnloadUnusedAsset;
            d._audioCue = cue;
            return d;
        }

        /// <summary>Gives <paramref name="def"/> its own voice lines (see <see cref="Set"/>). Call after the definition and prefab exist.</summary>
        public static bool Apply(SlimeDefinition def, Set set)
        {
            if (def == null || set == null) return false;
            try
            {
                var src = def.Sounds;
                if (src == null) { ForgeLog.Warn($"Voices {def.name}: the definition has no SlimeSounds"); return false; }
                var snd = Object.Instantiate(src);
                snd.name = def.name + "SlimeSounds";
                snd.hideFlags = HideFlags.DontUnloadUnusedAsset;
                string k = def.name;
                if (set.Fun != null) { snd._voiceFunClipDefinition = Line(src._voiceFunClipDefinition, k + "Fun", set.Fun); snd._voiceAweClipDefinition = Line(src._voiceAweClipDefinition, k + "Awe", set.Fun); }
                if (set.Fear != null) { snd._voiceFearClipDefinition = Line(src._voiceFearClipDefinition, k + "Fear", set.Fear); snd._voiceAlarmClipDefinition = Line(src._voiceAlarmClipDefinition, k + "Alarm", set.Fear); }
                if (set.Damage != null) snd._voiceDamageClipDefinition = Line(src._voiceDamageClipDefinition, k + "Damage", set.Damage);
                if (set.Jump != null) { snd._voiceJumpClipDefinition = Line(src._voiceJumpClipDefinition, k + "Jump", set.Jump); snd._voiceHighJumpClipDefinition = Line(src._voiceHighJumpClipDefinition, k + "HighJump", set.Jump); }
                def.Sounds = snd;
                var sa = def.prefab != null ? def.prefab.GetComponent<SlimeAudio>() : null;
                if (sa != null) sa.SlimeSounds = snd;
                return true;
            }
            catch (Exception e) { ForgeLog.Warn($"Voices {def.name}: {e.Message}"); return false; }
        }

        /// <summary>
        /// Same with WAV files embedded in a mod (names without ".wav"; several variants separated by '|', e.g.
        /// <c>"happy_01|happy_02"</c>). Null keeps the template's line.
        /// </summary>
        public static bool Apply(SlimeDefinition def, ModAssets assets, string fun = null, string fear = null, string damage = null, string jump = null)
        {
            if (assets == null) return false;
            AudioClip[] L(string names)
            {
                if (string.IsNullOrEmpty(names)) return null;
                var r = new List<AudioClip>();
                foreach (var n in names.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var c = assets.Clip(n);
                    if (c != null) r.Add(c);
                }
                return r.Count > 0 ? r.ToArray() : null;
            }
            return Apply(def, new Set { Fun = L(fun), Fear = L(fear), Damage = L(damage), Jump = L(jump) });
        }
    }
}
