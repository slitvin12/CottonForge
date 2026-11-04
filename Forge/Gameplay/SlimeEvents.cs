using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Gameplay events of slime actors without writing Harmony patches: eating, being sprayed with water, natural spawns.
    /// Handlers are keyed by slime definition (null = every slime); <c>largos: true</c> also fires for largos containing it.
    /// Exceptions in handlers are caught and logged once.
    /// </summary>
    public static class SlimeEvents
    {
        sealed class H<T> { public SlimeDefinition Def; public bool Largos; public T Run; public string Name; }

        static readonly List<H<Action<GameObject, IdentifiableType>>> _eat = new();
        static readonly List<H<Action<GameObject>>> _water = new();
        static readonly List<Action<GameObject, IdentifiableType>> _spawn = new();

        /// <summary>
        /// <paramref name="a"/>(slime, food) after a slime of <paramref name="def"/> ate something (food, plort, chick, other
        /// slime for largos/tarr...). Runs in <c>SlimeEat.InvokeOnEat</c>.
        /// </summary>
        public static void OnEat(SlimeDefinition def, Action<GameObject, IdentifiableType> a, bool largos = true)
        {
            if (a != null) _eat.Add(new H<Action<GameObject, IdentifiableType>> { Def = def, Largos = largos, Run = a, Name = "eat:" + a.Method.Name });
        }

        /// <summary><paramref name="a"/>(slime) whenever a slime of <paramref name="def"/> is hit by water (water tank, rain, geyser...).</summary>
        public static void OnWater(SlimeDefinition def, Action<GameObject> a, bool largos = true)
        {
            if (a != null) _water.Add(new H<Action<GameObject>> { Def = def, Largos = largos, Run = a, Name = "water:" + a.Method.Name });
        }

        /// <summary>
        /// Component form: <paramref name="a"/>(component) when a slime carrying behaviour <typeparamref name="T"/> is hit by
        /// water — no definition lookup, the behaviour handles its own slime (<c>SlimeEvents.OnWater&lt;QuantumSlime&gt;(q =&gt; q.Calm())</c>).
        /// </summary>
        public static void OnWater<T>(Action<T> a) where T : Component
        {
            if (a == null) return;
            _water.Add(new H<Action<GameObject>> { Def = null, Largos = true, Name = "water:" + typeof(T).Name,
                Run = go => { var c = go.GetComponent<T>(); if (c != null) a(c); } });
        }

        /// <summary>Component form of <see cref="OnEat"/>: <paramref name="a"/>(component, food) for slimes carrying <typeparamref name="T"/>.</summary>
        public static void OnEat<T>(Action<T, IdentifiableType> a) where T : Component
        {
            if (a == null) return;
            _eat.Add(new H<Action<GameObject, IdentifiableType>> { Def = null, Largos = true, Name = "eat:" + typeof(T).Name,
                Run = (go, food) => { var c = go.GetComponent<T>(); if (c != null) a(c, food); } });
        }

        /// <summary><paramref name="a"/>(actor, type) for every actor spawned by a wild slime spawner.</summary>
        public static void OnNaturalSpawn(Action<GameObject, IdentifiableType> a) { if (a != null) _spawn.Add(a); }

        /// <summary>True when <paramref name="actual"/> is <paramref name="def"/> (or a largo of it with <paramref name="largos"/>); null def matches all.</summary>
        public static bool Matches(SlimeDefinition actual, SlimeDefinition def, bool largos)
        {
            if (def == null) return true;
            if (actual == null) return false;
            if (actual.Pointer == def.Pointer) return true;
            if (!largos || !actual.IsLargo || actual.BaseSlimes == null) return false;
            foreach (var b in actual.BaseSlimes) if (b != null && b.Pointer == def.Pointer) return true;
            return false;
        }

        internal static void RaiseEat(SlimeEat eat, IdentifiableType food)
        {
            if (eat == null) return;
            ActorRuntime.Signal(eat.gameObject, new SlimeSignal(SignalKind.Ate, food));
            if (_eat.Count == 0) return;
            var d = eat.SlimeDefinition;
            foreach (var h in _eat)
                if (Matches(d, h.Def, h.Largos))
                    try { h.Run(eat.gameObject, food); } catch (Exception e) { ForgeLog.WarnOnce(h.Name, $"SlimeEvents.OnEat handler failed: {e}"); }
        }

        internal static void RaiseWater(CalmedByWaterSpray c)
        {
            if (c == null) return;
            var go = c.gameObject;
            ActorRuntime.Signal(go, new SlimeSignal(SignalKind.Watered));
            if (_water.Count == 0) return;
            var d = World.IdentOf(go)?.TryCast<SlimeDefinition>();
            foreach (var h in _water)
                if (Matches(d, h.Def, h.Largos))
                    try { h.Run(go); } catch (Exception e) { ForgeLog.WarnOnce(h.Name, $"SlimeEvents.OnWater handler failed: {e}"); }
        }

        internal static void RaiseNaturalSpawn(GameObject go, IdentifiableType id)
        {
            if (_spawn.Count == 0 || go == null) return;
            foreach (var a in _spawn)
                try { a(go, id); } catch (Exception e) { ForgeLog.WarnOnce("spawn:" + a.Method.Name, $"SlimeEvents.OnNaturalSpawn handler failed: {e}"); }
        }
    }

    /// <summary>
    /// Takes over a slime's movement: SR2's random hopping (<c>SlimeRandomMove</c>) is skipped and, instead of leaping at
    /// food (<c>GotoConsumable</c>), <c>goToFood</c>(target position) is called every time SR2 would act — e.g. for
    /// slimes that roll, run, float or teleport. Release in <c>OnDisable</c>/<c>OnDestroy</c> of your component.
    /// </summary>
    public static class Movement
    {
        static readonly Dictionary<IntPtr, Action<Vector3>> _byBehaviour = new();
        static readonly Dictionary<IntPtr, List<IntPtr>> _bySlime = new();

        /// <summary>Number of slimes currently taken over.</summary>
        public static int Count => _bySlime.Count;

        /// <summary>Takes over <paramref name="slime"/> (see class summary). <paramref name="goToFood"/> null = the slime just doesn't chase food.</summary>
        public static void TakeOver(GameObject slime, Action<Vector3> goToFood = null)
        {
            if (slime == null) return;
            Release(slime);
            var keys = new List<IntPtr>();
            var run = goToFood ?? (_ => { });
            try { foreach (var c in slime.GetComponentsInChildren<SlimeRandomMove>(true)) { _byBehaviour[c.Pointer] = run; keys.Add(c.Pointer); } } catch { }
            try { foreach (var c in slime.GetComponentsInChildren<GotoConsumable>(true)) { _byBehaviour[c.Pointer] = run; keys.Add(c.Pointer); } } catch { }
            _bySlime[slime.Pointer] = keys;
        }

        /// <summary>Gives the slime back to SR2's movement.</summary>
        public static void Release(GameObject slime)
        {
            if (slime == null || !_bySlime.TryGetValue(slime.Pointer, out var keys)) return;
            foreach (var k in keys) _byBehaviour.Remove(k);
            _bySlime.Remove(slime.Pointer);
        }

        /// <summary>True while <see cref="TakeOver"/> is active for the slime.</summary>
        public static bool IsTakenOver(GameObject slime) => slime != null && _bySlime.ContainsKey(slime.Pointer);

        internal static bool Allow(IntPtr behaviour) => _byBehaviour.Count == 0 || !_byBehaviour.ContainsKey(behaviour);

        internal static bool Food(GotoConsumable g)
        {
            if (_byBehaviour.Count == 0 || !_byBehaviour.TryGetValue(g.Pointer, out var run)) return true;
            var t = g._target;
            if (t != null) run(t.transform.position);
            return false;
        }
    }

    [HarmonyPatch(typeof(SlimeEat), nameof(SlimeEat.InvokeOnEat))]
    static class SlimeEventsEatPatch
    {
        static void Postfix(SlimeEat __instance, IdentifiableType otherId) => Safe.Run("SlimeEvents eat", () => SlimeEvents.RaiseEat(__instance, otherId));
    }

    [HarmonyPatch(typeof(CalmedByWaterSpray), nameof(CalmedByWaterSpray.AddLiquid))]
    static class SlimeEventsWaterPatch
    {
        static void Postfix(CalmedByWaterSpray __instance) => Safe.Run("SlimeEvents water", () => SlimeEvents.RaiseWater(__instance));
    }

    [HarmonyPatch(typeof(SlimeRandomMove), nameof(SlimeRandomMove.Action))]
    static class MovementHopPatch
    {
        static bool Prefix(SlimeRandomMove __instance)
        {
            try { return Movement.Allow(__instance.Pointer); } catch { return true; }
        }
    }

    [HarmonyPatch(typeof(GotoConsumable), nameof(GotoConsumable.Action))]
    static class MovementFoodPatch
    {
        static bool Prefix(GotoConsumable __instance)
        {
            try { return Movement.Food(__instance); } catch { return true; }
        }
    }
}
