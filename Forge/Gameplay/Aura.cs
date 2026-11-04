using System;
using Il2Cpp;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Effects of a slime on the player within a radius (radiation, damage), the SR1 rad-slime way: call one of
    /// the methods every frame (or tick) from your component with the elapsed time.
    /// </summary>
    public static class Aura
    {
        static PlayerRadAbsorber _absorber;
        static float _fallbackTick;

        /// <summary>True when the player's chest is within <paramref name="radius"/> (+ <paramref name="margin"/>) of <paramref name="center"/>.</summary>
        public static bool PlayerInside(Vector3 center, float radius, float margin = 0.35f) => World.DistToPlayer(center) <= radius + margin;

        /// <summary>SR2's radiation receiver on the player (cached).</summary>
        public static PlayerRadAbsorber Absorber
        {
            get
            {
                if (_absorber != null) return _absorber;
                var p = World.Player;
                if (p != null) _absorber = p.GetComponent<PlayerRadAbsorber>() ?? p.GetComponentInChildren<PlayerRadAbsorber>();
                return _absorber;
            }
        }

        /// <summary>
        /// Irradiates the player inside the sphere: <paramref name="radsPerSecond"/> * <paramref name="dt"/> through SR2's
        /// <c>PlayerRadAbsorber</c> (the game's rad meter, rad damage and sounds), otherwise
        /// <paramref name="fallbackDamage"/> every 2 s. Notifies <see cref="RadHud"/>. Returns true when the player was inside.
        /// </summary>
        public static bool Irradiate(GameObject source, Vector3 center, float radius, float radsPerSecond, float dt, int fallbackDamage = 1)
        {
            if (!World.InGame || !PlayerInside(center, radius)) return false;
            RadHud.NoteExposure();
            var a = Absorber;
            if (a != null)
            {
                try { a.Absorb(source, radsPerSecond * dt); return true; }
                catch (Exception e) { ForgeLog.WarnOnce("aura:absorb", "Aura.Irradiate: " + e.Message); }
            }
            if (Time.time > _fallbackTick) { _fallbackTick = Time.time + 2f; Dmg.Player(fallbackDamage, source, "RadAbsorb", "SlimeAttack"); }
            return true;
        }

        /// <summary>
        /// Damages the player inside the sphere at most once per <paramref name="interval"/> seconds (counted per
        /// <paramref name="source"/> object). <paramref name="sources"/> = damage source names (see
        /// <c>dmgsources</c>). Returns true when damage was dealt.
        /// </summary>
        public static bool Damage(GameObject source, Vector3 center, float radius, int amount, float interval, params string[] sources)
        {
            if (!World.InGame || source == null || !PlayerInside(center, radius)) return false;
            int key = source.GetInstanceID();
            if (_next.TryGetValue(key, out var t) && Time.time < t) return false;
            _next[key] = Time.time + interval;
            return Dmg.Player(amount, source, sources.Length > 0 ? sources : new[] { "SlimeAttack" });
        }

        static readonly System.Collections.Generic.Dictionary<int, float> _next = new();
    }
}
