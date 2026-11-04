using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Components for slime actors. They are NOT put on prefabs: Cotton copies prefab components for every largo with
    /// reflection (<c>MergeComponents</c>/<c>CopyFields</c>), which breaks injected MonoBehaviours and crashed largo generation.
    /// Forge adds them to each spawned actor instead (<c>SlimeEat.Awake</c> postfix), keyed by definition.
    /// </summary>
    public static class Behaviours
    {
        static readonly Dictionary<IntPtr, Action<GameObject>> _byDef = new();
        static readonly HashSet<Type> _registered = new();

        /// <summary>Number of definitions with attached behaviours.</summary>
        public static int Mapped => _byDef.Count;

        /// <summary>
        /// Registers a managed MonoBehaviour type in IL2CPP once (needs a <c>public T(IntPtr ptr) : base(ptr) { }</c> constructor).
        /// Game types are skipped. Call it before the first <c>AddComponent&lt;T&gt;</c>.
        /// </summary>
        public static void Register<T>() where T : class
        {
            if (!_registered.Add(typeof(T))) return;
            if (Il2CppClassPointerStore<T>.NativeClassPtr != IntPtr.Zero) return;   // game type or already injected
            try { ClassInjector.RegisterTypeInIl2Cpp<T>(); ForgeLog.Debug("[types] registered " + typeof(T).FullName); }
            catch (Exception e) { ForgeLog.Error($"Registering {typeof(T).FullName} in IL2CPP failed: {e}"); }
        }

        /// <summary>Calls <paramref name="add"/> for every spawned actor of <paramref name="def"/>.</summary>
        public static void Attach(SlimeDefinition def, Action<GameObject> add)
        {
            if (def == null || add == null) return;
            _byDef[def.Pointer] = _byDef.TryGetValue(def.Pointer, out var prev) ? prev + add : add;
        }

        /// <summary>Adds component <typeparamref name="T"/> to every spawned actor of <paramref name="def"/>.</summary>
        public static void Attach<T>(SlimeDefinition def) where T : Component
        {
            Register<T>();
            Attach(def, go => { if (go.GetComponent<T>() == null) go.AddComponent<T>(); });
        }

        internal static void OnSlimeAwake(SlimeEat eat)
        {
            if (_byDef.Count == 0 || eat == null) return;
            var d = eat.SlimeDefinition;
            if (d == null || !_byDef.TryGetValue(d.Pointer, out var add)) return;
            foreach (Action<GameObject> a in add.GetInvocationList())
            {
                try { a(eat.gameObject); }
                catch (Exception e) { ForgeLog.WarnOnce("attach:" + d.name + a.Method.Name, $"Attaching behaviour to {d.name} failed: {e}"); }
            }
        }
    }

    [HarmonyPatch(typeof(SlimeEat), nameof(SlimeEat.Awake))]
    static class SlimeEatAwakePatch
    {
        static void Postfix(SlimeEat __instance)
        {
            Safe.Run("attach behaviours", () => Behaviours.OnSlimeAwake(__instance));
            Safe.Run("apply anatomy", () => Anatomy.OnSlimeAwake(__instance));
        }
    }
}
