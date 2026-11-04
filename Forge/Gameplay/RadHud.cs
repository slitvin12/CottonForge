using System;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.UI;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// SR1's radiation meter. SR2 still ships it in the HUD ("Rad Meter": StatusBar + RadMeter with the rad symbol) but
    /// keeps it switched off. <see cref="Enable"/> places it above the health meter and switches it on (the meter itself
    /// shows only while the player is irradiated) and optionally lets radiation wear off like in SR1. For mods that
    /// irradiate the player (rad slimes, hazards): call <see cref="NoteExposure"/> while exposing.
    /// </summary>
    public static class RadHud
    {
        static RadMeter _meter;
        static float _next, _nextFind, _prevRads = -1f, _recoverTick, _lastExposure = -100f;
        static bool _logged;

        /// <summary>True after <see cref="Enable"/>.</summary>
        public static bool Enabled { get; private set; }
        /// <summary>Offset from the health meter (HUD units).</summary>
        public static Vector2 Offset = new(0f, 78f);
        /// <summary>Radiation removed per second when not exposed for 3 s (0 = leave it to the game).</summary>
        public static float RecoveryPerSecond;

        /// <summary>Switches the meter on (and the SR1-style recovery when <paramref name="recoveryPerSecond"/> &gt; 0).</summary>
        public static void Enable(float recoveryPerSecond = 0f, Vector2? offset = null)
        {
            Enabled = true;
            RecoveryPerSecond = Mathf.Max(0f, recoveryPerSecond);
            if (offset.HasValue) Offset = offset.Value;
            if (_meter != null) Place();
        }

        /// <summary>Call while the player is being irradiated (recovery waits 3 s after the last call).</summary>
        public static void NoteExposure() => _lastExposure = Time.time;

        static RadMeter Find()
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<RadMeter>())
                if (m != null && m.gameObject.scene.IsValid()) return m;
            return null;
        }

        /// <summary>Re-applies the position (after changing <see cref="Offset"/>).</summary>
        public static void Place()
        {
            if (_meter == null) return;
            var rt = _meter.GetComponent<RectTransform>();
            if (rt == null) return;
            var parent = _meter.transform.parent;
            RectTransform health = null;
            if (parent != null)
                for (int i = 0; i < parent.childCount; i++)
                {
                    var c = parent.GetChild(i);
                    if (c != null && c.name.StartsWith("Health", StringComparison.OrdinalIgnoreCase)) { health = c.GetComponent<RectTransform>(); break; }
                }
            if (health != null)
            {
                rt.anchorMin = health.anchorMin; rt.anchorMax = health.anchorMax; rt.pivot = health.pivot;
                rt.sizeDelta = health.sizeDelta; rt.localScale = health.localScale;
                rt.anchoredPosition = health.anchoredPosition + Offset;
            }
            if (!_logged) { _logged = true; ForgeLog.Msg($"Rad meter enabled at {rt.anchoredPosition}"); }
        }

        internal static void Tick()
        {
            if (!Enabled || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            var ps = SceneContext.Instance != null ? SceneContext.Instance.PlayerState : null;
            if (ps == null) return;
            if (_meter == null && Time.unscaledTime > _nextFind) { _nextFind = Time.unscaledTime + 3f; _meter = Find(); if (_meter != null) Place(); }
            if (_meter != null && !_meter.gameObject.activeSelf) { Place(); _meter.gameObject.SetActive(true); }
            if (RecoveryPerSecond <= 0f) return;
            float rads = ps.GetCurrRad();
            bool selfRecovered = _prevRads >= 0f && rads < _prevRads && Time.time - _recoverTick > 0.6f;   // the game already does it
            if (rads > 0f && !selfRecovered && Time.time - _lastExposure > 3f)
            {
                ps.RemoveRads(RecoveryPerSecond * 0.5f);
                _recoverTick = Time.time;
                rads = ps.GetCurrRad();
            }
            _prevRads = rads;
        }
    }
}
