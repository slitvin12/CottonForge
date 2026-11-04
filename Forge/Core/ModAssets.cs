using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CottonLibrary.Forge
{
    /// <summary>
    /// Assets embedded in a mod DLL (<c>&lt;EmbeddedResource Include="Assets\**\*" /&gt;</c>). Files are found by name, whatever
    /// the resource prefix: <c>Sprite("iconSlimeRad")</c> finds <c>MyMod.Assets.iconSlimeRad.png</c>. Every created object is
    /// cached, marked <c>HideAndDontSave</c> and <c>DontDestroyOnLoad</c> (survives <c>Resources.UnloadUnusedAssets</c>).
    /// <para>Get the instance with <see cref="Of{T}"/> or <see cref="ForgeMod{M}.Assets"/>.</para>
    /// </summary>
    public sealed class ModAssets
    {
        static readonly Dictionary<Assembly, ModAssets> _all = new();

        /// <summary>Assets of the assembly that contains <typeparamref name="T"/>.</summary>
        public static ModAssets Of<T>() => Of(typeof(T).Assembly);

        public static ModAssets Of(Assembly asm)
        {
            if (!_all.TryGetValue(asm, out var a)) _all[asm] = a = new ModAssets(asm);
            return a;
        }

        public Assembly Assembly { get; }

        /// <summary>Prefix for names of created objects (assembly name).</summary>
        public string Prefix { get; }

        readonly string[] _names;
        readonly Dictionary<string, string> _resolved = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Texture2D> _tex = new();
        readonly Dictionary<string, Sprite> _sprites = new();
        readonly Dictionary<string, AudioClip> _clips = new();
        readonly Dictionary<string, Mesh> _meshes = new();
        readonly Dictionary<string, AssetBundle> _bundles = new();

        ModAssets(Assembly asm)
        {
            Assembly = asm;
            Prefix = asm.GetName().Name;
            _names = asm.GetManifestResourceNames();
        }

        /// <summary>All embedded resource names.</summary>
        public IReadOnlyList<string> Names => _names;

        /// <summary>Full manifest name for a file name (exact, then "*.file", case-insensitive), or null.</summary>
        public string Resolve(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (_resolved.TryGetValue(file, out var r)) return r;
            r = _names.FirstOrDefault(n => n == file)
                ?? _names.FirstOrDefault(n => n == Prefix + "." + file)
                ?? _names.FirstOrDefault(n => n.EndsWith("." + file, StringComparison.OrdinalIgnoreCase))
                ?? _names.FirstOrDefault(n => string.Equals(n, file, StringComparison.OrdinalIgnoreCase));
            _resolved[file] = r;
            return r;
        }

        static string Ext(string name, string ext) => Path.HasExtension(name) && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext;

        public bool Has(string file) => Resolve(file) != null;

        /// <summary>Raw bytes of an embedded file, or null.</summary>
        public byte[] Bytes(string file)
        {
            var n = Resolve(file);
            if (n == null) return null;
            using var s = Assembly.GetManifestResourceStream(n);
            if (s == null) return null;
            var buf = new byte[s.Length];
            int read = 0;
            while (read < buf.Length) { int k = s.Read(buf, read, buf.Length - read); if (k <= 0) break; read += k; }
            return buf;
        }

        /// <summary>UTF-8 text of an embedded file (BOM stripped), or null.</summary>
        public string Text(string file)
        {
            var b = Bytes(file);
            if (b == null) return null;
            var s = Encoding.UTF8.GetString(b);
            return s.Length > 0 && s[0] == '\uFEFF' ? s.Substring(1) : s;
        }

        static T Keep<T>(T o) where T : Object
        {
            o.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(o);
            return o;
        }

        /// <summary>PNG texture. sRGB colour by default; <paramref name="linear"/> for masks and normal maps.</summary>
        public Texture2D Texture(string name, bool linear = false, bool repeat = true)
        {
            string key = name + (linear ? "|lin" : "") + (repeat ? "|rep" : "");
            if (_tex.TryGetValue(key, out var t) && t != null) return t;
            var bytes = Bytes(Ext(name, ".png"));
            if (bytes == null) { ForgeLog.WarnOnce("tex:" + Prefix + name, $"[{Prefix}] missing texture {name}.png"); return null; }
            t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            Il2CppImageConversionManager.LoadImage(t, bytes); // ImageConversion.LoadImage is stripped in SR2
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.name = Prefix + "_" + Path.GetFileNameWithoutExtension(name);
            _tex[key] = Keep(t);
            return t;
        }

        /// <summary>Sprite from an embedded PNG (icons, pedia art).</summary>
        public Sprite Sprite(string name)
        {
            if (_sprites.TryGetValue(name, out var sp) && sp != null) return sp;
            var bytes = Bytes(Ext(name, ".png"));
            if (bytes == null) { ForgeLog.WarnOnce("spr:" + Prefix + name, $"[{Prefix}] missing sprite {name}.png"); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Il2CppImageConversionManager.LoadImage(tex, bytes);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = Prefix + "_" + Path.GetFileNameWithoutExtension(name);
            Keep(tex);
            sp = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sp.name = tex.name;
            _sprites[name] = Keep(sp);
            return sp;
        }

        /// <summary>AudioClip from an embedded 16-bit PCM WAV.</summary>
        public AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var c) && c != null) return c;
            var bytes = Bytes(Ext(name, ".wav"));
            if (bytes == null) { ForgeLog.WarnOnce("wav:" + Prefix + name, $"[{Prefix}] missing clip {name}.wav"); return null; }
            try
            {
                int channels = 1, rate = 44100, bits = 16, dataOff = -1, dataLen = 0, p = 12;
                while (p + 8 <= bytes.Length)
                {
                    string id = Encoding.ASCII.GetString(bytes, p, 4);
                    int len = BitConverter.ToInt32(bytes, p + 4);
                    if (id == "fmt ")
                    {
                        channels = BitConverter.ToInt16(bytes, p + 10);
                        rate = BitConverter.ToInt32(bytes, p + 12);
                        bits = BitConverter.ToInt16(bytes, p + 22);
                    }
                    else if (id == "data") { dataOff = p + 8; dataLen = Math.Min(len, bytes.Length - dataOff); break; }
                    p += 8 + len + (len & 1);
                }
                if (dataOff < 0 || bits != 16) throw new Exception($"unsupported wav (bits={bits}), use 16-bit PCM");
                int samples = dataLen / 2;
                var data = new float[samples];
                for (int i = 0; i < samples; i++) data[i] = BitConverter.ToInt16(bytes, dataOff + i * 2) / 32768f;
                c = AudioClip.Create(Prefix + "_" + Path.GetFileNameWithoutExtension(name), samples / channels, channels, rate, false);
                c.SetData(new Il2CppStructArray<float>(data), 0);
                _clips[name] = Keep(c);
                return c;
            }
            catch (Exception e) { ForgeLog.Warn($"[{Prefix}] clip {name}: {e.Message}"); return null; }
        }

        /// <summary>
        /// Mesh in the CSM1 format (ClassicSlimes <c>tools/export_assets.py</c>): "CSM1", nv, ni, flags (1 normals, 2 uv,
        /// 4 bone weights), vertices, [normals], [uv], [weights], triangles, bindposes.
        /// </summary>
        public Mesh Mesh(string name)
        {
            if (_meshes.TryGetValue(name, out var m) && m != null) return m;
            var bytes = Bytes(Ext(name, ".csm"));
            if (bytes == null) { ForgeLog.WarnOnce("csm:" + Prefix + name, $"[{Prefix}] missing mesh {name}.csm"); return null; }
            using var br = new BinaryReader(new MemoryStream(bytes));
            if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "CSM1") throw new Exception("bad csm " + name);
            int nv = br.ReadInt32(), ni = br.ReadInt32(), flags = br.ReadInt32();
            var v = new Vector3[nv];
            for (int i = 0; i < nv; i++) v[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Vector3[] nrm = null; Vector2[] uv = null; BoneWeight[] bw = null;
            if ((flags & 1) != 0) { nrm = new Vector3[nv]; for (int i = 0; i < nv; i++) nrm[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()); }
            if ((flags & 2) != 0) { uv = new Vector2[nv]; for (int i = 0; i < nv; i++) uv[i] = new Vector2(br.ReadSingle(), br.ReadSingle()); }
            if ((flags & 4) != 0)
            {
                bw = new BoneWeight[nv];
                for (int i = 0; i < nv; i++)
                {
                    var w = new BoneWeight();
                    w.boneIndex0 = br.ReadInt32(); w.boneIndex1 = br.ReadInt32(); w.boneIndex2 = br.ReadInt32(); w.boneIndex3 = br.ReadInt32();
                    w.weight0 = br.ReadSingle(); w.weight1 = br.ReadSingle(); w.weight2 = br.ReadSingle(); w.weight3 = br.ReadSingle();
                    bw[i] = w;
                }
            }
            var tris = new int[ni];
            for (int i = 0; i < ni; i++) tris[i] = br.ReadInt32();
            int nbp = br.ReadInt32();
            var bp = new Matrix4x4[nbp];
            for (int b = 0; b < nbp; b++)
            {
                var mx = new Matrix4x4();
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) mx[r, c] = br.ReadSingle();
                bp[b] = mx;
            }
            m = new Mesh { name = Prefix + "_" + name };
            m.vertices = v;
            if (nrm != null) m.normals = nrm;
            if (uv != null) m.uv = uv;
            m.triangles = tris;
            if (bw != null) m.boneWeights = bw;
            if (nbp > 0) m.bindposes = bp;
            if (nrm == null) m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            _meshes[name] = Keep(m);
            return m;
        }

        /// <summary>AssetBundle from an embedded file (loaded once).</summary>
        public AssetBundle Bundle(string file)
        {
            if (_bundles.TryGetValue(file, out var b) && b != null) return b;
            var bytes = Bytes(file);
            if (bytes == null) { ForgeLog.Warn($"[{Prefix}] missing bundle {file}"); return null; }
            b = AssetBundle.LoadFromMemory(new Il2CppStructArray<byte>(bytes));
            _bundles[file] = b;
            return b;
        }
    }
}
