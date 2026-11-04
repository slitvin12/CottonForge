using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Sound bank with loudness auto-balancing: clips are grouped under keys, measured (RMS) and scaled to a common
    /// target loudness so mod sounds sit at the same level as each other (and roughly SR2's). Works on clips whose samples
    /// are readable — every clip loaded with <see cref="ModAssets.Clip"/>; stock compressed clips are only measured when
    /// Unity allows it.
    /// <code>SoundBank.Add("zap", assets.Clip("zap_01"), assets.Clip("zap_02"));
    /// SoundBank.Play("zap", pos);</code>
    /// </summary>
    public static class SoundBank
    {
        /// <summary>Target RMS loudness (0.1 ≈ -20 dBFS, about SR2's slime voices).</summary>
        public static float TargetRms = 0.1f;
        /// <summary>Peak ceiling after normalisation (no clipping).</summary>
        public static float PeakLimit = 0.97f;
        /// <summary>Largest gain applied to a quiet clip.</summary>
        public static float MaxGain = 8f;

        sealed class Group { public AudioClip[] Clips; public float Volume; public float MaxDistance; public bool Loop; }
        static readonly Dictionary<string, Group> _groups = new(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<IntPtr> _normalized = new();

        /// <summary>RMS and peak of a clip (-1 when its samples can't be read).</summary>
        public static (float rms, float peak) Measure(AudioClip clip)
        {
            if (clip == null) return (-1f, -1f);
            try
            {
                int n = clip.samples * clip.channels;
                if (n <= 0) return (-1f, -1f);
                var buf = new Il2CppStructArray<float>(n);
                if (!clip.GetData(buf, 0)) return (-1f, -1f);
                double sum = 0; float peak = 0f;
                for (int i = 0; i < n; i++) { float v = buf[i]; sum += v * v; float a = Math.Abs(v); if (a > peak) peak = a; }
                return ((float)Math.Sqrt(sum / n), peak);
            }
            catch { return (-1f, -1f); }
        }

        /// <summary>
        /// Scales the samples of <paramref name="clip"/> to <paramref name="targetRms"/> (default <see cref="TargetRms"/>),
        /// never above <see cref="PeakLimit"/>. Returns the gain applied (1 = unchanged / unreadable). Each clip is
        /// normalised once.
        /// </summary>
        public static float Normalize(AudioClip clip, float targetRms = -1f)
        {
            if (clip == null || _normalized.Contains(clip.Pointer)) return 1f;
            if (targetRms <= 0f) targetRms = TargetRms;
            try
            {
                int n = clip.samples * clip.channels;
                var buf = new Il2CppStructArray<float>(n);
                if (n <= 0 || !clip.GetData(buf, 0)) return 1f;
                double sum = 0; float peak = 0f;
                for (int i = 0; i < n; i++) { float v = buf[i]; sum += v * v; float a = Math.Abs(v); if (a > peak) peak = a; }
                float rms = (float)Math.Sqrt(sum / n);
                if (rms < 1e-5f) return 1f;
                float gain = Mathf.Min(targetRms / rms, MaxGain);
                if (peak * gain > PeakLimit) gain = PeakLimit / peak;
                _normalized.Add(clip.Pointer);
                if (Mathf.Abs(gain - 1f) < 0.02f) return 1f;
                for (int i = 0; i < n; i++) buf[i] *= gain;
                clip.SetData(buf, 0);
                ForgeLog.Debug($"[sound] {clip.name}: rms {rms:0.000} peak {peak:0.00} gain x{gain:0.00}");
                return gain;
            }
            catch (Exception e) { ForgeLog.WarnOnce("normalize:" + clip.name, $"[sound] can't normalise {clip.name}: {e.Message}"); return 1f; }
        }

        /// <summary>Normalises every clip to the same loudness.</summary>
        public static void Balance(IEnumerable<AudioClip> clips, float targetRms = -1f)
        {
            if (clips == null) return;
            foreach (var c in clips) Normalize(c, targetRms);
        }

        /// <summary>Adds a sound group (normalised right away). <paramref name="volume"/> is the group's mix level.</summary>
        public static void Add(string key, float volume, float maxDistance, bool loop, params AudioClip[] clips)
        {
            if (string.IsNullOrEmpty(key)) return;
            var list = new List<AudioClip>();
            foreach (var c in clips ?? Array.Empty<AudioClip>()) if (c != null) { Normalize(c); list.Add(c); }
            _groups[key] = new Group { Clips = list.ToArray(), Volume = volume, MaxDistance = maxDistance, Loop = loop };
        }

        public static void Add(string key, params AudioClip[] clips) => Add(key, 1f, 30f, false, clips);

        /// <summary>Adds a group from embedded WAVs (names without ".wav").</summary>
        public static void Add(string key, ModAssets assets, params string[] wavNames)
        {
            var l = new List<AudioClip>();
            foreach (var n in wavNames) { var c = assets?.Clip(n); if (c != null) l.Add(c); }
            Add(key, l.ToArray());
        }

        public static bool Has(string key) => key != null && _groups.ContainsKey(key);
        public static IEnumerable<string> Keys => _groups.Keys;

        /// <summary>SECTR cue of a group (cached; random clip per play).</summary>
        public static SECTR_AudioCue Cue(string key)
        {
            if (key == null || !_groups.TryGetValue(key, out var g) || g.Clips.Length == 0) return null;
            return Snd.Cue("bank_" + key, g.Loop, g.Volume, g.MaxDistance, g.Clips);
        }

        /// <summary>Plays a group at a position.</summary>
        public static void Play(string key, Vector3 pos) => Snd.Play(Cue(key), pos);
    }

    /// <summary>
    /// A slime voice pack: fun / fear / damage / jump lines, balanced to one loudness and applied to definitions (directly or
    /// via <see cref="AnatomySpec.Voices"/> so largos pick it up).
    /// <code>var pack = VoicePack.FromAssets(ModAssets.Of&lt;MyMod&gt;(), "vo_glitch");   // vo_glitch_fun_01.wav, vo_glitch_fear_01.wav...
    /// pack.Apply(glitchDef);</code>
    /// </summary>
    public sealed class VoicePack
    {
        public string Name;
        public AudioClip[] Fun, Fear, Damage, Jump;
        /// <summary>Loudness of this pack (default <see cref="SoundBank.TargetRms"/>); 0.8 = a bit quieter than other slimes.</summary>
        public float Loudness = 1f;
        bool _balanced;

        static readonly Dictionary<string, VoicePack> _packs = new(StringComparer.OrdinalIgnoreCase);

        public VoicePack(string name) { Name = name; }

        /// <summary>Registered packs (by name).</summary>
        public static IEnumerable<VoicePack> All => _packs.Values;
        public static VoicePack Get(string name) => name != null && _packs.TryGetValue(name, out var p) ? p : null;
        /// <summary>Makes the pack available by name (e.g. for other mods or config files).</summary>
        public VoicePack Register() { _packs[Name] = this; return this; }

        /// <summary>
        /// Pack from embedded WAVs named <c>&lt;prefix&gt;_fun*</c>, <c>_fear*</c>, <c>_damage*</c> (or <c>_hurt*</c>),
        /// <c>_jump*</c>; several variants per line are fine.
        /// </summary>
        public static VoicePack FromAssets(ModAssets assets, string prefix, string name = null)
        {
            var p = new VoicePack(name ?? prefix);
            if (assets == null || string.IsNullOrEmpty(prefix)) return p;
            List<AudioClip> fun = new(), fear = new(), dmg = new(), jump = new();
            foreach (var full in assets.Names)
            {
                if (!full.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) continue;
                string n = full.Substring(0, full.Length - 4);
                int dot = n.LastIndexOf('.');
                if (dot >= 0) n = n.Substring(dot + 1);
                if (!n.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase)) continue;
                string rest = n.Substring(prefix.Length + 1).ToLowerInvariant();
                var list = rest.StartsWith("fun") || rest.StartsWith("happy") ? fun
                    : rest.StartsWith("fear") || rest.StartsWith("alarm") ? fear
                    : rest.StartsWith("damage") || rest.StartsWith("hurt") ? dmg
                    : rest.StartsWith("jump") ? jump : null;
                var clip = list != null ? assets.Clip(n) : null;
                if (clip != null) list.Add(clip);
            }
            p.Fun = fun.Count > 0 ? fun.ToArray() : null;
            p.Fear = fear.Count > 0 ? fear.ToArray() : null;
            p.Damage = dmg.Count > 0 ? dmg.ToArray() : null;
            p.Jump = jump.Count > 0 ? jump.ToArray() : null;
            if (p.Clips().Count == 0) ForgeLog.Warn($"[voices] no clips '{prefix}_fun|fear|damage|jump*' in {assets.Prefix}");
            return p;
        }

        /// <summary>All clips of the pack.</summary>
        public List<AudioClip> Clips()
        {
            var l = new List<AudioClip>();
            foreach (var a in new[] { Fun, Fear, Damage, Jump }) if (a != null) foreach (var c in a) if (c != null) l.Add(c);
            return l;
        }

        /// <summary>Normalises every line to the pack loudness (done automatically by <see cref="ToSet"/>).</summary>
        public VoicePack Balance()
        {
            if (_balanced) return this;
            _balanced = true;
            SoundBank.Balance(Clips(), SoundBank.TargetRms * Mathf.Max(0.05f, Loudness));
            return this;
        }

        /// <summary>The pack as <see cref="Voices.Set"/> (balanced).</summary>
        public Voices.Set ToSet() { Balance(); return new Voices.Set { Fun = Fun, Fear = Fear, Damage = Damage, Jump = Jump }; }

        /// <summary>Gives <paramref name="def"/> these voices now.</summary>
        public bool Apply(SlimeDefinition def) => Voices.Apply(def, ToSet());

        /// <summary>Declares the pack in the definition's anatomy (largos inherit it by <paramref name="priority"/>).</summary>
        public AnatomySpec ApplyToAnatomy(SlimeDefinition def, int priority = 0) => Anatomy.For(def).Voices(ToSet(), priority);

        public override string ToString() =>
            $"{Name}: fun {Fun?.Length ?? 0}, fear {Fear?.Length ?? 0}, damage {Damage?.Length ?? 0}, jump {Jump?.Length ?? 0}";
    }
}
