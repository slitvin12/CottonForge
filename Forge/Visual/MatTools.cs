using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace CottonLibrary.Forge
{
    public static partial class Mat
    {
        /// <summary>
        /// All loaded materials named <paramref name="name"/>, plus their runtime instances ("name (Instance)", "name(Clone)").
        /// A trailing '*' matches by prefix. For live tuning (dev console <c>flt/col/vec/kw/tex</c>) and for changing a look on
        /// slimes that are already spawned.
        /// </summary>
        public static List<Material> Live(string name)
        {
            var r = new List<Material>();
            if (string.IsNullOrEmpty(name)) return r;
            bool prefix = name.Length > 1 && name.EndsWith("*");
            if (prefix) name = name.Substring(0, name.Length - 1);
            foreach (var x in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (x == null) continue;
                var n = x.name;
                if (n == name || (prefix && n.StartsWith(name, StringComparison.Ordinal))
                    || n.StartsWith(name + " (Instance)", StringComparison.Ordinal) || n.StartsWith(name + "(Clone)", StringComparison.Ordinal))
                    r.Add(x);
            }
            return r;
        }

        /// <summary>One line: name, shader, support, passes, queue, keywords.</summary>
        public static string Describe(Material m)
        {
            if (m == null) return "<null material>";
            var sh = m.shader;
            if (sh == null) return $"{m.name} shader=<null>";
            return $"{m.name} shader={sh.name} ok={sh.isSupported} passes={sh.passCount} rq={m.renderQueue} kw=[{string.Join(" ", (IEnumerable<string>)m.shaderKeywords)}]";
        }

        /// <summary>Every shader property with its current value (one per line), for diffing looks.</summary>
        public static string Dump(Material m)
        {
            if (m == null) return "<null material>";
            var sb = new StringBuilder(Describe(m)).AppendLine();
            var sh = m.shader;
            if (sh == null) return sb.ToString();
            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string pn = sh.GetPropertyName(i);
                string v;
                try
                {
                    switch (sh.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color: { var c = m.GetColor(pn); v = F($"({c.r:0.###}, {c.g:0.###}, {c.b:0.###}, {c.a:0.###})"); break; }
                        case ShaderPropertyType.Vector: { var x = m.GetVector(pn); v = F($"({x.x:0.###}, {x.y:0.###}, {x.z:0.###}, {x.w:0.###})"); break; }
                        case ShaderPropertyType.Texture: { var t = m.GetTexture(pn); v = t != null ? $"{t.name} {t.width}x{t.height}" : "null"; break; }
                        case ShaderPropertyType.Int: v = m.GetInt(pn).ToString(CultureInfo.InvariantCulture); break;
                        default: v = m.GetFloat(pn).ToString("0.###", CultureInfo.InvariantCulture); break;
                    }
                }
                catch (Exception e) { v = "? " + e.Message; }
                sb.Append(pn).Append(" = ").AppendLine(v);
            }
            return sb.ToString();
        }

        static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Copies SR2's subsurface diffusion profile (the skin tint of light passing through the body) from the first found
        /// source material. Returns false when none has one.
        /// </summary>
        public static bool CopyDiffusionProfile(Material dst, params string[] sources)
        {
            if (dst == null || !dst.HasProperty("_DiffusionProfile")) return false;
            foreach (var n in sources)
            {
                var src = Find(n);
                if (src == null || !src.HasProperty("_DiffusionProfile")) continue;
                dst.SetFloat("_DiffusionProfile", src.GetFloat("_DiffusionProfile"));
                if (src.HasProperty("_DiffusionProfile_Asset") && dst.HasProperty("_DiffusionProfile_Asset")) dst.SetVector("_DiffusionProfile_Asset", src.GetVector("_DiffusionProfile_Asset"));
                return true;
            }
            return false;
        }

        /// <summary>
        /// Neutral (colourless) light scattering of the rock / crystal / phosphor slimes. The pink and gold bodies scatter
        /// through a warm skin-like profile that tints metallic or dark looks pink/maroon.
        /// </summary>
        public static bool NeutralScatter(Material dst) => CopyDiffusionProfile(dst, "slimeRockBase", "slimeCrystalBase", "slimePhosphorBase");
    }
}
