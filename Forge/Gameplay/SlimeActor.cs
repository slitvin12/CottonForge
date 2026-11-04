using System;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Cached view of one slime actor for behaviour components: definition, rigidbody, emotions, vacpack state, size.
    /// Create it in <c>Start</c> (<c>_s = new SlimeActor(gameObject)</c>) instead of repeating the same GetComponent calls
    /// and try/catch guards in every component. All getters are safe on destroyed objects (they return neutral values).
    /// </summary>
    public sealed class SlimeActor
    {
        /// <summary>The actor.</summary>
        public GameObject Go { get; }
        /// <summary>Its definition (a largo has its own definition, see <see cref="IsLargo"/>/<see cref="Has"/>).</summary>
        public SlimeDefinition Def { get; }
        /// <summary>Rigidbody (may be null).</summary>
        public Rigidbody Rb { get; }
        /// <summary>Emotions (may be null).</summary>
        public SlimeEmotions Emotions { get; }
        /// <summary>Vacuumable (may be null).</summary>
        public Vacuumable Vac { get; }
        /// <summary>SlimeEat (may be null).</summary>
        public SlimeEat Eat { get; }

        /// <summary>Wraps <paramref name="slime"/> (the actor root or any child).</summary>
        public SlimeActor(GameObject slime)
        {
            if (slime == null) throw new ArgumentNullException(nameof(slime));
            var root = slime.GetComponentInParent<IdentifiableActor>();
            Go = root != null ? root.gameObject : slime;
            Def = World.IdentOf(Go)?.TryCast<SlimeDefinition>();
            Rb = Go.GetComponent<Rigidbody>();
            Emotions = Go.GetComponent<SlimeEmotions>();
            Vac = Go.GetComponent<Vacuumable>();
            Eat = Go.GetComponent<SlimeEat>();
        }

        /// <summary>False once the actor is destroyed.</summary>
        public bool Alive => Go != null;
        /// <summary>True for largos.</summary>
        public bool IsLargo => Def != null && Def.IsLargo;
        /// <summary>True when the actor is <paramref name="def"/> or a largo containing it.</summary>
        public bool Has(SlimeDefinition def) => SlimeEvents.Matches(Def, def, true);

        /// <summary>Held in the vacpack's beam or carried.</summary>
        public bool Held { get { try { return Vac != null && Vac.IsHeld(); } catch { return false; } } }
        /// <summary>Just shot out of the vacpack (still flying).</summary>
        public bool Launched { get { try { return Vac != null && Vac.IsLaunched(); } catch { return false; } } }
        /// <summary>Caught in a snare / joint (e.g. a captured gordo bait).</summary>
        public bool Captive { get { try { return Vac != null && Vac.IsCaptive(); } catch { return false; } } }
        /// <summary>Caught by a tornado.</summary>
        public bool Tornadoed { get { try { return Vac != null && Vac.IsTornadoed(); } catch { return false; } } }
        /// <summary>Sleeping (night, sloomber).</summary>
        public bool Asleep { get { try { return Emotions != null && Emotions.IsSleeping(); } catch { return false; } } }
        /// <summary>The vacpack can't grab the slime for <paramref name="seconds"/> (e.g. while it teleports or dashes).</summary>
        public void PreventCapture(float seconds) { try { if (Vac != null) Vac.PreventCaptureFor(seconds); } catch { } }
        /// <summary>Agitation 0..1.</summary>
        public float Agitation => World.Agitation(Emotions);
        /// <summary>Hunger 0..1 (1 = starving).</summary>
        public float Hunger => World.Hunger(Emotions);
        /// <summary>Fear 0..1.</summary>
        public float Fear => World.Fear(Emotions);
        /// <summary>World scale (largos are bigger).</summary>
        public float Scale => Go != null ? Go.transform.lossyScale.x : 1f;
        /// <summary>Centre of the body (the pivot is at the bottom).</summary>
        public Vector3 Center => Go != null ? Go.transform.position + Vector3.up * (0.5f * Go.transform.lossyScale.y) : Vector3.zero;
        /// <summary>Ground below the slime.</summary>
        public bool Grounded => Go != null && World.Grounded(Go.transform, Scale);
        /// <summary>Distance from the body centre to the player's chest.</summary>
        public float DistToPlayer => World.DistToPlayer(Center);
        /// <summary>Horizontal speed (m/s).</summary>
        public float Speed { get { if (Rb == null) return 0f; var v = Rb.linearVelocity; return new Vector2(v.x, v.z).magnitude; } }
        /// <summary>Free to act: alive, not held, not flying out of the vacpack, not kinematic (posed / in a machine).</summary>
        public bool Free => Alive && !Held && !Launched && !Captive && !Tornadoed && (Rb == null || !Rb.isKinematic);

        /// <summary>Changes an emotion by <paramref name="delta"/> (clamped by the game).</summary>
        public void Emote(SlimeEmotions.Emotion e, float delta)
        {
            try { if (Emotions != null) Emotions.Adjust(e, delta); } catch { }
        }

        /// <summary>Sets an emotion (0..1).</summary>
        public void SetEmotion(SlimeEmotions.Emotion e, float value)
        {
            try { if (Emotions != null) Emotions.Set(e, Mathf.Clamp01(value)); } catch { }
        }
    }
}
