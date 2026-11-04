using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Bone = Il2Cpp.SlimeAppearance.SlimeBone;
using EType = Il2Cpp.SlimeAppearanceElement.ElementType;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Slime appearance helpers: body material, structures (extra meshes on the slime skeleton), radiant variant.
    /// <para><c>Structures[0]</c> is the body; find the others by <c>Element.Type</c> (FACE, AURA, ...) or <c>Element.Name</c>.
    /// Insert new structures before the face (<see cref="InsertStructure"/>); arrays are rebuilt through the GC write barrier.</para>
    /// </summary>
    public static class Looks
    {
        static GameObject _root;

        /// <summary>Inactive, never saved parent for prefab-like objects you build at runtime (structure objects, templates).</summary>
        public static Transform PrefabRoot
        {
            get
            {
                if (_root == null)
                {
                    _root = new GameObject("ForgePrefabs");
                    _root.SetActive(false);
                    _root.hideFlags = HideFlags.HideAndDontSave;
                    Object.DontDestroyOnLoad(_root);
                }
                return _root.transform;
            }
        }

        /// <summary>Body material (structure 0, material 0) or null.</summary>
        public static Material BodyMaterial(SlimeAppearance app)
        {
            if (app == null) return null;
            var st = app.Structures;
            if (st == null || st.Length == 0 || st[0] == null) return null;
            var m = st[0].DefaultMaterials;
            return m != null && m.Length > 0 ? m[0] : null;
        }

        /// <summary>Sets the body material; an optional second one is drawn over the same mesh (SR2 does this for the radiant aura).</summary>
        public static void SetBodyMaterial(SlimeAppearance app, Material m, Material second = null)
        {
            if (app == null || m == null) return;
            app.Structures[0].DefaultMaterials = Gc.Array(second != null ? new List<Material> { m, second } : new List<Material> { m });
        }

        /// <summary>Index of the FACE structure or -1.</summary>
        public static int FaceIndex(SlimeAppearance app) => IndexOf(app, s => s.Element != null && s.Element.Type == EType.FACE);

        /// <summary>Index of the first structure matching the predicate or -1.</summary>
        public static int IndexOf(SlimeAppearance app, Func<SlimeAppearanceStructure, bool> match)
        {
            var arr = app?._structures;
            if (arr == null) return -1;
            for (int i = 0; i < arr.Length; i++) if (arr[i] != null && match(arr[i])) return i;
            return -1;
        }

        /// <summary>True when a structure with this element name exists.</summary>
        public static bool HasElement(SlimeAppearance app, string elementName) => IndexOf(app, s => s.Element != null && s.Element.Name == elementName) >= 0;

        /// <summary>Inserts a structure before the face (or at the end).</summary>
        public static void InsertStructure(SlimeAppearance app, SlimeAppearanceStructure st)
        {
            var list = app._structures.ToManagedList();
            int fi = FaceIndex(app);
            if (fi < 0) list.Add(st); else list.Insert(fi, st);
            app._structures = Gc.Array(list);
        }

        /// <summary>Removes every structure matching the predicate. Returns how many were removed.</summary>
        public static int RemoveStructures(SlimeAppearance app, Func<SlimeAppearanceStructure, bool> match)
        {
            var list = app._structures.ToManagedList();
            int n = list.RemoveAll(s => s != null && match(s));
            if (n > 0) app._structures = Gc.Array(list);
            return n;
        }

        /// <summary>Pink's body object (LOD0 skinned mesh) - the usual template for <see cref="SkinnedObject"/>.</summary>
        public static SlimeAppearanceObject BodyTemplate(string slime = "Pink") =>
            CottonLibrary.Library.GetSlime(slime).AppearancesDefault[0].Structures[0].Element.Prefabs[0];

        /// <summary>
        /// Copy of a skinned slime object with another mesh, bound to the slime skeleton. <paramref name="attached"/> are the bones
        /// that deform the mesh, in the mesh's bone order (SR1 and SR2 use the same <c>SlimeBone</c> numbering and rest pose,
        /// so SR1 meshes can be re-skinned as they are).
        /// </summary>
        public static SlimeAppearanceObject SkinnedObject(SlimeAppearanceObject template, string name, Mesh mesh, Bone root, Bone[] attached, bool shadows = true)
        {
            var go = Object.Instantiate(template.gameObject, PrefabRoot);
            go.name = name;
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            var sao = go.GetComponent<SlimeAppearanceObject>();
            sao.RootBone = root;
            sao.ParentBone = Bone.NONE;
            if (attached != null) sao.AttachedBones = new Il2CppStructArray<Bone>(attached);
            sao.IgnoreLODIndex = true;
            sao.LODIndex = 0;
            return sao;
        }

        /// <summary>New structure copied from <paramref name="template"/> with its own element (name, type, objects, material).</summary>
        public static SlimeAppearanceStructure NewStructure(SlimeAppearanceStructure template, string elementName, EType type, Material mat, bool supportsFaces, params SlimeAppearanceObject[] objects)
        {
            var st = new SlimeAppearanceStructure(template);
            var el = ScriptableObject.CreateInstance<SlimeAppearanceElement>();
            el.name = elementName;
            el.Name = elementName;
            el.Type = type;
            el.CastsShadows = true;
            el.MaterialInheritance = SlimeAppearanceElement.MatInheritType.NONE;
            el.hideFlags = HideFlags.DontUnloadUnusedAsset;
            el.Prefabs = Gc.Array(objects.ToList());
            st.Element = el;
            st.SupportsFaces = supportsFaces;
            if (mat != null) st.DefaultMaterials = Gc.Array(new List<Material> { mat });
            Safe.Run("structure element materials", () =>
            {
                var em = template.ElementMaterials;
                if (em == null || em.Length == 0) return;
                var arr = new List<SlimeAppearanceMaterials>();
                for (int i = 0; i < objects.Length; i++) arr.Add(em[Math.Min(i, em.Length - 1)]);
                st.ElementMaterials = Gc.Array(arr);
            });
            return st;
        }

        /// <summary>Removes the radiant variants inherited from Pink (they would be radiant pink slimes).</summary>
        public static void ClearRadiant(SlimeDefinition d)
        {
            if (d == null) return;
            Safe.Quiet(() => { d.RadiantBase = null; d.RadiantLargo0 = null; d.RadiantLargo1 = null; });
        }

        /// <summary>
        /// Replaces every material of <paramref name="app"/> that is shared with <paramref name="template"/> by a copy. Cotton's
        /// <c>CreateSlimeDef</c> copies only the first material of all structures but the last one, so recolouring the rest
        /// changed the template (Pink) too. Returns the number of materials cloned.
        /// </summary>
        public static int UnshareMaterials(SlimeAppearance app, SlimeAppearance template, string prefix)
        {
            if (app == null || template == null) return 0;
            var shared = new HashSet<IntPtr>();
            foreach (var st in template.Structures)
                if (st?.DefaultMaterials != null)
                    foreach (var m in st.DefaultMaterials) if (m != null) shared.Add(m.Pointer);
            var pink = CottonLibrary.Library.GetSlime("Pink");
            if (pink != null && pink.AppearancesDefault != null && pink.AppearancesDefault.Length > 0)
                foreach (var st in pink.AppearancesDefault[0].Structures)
                    if (st?.DefaultMaterials != null)
                        foreach (var m in st.DefaultMaterials) if (m != null) shared.Add(m.Pointer);
            int n = 0;
            var arr = app.Structures;
            for (int i = 0; i < arr.Length; i++)
            {
                var st = arr[i];
                if (st?.DefaultMaterials == null || st.DefaultMaterials.Length == 0) continue;
                bool any = false;
                var mats = new List<Material>();
                foreach (var m in st.DefaultMaterials)
                {
                    if (m != null && shared.Contains(m.Pointer))
                    {
                        var c = Object.Instantiate(m);
                        c.name = prefix + "_" + m.name.Replace("(Clone)", "");
                        c.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        mats.Add(c); any = true; n++;
                    }
                    else mats.Add(m);
                }
                if (any) st.DefaultMaterials = Gc.Array(mats);
            }
            return n;
        }
    }
}
