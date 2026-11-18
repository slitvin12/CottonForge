using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// GC safety for IL2CPP. Il2CppInterop writes elements of <c>Il2CppReferenceArray</c>/<c>Il2CppStringArray</c> with a raw
    /// pointer store (no IL2CPP GC write barrier) while SR2 runs Unity's incremental GC. An object referenced only by such
    /// an element can be freed in the middle of a GC cycle: garbage string lengths, "Length cannot be less than zero",
    /// OutOfMemoryException and finally an access violation in coreclr.dll.
    /// <list type="bullet">
    /// <item>Heavy one-shot work (creating definitions, largo post-processing): <see cref="Window"/>. Forge already pauses
    /// the GC around Cotton's whole content generation in the main menu (<c>SafeGc</c>).</item>
    /// <item>Reference arrays written during gameplay: build them with <see cref="Array{T}"/> or set elements with <see cref="Set{T}"/>.</item>
    /// </list>
    /// </summary>
    public static class Gc
    {
        static int _depth;
        static float _since;
        static string _why;

        /// <summary>True while the GC is paused by Forge.</summary>
        public static bool Paused => _depth > 0;

        /// <summary>Finishes the running GC cycle and disables the GC. Calls nest; every Pause needs a Resume.</summary>
        public static void Pause(string why)
        {
            if (ForgeConfig.SafeGc != null && !ForgeConfig.SafeGc.Value) return;
            if (_depth++ > 0) return;
            try
            {
                // UnityEngine.Scripting.GarbageCollector.GCMode is stripped in SR2: use the IL2CPP API directly.
                IL2CPP.il2cpp_gc_collect(0);
                IL2CPP.il2cpp_gc_disable();
                _since = Time.realtimeSinceStartup;
                _why = why;
                ForgeLog.Debug($"[gc] paused (incremental: {Incremental()}): {why}");
            }
            catch (Exception e) { _depth = 0; ForgeLog.Warn("[gc] pause failed: " + e.Message); }
        }

        /// <summary>Re-enables the GC when the outermost <see cref="Pause"/> is matched.</summary>
        public static void Resume(string why)
        {
            if (_depth == 0 || --_depth > 0) return;
            try
            {
                IL2CPP.il2cpp_gc_enable();
                ForgeLog.Msg($"[gc] resumed after {(Time.realtimeSinceStartup - _since) * 1000f:0} ms ({_why} -> {why})");
            }
            catch (Exception e) { ForgeLog.Warn("[gc] resume failed: " + e.Message); }
        }

        /// <summary>Runs <paramref name="a"/> with the GC paused.</summary>
        public static void Window(string why, Action a)
        {
            Pause(why);
            try { a(); }
            finally { Resume(why); }
        }

        [DllImport("GameAssembly", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        static extern bool il2cpp_gc_is_incremental();

        /// <summary>"yes"/"no"/"?": whether the game runs the incremental GC.</summary>
        public static string Incremental()
        {
            try { return il2cpp_gc_is_incremental() ? "yes" : "no"; } catch { return "?"; }
        }

        static IntPtr ElementAddress(IntPtr array, int index) => IntPtr.Add(array, 4 * IntPtr.Size + index * IntPtr.Size); // Il2CppObject (2 ptr) + bounds + max_length

        /// <summary>Builds an IL2CPP reference array storing every element through the GC write barrier.</summary>
        public static Il2CppReferenceArray<T> Array<T>(IList<T> items) where T : Il2CppObjectBase
        {
            var arr = new Il2CppReferenceArray<T>(items.Count);
            if (IL2CPP.il2cpp_class_is_valuetype(Il2CppClassPointerStore<T>.NativeClassPtr))
            {
                for (int i = 0; i < items.Count; i++) arr[i] = items[i];
                return arr;
            }
            for (int i = 0; i < items.Count; i++)
                IL2CPP.il2cpp_gc_wbarrier_set_field(arr.Pointer, ElementAddress(arr.Pointer, i), (object)items[i] == null ? IntPtr.Zero : items[i].Pointer);
            return arr;
        }

        /// <summary>Sets one element of a reference array through the GC write barrier.</summary>
        public static void Set<T>(Il2CppReferenceArray<T> array, int index, T value) where T : Il2CppObjectBase
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index >= array.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (IL2CPP.il2cpp_class_is_valuetype(Il2CppClassPointerStore<T>.NativeClassPtr))
            {
                array[index] = value;
                return;
            }
            IL2CPP.il2cpp_gc_wbarrier_set_field(array.Pointer, ElementAddress(array.Pointer, index), (object)value == null ? IntPtr.Zero : value.Pointer);
        }

        /// <summary>Copy of <paramref name="array"/> with <paramref name="item"/> appended (write-barrier safe).</summary>
        public static Il2CppReferenceArray<T> Append<T>(Il2CppReferenceArray<T> array, T item) where T : Il2CppObjectBase
        {
            var list = new List<T>(array != null ? array.Length + 1 : 1);
            if (array != null) foreach (var x in array) list.Add(x);
            list.Add(item);
            return Array(list);
        }

        /// <summary>Copy of <paramref name="array"/> with <paramref name="item"/> appended using the IL2CPP GC write barrier.</summary>
        public static Il2CppStringArray StringArrayAppend(Il2CppStringArray array, string item)
        {
            int len = array != null ? array.Length : 0;
            var res = new Il2CppStringArray(len + 1);
            for (int i = 0; i < len; i++)
            {
                string s = array[i];
                IL2CPP.il2cpp_gc_wbarrier_set_field(res.Pointer, ElementAddress(res.Pointer, i), s == null ? IntPtr.Zero : IL2CPP.ManagedStringToIl2Cpp(s));
            }
            IL2CPP.il2cpp_gc_wbarrier_set_field(res.Pointer, ElementAddress(res.Pointer, len), item == null ? IntPtr.Zero : IL2CPP.ManagedStringToIl2Cpp(item));
            return res;
        }
    }
}
