using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppMonomiPark.SlimeRancher.UI.Pedia;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Slimepedia picture for modded types. A definition cloned from Pink still points at the Pink full art (addressable), so
    /// the pedia shows a pink slime; Forge clears the full art and shows this sprite instead (the slime's icon by default,
    /// see <see cref="SlimeBuilder.PediaArt(string)"/>). It is re-applied while the page is open because the game loads the
    /// full art asynchronously and would overwrite it.
    /// </summary>
    public static class PediaArt
    {
        static readonly Dictionary<IntPtr, Sprite> _art = new();
        static readonly Dictionary<IntPtr, PediaEntryIconAndHighlights> _views = new();
        static readonly List<IntPtr> _drop = new();

        /// <summary>Shows <paramref name="art"/> as the big pedia picture of <paramref name="ident"/>.</summary>
        public static void Register(IdentifiableType ident, Sprite art)
        {
            if (ident == null || art == null) return;
            _art[ident.Pointer] = art;
        }

        /// <summary>Registered art of a pedia entry, or null.</summary>
        public static Sprite For(PediaEntry entry)
        {
            if (entry == null || _art.Count == 0) return null;
            var id = entry.As<IdentifiablePediaEntry>()?._identifiableType;
            return id != null && _art.TryGetValue(id.Pointer, out var sp) ? sp : null;
        }

        internal static void Shown(PediaEntryIconAndHighlights view, PediaEntry entry)
        {
            if (view == null) return;
            var sp = For(entry);
            if (sp == null) { _views.Remove(view.Pointer); return; }
            _views[view.Pointer] = view;
            Apply(view, sp);
        }

        static void Apply(PediaEntryIconAndHighlights view, Sprite sp)
        {
            var img = view._primaryIcon;
            if (img == null) return;
            if (img.sprite == null || img.sprite.Pointer != sp.Pointer) img.sprite = sp;
            img.preserveAspect = true;
            if (!img.enabled) img.enabled = true;
            var c = img.color;
            if (c.a < 1f) img.color = new Color(c.r, c.g, c.b, 1f);
        }

        internal static void Tick()
        {
            if (_views.Count == 0) return;
            _drop.Clear();
            foreach (var kv in _views)
            {
                try
                {
                    var v = kv.Value;
                    var sp = v != null ? For(v._currentEntry) : null;
                    if (sp == null) { _drop.Add(kv.Key); continue; }
                    if (v.isActiveAndEnabled) Apply(v, sp);
                }
                catch (Exception e) { _drop.Add(kv.Key); ForgeLog.WarnOnce("pediaart", "Pedia art: " + e.Message); }
            }
            foreach (var k in _drop) _views.Remove(k);
        }
    }

    [HarmonyPatch(typeof(PediaEntryIconAndHighlights), nameof(PediaEntryIconAndHighlights.SetPediaEntry))]
    static class PediaArtPatch
    {
        static void Postfix(PediaEntryIconAndHighlights __instance, PediaEntry entry) => Safe.Run("pedia art", () => PediaArt.Shown(__instance, entry));
    }
}
