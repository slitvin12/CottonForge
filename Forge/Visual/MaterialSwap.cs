using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Temporary material changes on one actor with exact restore: state looks (charged, phasing, glitching, frozen),
    /// hiding parts (eyes, mouth, body) and fades. Keep one instance per component and call <see cref="RestoreAll"/> in
    /// <c>OnDisable</c>/<c>OnDestroy</c>; destroyed renderers are skipped. Restoring puts back exactly what was there before
    /// the first change of each slot, however many changes followed.
    /// </summary>
    public sealed class MaterialSwap
    {
        sealed class Slot { public Renderer R; public int I; public Material Orig; }
        readonly Dictionary<(IntPtr, int), Slot> _slots = new();
        readonly Dictionary<IntPtr, (Renderer r, bool en)> _hidden = new();

        /// <summary>True while any slot is changed or any renderer hidden.</summary>
        public bool Active => _slots.Count > 0 || _hidden.Count > 0;

        /// <summary>Puts <paramref name="mat"/> into slot <paramref name="index"/> of <paramref name="r"/> (remembers the original once).</summary>
        public void Set(Renderer r, int index, Material mat)
        {
            if (r == null || mat == null) return;
            var mats = r.sharedMaterials;
            if (index < 0 || index >= mats.Length) return;
            var key = (r.Pointer, index);
            if (!_slots.ContainsKey(key)) _slots[key] = new Slot { R = r, I = index, Orig = mats[index] };
            if (mats[index] == mat) return;
            mats[index] = mat;
            r.sharedMaterials = mats;
        }

        /// <summary>
        /// Replaces every slot of the actor's renderers whose material passes <paramref name="match"/> (null = all) with
        /// <paramref name="mat"/> or with <paramref name="map"/>(original). Returns the number of slots changed.
        /// </summary>
        public int SetWhere(GameObject actor, Func<Renderer, Material, bool> match, Material mat = null, Func<Material, Material> map = null, bool includeInactive = true)
        {
            if (actor == null || (mat == null && map == null)) return 0;
            int n = 0;
            foreach (var r in actor.GetComponentsInChildren<Renderer>(includeInactive))
            {
                if (r == null || r.gameObject.name.StartsWith(MeshKit.LayerPrefix, StringComparison.Ordinal)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var orig = _slots.TryGetValue((r.Pointer, i), out var s) ? s.Orig : mats[i];
                    if (orig == null || (match != null && !match(r, orig))) continue;
                    var m = mat ?? map(orig);
                    if (m == null) continue;
                    Set(r, i, m);
                    n++;
                }
            }
            return n;
        }

        /// <summary>Swaps the body material (the definition's body material and its instances, see <see cref="MeshKit.BodyRenderer"/>).</summary>
        public int SetBody(GameObject actor, Material mat)
        {
            var body = MeshKit.BodyRenderer(actor);
            if (body == null || mat == null) return 0;
            var origName = (_slots.TryGetValue((body.Pointer, 0), out var s) ? s.Orig : body.sharedMaterial)?.name;
            if (origName == null) return 0;
            return SetWhere(actor, (r, m) => m.name == origName || m.name.StartsWith(origName, StringComparison.Ordinal), mat);
        }

        /// <summary>Hides (or shows again) a renderer, remembering its original enabled state.</summary>
        public void Hide(Renderer r, bool hide = true)
        {
            if (r == null) return;
            if (hide)
            {
                if (!_hidden.ContainsKey(r.Pointer)) _hidden[r.Pointer] = (r, r.enabled);
                r.enabled = false;
            }
            else if (_hidden.TryGetValue(r.Pointer, out var h)) { r.enabled = h.en; _hidden.Remove(r.Pointer); }
        }

        /// <summary>Restores one slot.</summary>
        public void Restore(Renderer r, int index)
        {
            if (r == null || !_slots.TryGetValue((r.Pointer, index), out var s)) return;
            _slots.Remove((r.Pointer, index));
            var mats = r.sharedMaterials;
            if (index < mats.Length && mats[index] != s.Orig) { mats[index] = s.Orig; r.sharedMaterials = mats; }
        }

        /// <summary>Restores every changed slot and hidden renderer.</summary>
        public void RestoreAll()
        {
            foreach (var s in _slots.Values)
            {
                try
                {
                    if (s.R == null) continue;
                    var mats = s.R.sharedMaterials;
                    if (s.I < mats.Length && mats[s.I] != s.Orig) { mats[s.I] = s.Orig; s.R.sharedMaterials = mats; }
                }
                catch { }
            }
            _slots.Clear();
            foreach (var h in _hidden.Values) try { if (h.r != null) h.r.enabled = h.en; } catch { }
            _hidden.Clear();
        }
    }
}
