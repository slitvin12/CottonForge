using System;
using UnityEngine;

namespace CottonLibrary.Forge
{
    public static unsafe partial class Faces
    {
        /// <summary>
        /// Material remap for <see cref="Clone"/>: the same face on SR2's glowing face shader (the one of the invoke/radiant
        /// faces, <c>eyeInvoke</c> / <c>mouthInvoke</c>), keeping the face atlas. Use it for eyes (<paramref name="eyes"/> true)
        /// or mouths. Falls back to a plain copy when the template is not loaded.
        /// </summary>
        public static Func<Material, Material> Glow(bool eyes, string suffix = "_Glow") => src =>
        {
            var m = Mat.Copy(src, src.name + suffix);
            var tpl = Mat.Find(eyes ? "eyeInvoke" : "mouthInvoke");
            if (tpl == null) { ForgeLog.WarnOnce("faces:glow", "Faces.Glow: eyeInvoke/mouthInvoke not loaded, face stays unlit"); return m; }
            if (m.shader != tpl.shader)
            {
                var atlas = src.HasProperty("_FaceAtlas") ? src.GetTexture("_FaceAtlas") : null;
                m.shader = tpl.shader;
                if (atlas != null) m.SetTexture("_FaceAtlas", atlas);
            }
            if (tpl.HasProperty("_GlowNoise")) m.Tx("_GlowNoise", tpl.GetTexture("_GlowNoise"));
            if (tpl.HasProperty("_GlowNoiseSpeed")) m.Flt("_GlowNoiseSpeed", tpl.GetFloat("_GlowNoiseSpeed"));
            // a shader swap resets the queue to 2000; stock faces draw at ~2450, after the body
            m.renderQueue = tpl.renderQueue;
            return m;
        };

        /// <summary>
        /// Material remap that draws nothing (SR2's own "Null Render" shader; fallback: an empty face atlas), e.g. a slime
        /// without a mouth: <c>Faces.Clone(src, "MyFace", null, Faces.Hidden())</c>.
        /// </summary>
        public static Func<Material, Material> Hidden(string suffix = "_None") => src =>
        {
            var m = Mat.Copy(src, src.name + suffix);
            var nul = Mat.Find("Null Render");
            if (nul != null && nul.shader != null) m.shader = nul.shader;
            else m.Tx("_FaceAtlas", Gfx.Generated("emptyFace", 4, 4, (x, y) => new Color(0f, 0f, 0f, 0f), true));
            return m;
        };
    }
}
