using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CottonLibrary.Forge
{
    /// <summary>Look of Forge HUD widgets (font copied from SR2's HUD when available).</summary>
    public sealed class HudStyle
    {
        public TMP_FontAsset Font;
        public float FontSize = 24f;
        public Color Text = new(1f, 1f, 1f, 0.95f);
        public Color Back = new(0.05f, 0.08f, 0.12f, 0.55f);
        public Color Accent = new(0.35f, 0.85f, 1f, 1f);
        /// <summary>Distance from the screen edge (reference 1920×1080).</summary>
        public Vector2 Margin = new(32f, 120f);
        public float Spacing = 6f;
    }

    /// <summary>
    /// Modular HUD: an overlay canvas (scaled for 1920×1080) with eight anchors; widgets (<see cref="IHudWidget"/>,
    /// built-ins <see cref="HudBar"/>, <see cref="HudCounter"/>, <see cref="HudText"/>) stack at their anchor and are shown
    /// only while SR2's own HUD is visible. <see cref="Toast"/> shows short messages. Runs as module <c>hud</c>.
    /// <code>ForgeHud.Add(new HudBar("rad", HudAnchor.BottomLeft, "Radiation", () => World.PlayerStats().rad / 100f, Color.green));</code>
    /// </summary>
    public static class ForgeHud
    {
        sealed class Slot { public IHudWidget W; public RectTransform Root; public int Errors; }

        static readonly List<Slot> _slots = new();
        static readonly List<(string text, float until)> _toasts = new();
        static GameObject _canvas;
        static RectTransform _rootRt;
        static HudUI _hud;
        static float _findAt;
        static bool _wasVisible;

        public static HudStyle Style = new();
        /// <summary>Master switch (false hides every Forge widget).</summary>
        public static bool Enabled = true;
        /// <summary>
        /// Keep widgets clear of SR2's own HUD: every 2 s the visible game HUD graphics near the top / bottom edges are measured
        /// and the corner / center stacks start beyond them (see <see cref="Reserved"/>). Middle anchors are never shifted.
        /// </summary>
        public static bool AvoidGameHud = true;
        static readonly float[] _reserved = new float[8];
        static float _measureAt;
        static Il2CppStructArray<Vector3> _corners;
        public static IEnumerable<IHudWidget> Widgets { get { foreach (var s in _slots) yield return s.W; } }

        /// <summary>Adds a widget (replaces one with the same id).</summary>
        public static T Add<T>(T widget) where T : class, IHudWidget
        {
            if (widget == null) return null;
            Remove(widget.Id);
            var s = new Slot { W = widget };
            _slots.Add(s);
            if (_canvas != null) Build(s);
            return widget;
        }

        public static bool Remove(string id)
        {
            int i = _slots.FindIndex(s => string.Equals(s.W.Id, id, StringComparison.Ordinal));
            if (i < 0) return false;
            if (_slots[i].Root != null) Object.Destroy(_slots[i].Root.gameObject);
            _slots.RemoveAt(i);
            return true;
        }

        public static IHudWidget Get(string id) => _slots.Find(s => string.Equals(s.W.Id, id, StringComparison.Ordinal))?.W;

        /// <summary>Short message at the top of the screen (several stack).</summary>
        public static void Toast(string text, float seconds = 2.5f)
        {
            if (string.IsNullOrEmpty(text)) return;
            _toasts.RemoveAll(t => t.text == text);
            _toasts.Add((text, Time.unscaledTime + seconds));
            if (_toasts.Count > 4) _toasts.RemoveAt(0);
            if (Get("forge.toast") == null) Add(new HudText("forge.toast", HudAnchor.TopCenter, ToastText, 900f, TextAlignmentOptions.Center) { Interval = 0f });
        }

        static string ToastText()
        {
            _toasts.RemoveAll(t => Time.unscaledTime > t.until);
            if (_toasts.Count == 0) return null;
            var parts = new string[_toasts.Count];
            for (int i = 0; i < _toasts.Count; i++) parts[i] = _toasts[i].text;
            return string.Join("\n", parts);
        }

        /// <summary>True while SR2's HUD is on screen (in game, not paused, HUD not hidden).</summary>
        public static bool GameHudVisible
        {
            get
            {
                if (!World.InGame || Time.timeScale <= 0f) return false;
                if (_hud == null && Time.unscaledTime >= _findAt) { _findAt = Time.unscaledTime + 2f; _hud = Object.FindObjectOfType<HudUI>(); }
                if (_hud == null) return false;
                try
                {
                    if (!_hud.isActiveAndEnabled) return false;
                    var cg = _hud.GetComponentInParent<CanvasGroup>();
                    if (cg != null && cg.alpha < 0.01f) return false;
                    var cv = _hud.GetComponentInParent<Canvas>();
                    return cv == null || cv.enabled;
                }
                catch { return false; }
            }
        }

        // ------------------------------------------------------------------ UI helpers for widgets

        /// <summary>New child RectTransform.</summary>
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Stretches <paramref name="rt"/> over its parent with insets.</summary>
        public static RectTransform Fill(RectTransform rt, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Solid panel.</summary>
        public static Image Panel(Transform parent, string name, Color color)
        {
            var rt = Fill(Rect(parent, name));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Text label with the HUD font.</summary>
        public static TextMeshProUGUI Label(Transform parent, string name, HudStyle style, float size = 0f, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var rt = Fill(Rect(parent, name), 10f, 10f, 2f, 2f);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (style.Font != null) t.font = style.Font;
            t.fontSize = size > 0f ? size : style.FontSize;
            t.color = style.Text;
            t.alignment = align;
            t.raycastTarget = false;
            try { t.textWrappingMode = TextWrappingModes.NoWrap; } catch { }
            t.text = "";
            return t;
        }

        // ------------------------------------------------------------------ driver

        static void EnsureCanvas()
        {
            if (_canvas != null) return;
            _canvas = new GameObject("ForgeHud");
            Object.DontDestroyOnLoad(_canvas);
            var c = _canvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 40;
            var sc = _canvas.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f);
            sc.matchWidthOrHeight = 0.5f;
            _rootRt = _canvas.GetComponent<RectTransform>();
            if (Style.Font == null && _hud != null)
                foreach (var t in _hud.GetComponentsInChildren<TMP_Text>(true))
                    if (t != null && t.font != null) { Style.Font = t.font; break; }
            foreach (var s in _slots) Build(s);
        }

        static void Build(Slot s)
        {
            if (s.Root != null) Object.Destroy(s.Root.gameObject);
            var rt = Rect(_rootRt, "W_" + s.W.Id);
            rt.sizeDelta = new Vector2(360f, 36f);
            Place(rt, s.W.Anchor, 0f);
            s.Root = rt;
            try { s.W.Build(rt, Style); }
            catch (Exception e) { s.Errors++; ForgeLog.WarnOnce("hud.build:" + s.W.Id, $"HUD widget {s.W.Id} build failed: {e}"); }
        }

        static void Place(RectTransform rt, HudAnchor a, float offset)
        {
            float x = a switch { HudAnchor.TopLeft or HudAnchor.MiddleLeft or HudAnchor.BottomLeft => 0f, HudAnchor.TopCenter or HudAnchor.BottomCenter => 0.5f, _ => 1f };
            float y = a switch { HudAnchor.TopLeft or HudAnchor.TopCenter or HudAnchor.TopRight => 1f, HudAnchor.MiddleLeft or HudAnchor.MiddleRight => 0.5f, _ => 0f };
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(x, y);
            float mx = x == 0f ? Style.Margin.x : x == 1f ? -Style.Margin.x : 0f;
            float res = _reserved[(int)a] > 0f ? _reserved[(int)a] + Style.Spacing * 2f : 0f;
            float my = y == 1f ? -Mathf.Max(Style.Margin.y * 0.5f, res) - offset : y == 0f ? Mathf.Max(Style.Margin.y, res) + offset : -offset;
            rt.anchoredPosition = new Vector2(mx, my);
        }

        /// <summary>Distance from the screen edge (canvas units, 1080p reference) taken by SR2's HUD at an anchor; 0 = free.</summary>
        public static float Reserved(HudAnchor a) => _reserved[(int)a];

        /// <summary>"BottomLeft 212, BottomRight 180" (diagnostics; anchors with nothing reserved are omitted).</summary>
        public static string DescribeReserved()
        {
            if (!AvoidGameHud) return "avoidance off";
            var parts = new List<string>();
            for (int i = 0; i < _reserved.Length; i++) if (_reserved[i] > 0f) parts.Add($"{(HudAnchor)i} {_reserved[i]:0}");
            return parts.Count > 0 ? string.Join(", ", parts) : "nothing measured";
        }

        /// <summary>Measures visible game HUD graphics near the top / bottom edges into <see cref="_reserved"/>.</summary>
        static void MeasureGameHud()
        {
            Array.Clear(_reserved, 0, _reserved.Length);
            if (!AvoidGameHud || _hud == null || _canvas == null) return;
            var own = _canvas.GetComponent<Canvas>();
            float k = own != null && own.scaleFactor > 0.01f ? own.scaleFactor : 1f;
            float W = Screen.width / k, H = Screen.height / k, col = 420f, half = 450f;
            var hudCanvas = _hud.GetComponentInParent<Canvas>();
            var scanRoot = hudCanvas != null && hudCanvas.rootCanvas != null ? hudCanvas.rootCanvas.transform : _hud.transform;
            _corners ??= new Il2CppStructArray<Vector3>(4);
            foreach (var g in scanRoot.GetComponentsInChildren<Graphic>(false))
            {
                if (g == null || !g.isActiveAndEnabled || g.color.a < 0.05f) continue;
                try { var cr = g.canvasRenderer; if (cr != null && (cr.cull || cr.GetInheritedAlpha() < 0.05f)) continue; } catch { }
                var tmp = g.TryCast<TMP_Text>();
                if (tmp != null && string.IsNullOrWhiteSpace(tmp.text)) continue;
                var cv = g.canvas != null ? g.canvas.rootCanvas : null;
                if (cv == null || cv.renderMode == RenderMode.WorldSpace) continue;
                var cam = cv.renderMode == RenderMode.ScreenSpaceCamera ? cv.worldCamera : null;
                g.rectTransform.GetWorldCorners(_corners);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 sp = cam != null ? RectTransformUtility.WorldToScreenPoint(cam, _corners[i]) : (Vector2)_corners[i];
                    x0 = Mathf.Min(x0, sp.x / k); x1 = Mathf.Max(x1, sp.x / k);
                    y0 = Mathf.Min(y0, sp.y / k); y1 = Mathf.Max(y1, sp.y / k);
                }
                float w = x1 - x0, h = y1 - y0;
                if (w < 2f || h < 2f || w > W * 0.45f || h > H * 0.4f) continue;   // specks, backgrounds, full-screen overlays
                bool bottom = y1 <= H * 0.4f, top = y0 >= H * 0.6f;
                if (!bottom && !top) continue;
                bool left = x0 < Style.Margin.x + col && x1 > Style.Margin.x;
                bool right = x1 > W - Style.Margin.x - col && x0 < W - Style.Margin.x;
                bool center = x1 > W * 0.5f - half && x0 < W * 0.5f + half;
                float need = bottom ? y1 : H - y0;
                if (left) Reserve(bottom ? HudAnchor.BottomLeft : HudAnchor.TopLeft, need);
                if (right) Reserve(bottom ? HudAnchor.BottomRight : HudAnchor.TopRight, need);
                if (center) Reserve(bottom ? HudAnchor.BottomCenter : HudAnchor.TopCenter, need);
            }
        }

        static void Reserve(HudAnchor a, float v) { if (v > _reserved[(int)a]) _reserved[(int)a] = v; }

        static readonly float[] _stack = new float[8];

        internal static void Tick(float dt)
        {
            bool show = Enabled && _slots.Count > 0 && GameHudVisible;
            if (show) EnsureCanvas();
            if (_canvas == null) return;
            if (show != _wasVisible) { _canvas.SetActive(show); _wasVisible = show; }
            if (!show) return;
            if (Time.unscaledTime >= _measureAt)
            {
                _measureAt = Time.unscaledTime + 2f;
                try { MeasureGameHud(); }
                catch (Exception e) { Array.Clear(_reserved, 0, _reserved.Length); ForgeLog.WarnOnce("hud.measure", "game HUD measuring failed: " + e.Message); }
            }
            Array.Clear(_stack, 0, _stack.Length);
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s.Root == null) Build(s);
                if (s.Root == null) continue;
                bool vis = false;
                try { s.W.Refresh(dt); vis = s.W.Visible; }
                catch (Exception e)
                {
                    ForgeLog.WarnOnce("hud.tick:" + s.W.Id, $"HUD widget {s.W.Id} failed: {e}");
                    if (++s.Errors >= 10) { ForgeLog.Warn($"HUD widget {s.W.Id} removed after 10 errors"); Remove(s.W.Id); i--; continue; }
                }
                if (s.Root.gameObject.activeSelf != vis) s.Root.gameObject.SetActive(vis);
                if (!vis) continue;
                int a = (int)s.W.Anchor;
                Place(s.Root, s.W.Anchor, _stack[a]);
                _stack[a] += s.Root.sizeDelta.y + Style.Spacing;
            }
        }

        /// <summary>Drops the canvas (rebuilt on the next visible frame, e.g. after changing <see cref="Style"/>).</summary>
        public static void Rebuild()
        {
            if (_canvas != null) Object.Destroy(_canvas);
            _canvas = null; _wasVisible = false;
            foreach (var s in _slots) s.Root = null;
        }

        internal sealed class Module : IForgeModule
        {
            public string Id => "hud";
            public void Init() { }
            public void Tick(float dt) { if (_slots.Count > 0 || _canvas != null) ForgeHud.Tick(dt); }
        }
    }

    /// <summary>Base of the built-in widgets.</summary>
    public abstract class HudWidget : IHudWidget
    {
        protected HudWidget(string id, HudAnchor anchor) { Id = id; Anchor = anchor; }
        public string Id { get; }
        public HudAnchor Anchor { get; }
        public virtual bool Visible { get; protected set; } = true;
        protected RectTransform Root { get; private set; }
        protected HudStyle Style { get; private set; }
        /// <summary>Seconds between value updates (0 = every frame).</summary>
        public float Interval = 0.2f;
        float _acc; bool _fresh;
        public void Build(RectTransform parent, HudStyle style) { Root = parent; Style = style; _fresh = true; OnBuild(); }
        /// <summary>True when the widget should re-read its value now (first frame after a build, then every <see cref="Interval"/>).</summary>
        protected bool Due(float dt)
        {
            _acc += dt;
            if (!_fresh && _acc < Interval) return false;
            _acc = 0f; _fresh = false;
            return true;
        }
        protected abstract void OnBuild();
        public abstract void Refresh(float dt);
    }

    /// <summary>Plain text line(s); hidden while the text function returns null.</summary>
    public class HudText : HudWidget
    {
        readonly Func<string> _text; readonly float _width; readonly TextAlignmentOptions _align;
        TextMeshProUGUI _label; string _last;

        public HudText(string id, HudAnchor anchor, Func<string> text, float width = 460f, TextAlignmentOptions align = TextAlignmentOptions.Left) : base(id, anchor)
        { _text = text; _width = width; _align = align; }

        protected override void OnBuild()
        {
            _label = ForgeHud.Label(Root, "Text", Style, 0f, _align);
            _last = null;
        }

        public override void Refresh(float dt)
        {
            if (!Due(dt)) return;
            var t = _text?.Invoke();
            Visible = t != null;
            if (t == null || t == _last || _label == null) return;
            _last = t;
            _label.text = t;
            int lines = 1; foreach (var ch in t) if (ch == '\n') lines++;
            Root.sizeDelta = new Vector2(_width, Style.FontSize * 1.25f * lines + 8f);
        }
    }

    /// <summary>Panel with an accent stripe and a value text (ammo counters, currencies...).</summary>
    public class HudCounter : HudWidget
    {
        readonly Func<string> _text; readonly float _width;
        TextMeshProUGUI _label; string _last;
        public Color? Accent;

        public HudCounter(string id, HudAnchor anchor, Func<string> text, float width = 380f) : base(id, anchor) { _text = text; _width = width; }

        protected override void OnBuild()
        {
            Root.sizeDelta = new Vector2(_width, Style.FontSize + 16f);
            ForgeHud.Panel(Root, "Back", Style.Back);
            var stripe = ForgeHud.Panel(Root, "Accent", Accent ?? Style.Accent);
            var srt = stripe.rectTransform; srt.anchorMax = new Vector2(0f, 1f); srt.offsetMax = new Vector2(5f, 0f);
            _label = ForgeHud.Label(Root, "Text", Style, 0f, TextAlignmentOptions.Left);
            _last = null;
        }

        public override void Refresh(float dt)
        {
            if (!Due(dt)) return;
            var t = _text?.Invoke();
            Visible = t != null;
            if (t == null || t == _last || _label == null) return;
            _last = t;
            _label.text = t;
        }
    }

    /// <summary>Labelled progress bar (0..1); hidden while <c>visible</c> returns false.</summary>
    public class HudBar : HudWidget
    {
        readonly string _caption; readonly Func<float> _value; readonly Func<bool> _visible; readonly Color _color; readonly float _width;
        RectTransform _fill; TextMeshProUGUI _label; float _shown = -1f;

        public HudBar(string id, HudAnchor anchor, string caption, Func<float> value, Color? color = null, Func<bool> visible = null, float width = 360f) : base(id, anchor)
        { _caption = caption; _value = value; _color = color ?? new Color(0.35f, 0.85f, 1f, 0.9f); _visible = visible; _width = width; }

        protected override void OnBuild()
        {
            Root.sizeDelta = new Vector2(_width, Style.FontSize + 14f);
            ForgeHud.Panel(Root, "Back", Style.Back);
            var fill = ForgeHud.Panel(Root, "Fill", _color);
            _fill = fill.rectTransform;
            ForgeHud.Fill(_fill, 3f, 3f, 3f, 3f);
            _label = ForgeHud.Label(Root, "Text", Style, Style.FontSize * 0.8f, TextAlignmentOptions.Left);
            _shown = -1f;
        }

        public override void Refresh(float dt)
        {
            if (!Due(dt)) return;
            Visible = _visible == null || _visible();
            if (!Visible || _fill == null) return;
            float v = Mathf.Clamp01(_value != null ? _value() : 0f);
            if (Mathf.Abs(v - _shown) < 0.002f) return;
            _shown = v;
            _fill.anchorMax = new Vector2(v, 1f);
            if (_label != null) _label.text = $"{_caption}  {v * 100f:0}%";
        }
    }
}
