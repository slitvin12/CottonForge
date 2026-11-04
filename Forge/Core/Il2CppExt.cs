using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Il2CppInterop pitfalls in one place:
    /// <list type="bullet">
    /// <item><c>obj is T</c> / <c>as T</c> look at the managed wrapper type, not the real IL2CPP type: use <see cref="Is{T}"/> / <c>TryCast</c>.</item>
    /// <item>One native object can have several wrappers: compare and hash by <c>Pointer</c> (<see cref="Same"/>, <see cref="PtrSet{T}"/>).</item>
    /// <item><c>?.</c> and <c>??</c> bypass Unity's overloaded <c>==</c>: for Unity objects use <c>x == null</c> or <see cref="Alive"/>.</item>
    /// </list>
    /// </summary>
    public static class Il2CppExt
    {
        /// <summary>Real IL2CPP type check (unlike <c>is</c>).</summary>
        public static bool Is<T>(this Il2CppObjectBase o) where T : Il2CppObjectBase
        {
            if ((object)o == null) return false;
            try { return o.TryCast<T>() != null; } catch { return false; }
        }

        /// <summary><c>TryCast</c> that tolerates null and destroyed objects.</summary>
        public static T As<T>(this Il2CppObjectBase o) where T : Il2CppObjectBase
        {
            if ((object)o == null) return null;
            try { return o.TryCast<T>(); } catch { return null; }
        }

        /// <summary>Same native object (wrapper-independent).</summary>
        public static bool Same(this Il2CppObjectBase a, Il2CppObjectBase b) =>
            (object)a != null && (object)b != null && a.Pointer == b.Pointer;

        /// <summary>Unity-aware "not null and not destroyed".</summary>
        public static bool Alive(this UnityEngine.Object o) => o != null;

        /// <summary>IL2CPP reference array built through the GC write barrier (<see cref="Gc.Array{T}"/>).</summary>
        public static Il2CppReferenceArray<T> ToIl2CppArray<T>(this IEnumerable<T> items) where T : Il2CppObjectBase =>
            Gc.Array(items as IList<T> ?? items.ToList());

        /// <summary>Managed list copy of an IL2CPP array (null-safe).</summary>
        public static List<T> ToManagedList<T>(this Il2CppArrayBase<T> arr)
        {
            var list = new List<T>(arr?.Length ?? 0);
            if (arr != null) foreach (var x in arr) list.Add(x);
            return list;
        }

        /// <summary>Managed list copy of an IL2CPP list (null-safe).</summary>
        public static List<T> ToManagedList<T>(this Il2CppSystem.Collections.Generic.List<T> list)
        {
            var r = new List<T>(list?.Count ?? 0);
            if (list != null) for (int i = 0; i < list.Count; i++) r.Add(list[i]);
            return r;
        }
    }

    /// <summary>Set of IL2CPP objects keyed by native pointer (one native object = one entry, whatever the wrapper).</summary>
    public sealed class PtrSet<T> : IEnumerable<T> where T : Il2CppObjectBase
    {
        readonly Dictionary<IntPtr, T> _d = new();

        public int Count => _d.Count;
        public bool Add(T o) { if ((object)o == null || _d.ContainsKey(o.Pointer)) return false; _d[o.Pointer] = o; return true; }
        public bool Contains(T o) => (object)o != null && _d.ContainsKey(o.Pointer);
        public bool Contains(IntPtr p) => _d.ContainsKey(p);
        public bool Remove(T o) => (object)o != null && _d.Remove(o.Pointer);
        public void Clear() => _d.Clear();
        public IEnumerator<T> GetEnumerator() => _d.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Dictionary keyed by IL2CPP object pointer.</summary>
    public sealed class PtrMap<TKey, TValue> where TKey : Il2CppObjectBase
    {
        readonly Dictionary<IntPtr, TValue> _d = new();

        public int Count => _d.Count;
        public TValue this[TKey k] { get => _d[k.Pointer]; set => _d[k.Pointer] = value; }
        public bool TryGetValue(TKey k, out TValue v) { if ((object)k == null) { v = default; return false; } return _d.TryGetValue(k.Pointer, out v); }
        public bool ContainsKey(TKey k) => (object)k != null && _d.ContainsKey(k.Pointer);
        public bool Remove(TKey k) => (object)k != null && _d.Remove(k.Pointer);
        public void Clear() => _d.Clear();
        public IEnumerable<TValue> Values => _d.Values;
    }
}
