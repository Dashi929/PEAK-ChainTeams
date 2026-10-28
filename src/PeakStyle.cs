using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChainTeams
{
    /// <summary>
    /// PEAK 风格 UI 皮肤：奶油纸张底色、深棕粗描边、圆角块状按钮（运行时生成圆角纹理，
    /// 用 GUIStyle.border 保证拉伸时四角不变形）。随面板缩放系数重建。
    /// </summary>
    public static class PeakStyle
    {
        // PEAK 配色
        public static readonly Color Cream = new Color(0.955f, 0.922f, 0.847f);   // 纸张底 #F4EBD8
        public static readonly Color CreamDark = new Color(0.914f, 0.867f, 0.745f); // 区块底 #E9DDBE
        public static readonly Color Border = new Color(0.29f, 0.231f, 0.173f);   // 深棕描边 #4A3B2C
        public static readonly Color Text = new Color(0.227f, 0.180f, 0.125f);    // 深棕文字 #3A2E20
        public static readonly Color Accent = new Color(0.961f, 0.620f, 0.239f);  // 橙 #F59E3D
        public static readonly Color Good = new Color(0.478f, 0.714f, 0.384f);    // 绿 #7AB662
        public static readonly Color Bad = new Color(0.886f, 0.341f, 0.298f);     // 红 #E2574C

        public static GUISkin Skin;
        public static GUIStyle BoxStyle;
        public static GUIStyle SliderTrack;
        public static GUIStyle SliderThumb;

        private static readonly Dictionary<Color, GUIStyle> Buttons = new Dictionary<Color, GUIStyle>();
        private static readonly List<Object> _texCache = new List<Object>();
        private static float _builtFor = -1f;

        /// <summary>按缩放系数构建全套皮肤；比例变化时重建。</summary>
        public static void Build(float scale)
        {
            if (Skin != null && Mathf.Abs(_builtFor - scale) < 0.01f) return;
            Dispose();

            var f = Mathf.RoundToInt(13 * scale);
            var corner = Mathf.Max(4, Mathf.RoundToInt(9 * scale));
            var borderW = Mathf.Max(2, Mathf.RoundToInt(3 * scale));

            Skin = Object.Instantiate(GUI.skin);
            Skin.label.fontSize = f;
            Skin.label.normal.textColor = Text;
            Skin.label.fontStyle = FontStyle.Bold;
            Skin.box.fontSize = f;
            Skin.button.fontSize = f;
            Skin.toggle.fontSize = f;
            Skin.textField.fontSize = f;
            Skin.window.fontSize = f + Mathf.RoundToInt(2 * scale);

            // 窗口：奶油纸底 + 深棕描边圆角
            var winTex = RoundTex(Cream, Border, Mathf.RoundToInt(12 * scale), borderW);
            Skin.window.normal.background = winTex;
            Skin.window.onNormal.background = winTex;
            Skin.window.normal.textColor = Text;
            Skin.window.onNormal.textColor = Text;
            Skin.window.border = new RectOffset(corner + borderW, corner + borderW, corner + borderW, corner + borderW);
            var pad = Mathf.RoundToInt(14 * scale);
            Skin.window.padding = new RectOffset(pad, pad, pad + Mathf.RoundToInt(6 * scale), pad);

            // 区块：稍深的奶油底 + 细描边圆角
            BoxStyle = new GUIStyle(Skin.box)
            {
                normal = { background = RoundTex(CreamDark, Border, corner, borderW), textColor = Text },
                hover = { background = RoundTex(CreamDark, Border, corner, borderW), textColor = Text },
                border = new RectOffset(corner + borderW, corner + borderW, corner + borderW, corner + borderW),
                padding = new RectOffset(Mathf.RoundToInt(10 * scale), Mathf.RoundToInt(10 * scale), Mathf.RoundToInt(8 * scale), Mathf.RoundToInt(8 * scale)),
                margin = new RectOffset(2, 2, 2, 2),
            };
            Skin.box = BoxStyle;

            // 输入框：白底描边
            var tfTex = RoundTex(Color.white, Border, corner, 2);
            Skin.textField.normal.background = tfTex;
            Skin.textField.active.background = tfTex;
            Skin.textField.focused.background = tfTex;
            Skin.textField.normal.textColor = Text;
            Skin.textField.border = new RectOffset(corner + 2, corner + 2, corner + 2, corner + 2);

            // 滑条：深棕轨道 + 橙色圆钮
            var trackTex = RoundTex(new Color(0.80f, 0.74f, 0.63f), Border, Mathf.Max(3, Mathf.RoundToInt(5 * scale)), 2);
            SliderTrack = new GUIStyle(GUI.skin.horizontalSlider)
            {
                normal = { background = trackTex },
                border = new RectOffset(8, 8, 2, 2),
                fixedHeight = Mathf.Round(22 * scale),
            };
            var thumbR = Mathf.RoundToInt(15 * scale);
            SliderThumb = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                normal = { background = RoundTex(Accent, Border, thumbR, borderW), textColor = Color.white },
                hover = { background = RoundTex(Lighten(Accent, 0.1f), Border, thumbR, borderW) },
                active = { background = RoundTex(Darken(Accent, 0.12f), Border, thumbR, borderW) },
                border = new RectOffset(thumbR + borderW, thumbR + borderW, thumbR + borderW, thumbR + borderW),
                fixedWidth = Mathf.Round(30 * scale),
                fixedHeight = Mathf.Round(30 * scale),
            };

            // 滚动条收进深棕极简
            var sbThumb = RoundTex(new Color(0.55f, 0.47f, 0.37f), Border, Mathf.Max(3, Mathf.RoundToInt(6 * scale)), 2);
            Skin.verticalScrollbarThumb.normal.background = sbThumb;
            Skin.verticalScrollbar.normal.background = Texture2D.whiteTexture.MakeInvisible();

            _builtFor = scale;
        }

        /// <summary>取某填充色的圆角按钮样式（含 hover/active 与自适应文字色），按颜色缓存。</summary>
        public static GUIStyle Button(Color fill)
        {
            GUIStyle st;
            if (Buttons.TryGetValue(fill, out st)) return st;

            var scale = _builtFor;
            var corner = Mathf.Max(4, Mathf.RoundToInt(8 * scale));
            var borderW = Mathf.Max(2, Mathf.RoundToInt(3 * scale));
            st = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(13 * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
            };
            st.normal.background = RoundTex(fill, Border, corner, borderW);
            st.normal.textColor = TextOn(fill);
            st.hover.background = RoundTex(Lighten(fill, 0.08f), Border, corner, borderW);
            st.hover.textColor = TextOn(fill);
            st.active.background = RoundTex(Darken(fill, 0.12f), Border, corner, borderW);
            st.active.textColor = TextOn(fill);
            st.focused.background = st.normal.background;
            st.focused.textColor = st.normal.textColor;
            var b = corner + borderW;
            st.border = new RectOffset(b, b, b, b);
            st.padding = new RectOffset(Mathf.RoundToInt(8 * scale), Mathf.RoundToInt(8 * scale), Mathf.RoundToInt(4 * scale), Mathf.RoundToInt(6 * scale));
            Buttons[fill] = st;
            return st;
        }

        // ---- 工具 ----

        private static Color TextOn(Color c)
        {
            return c.r * 0.299f + c.g * 0.587f + c.b * 0.114f > 0.56f ? Text : Color.white;
        }

        private static Color Lighten(Color c, float k)
        {
            return new Color(c.r + (1 - c.r) * k, c.g + (1 - c.g) * k, c.b + (1 - c.b) * k, c.a);
        }

        private static Color Darken(Color c, float k)
        {
            return new Color(c.r * (1 - k), c.g * (1 - k), c.b * (1 - k), c.a);
        }

        private static Texture2D MakeInvisible(this Texture2D t)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, new Color(0, 0, 0, 0));
            tex.Apply();
            tex.hideFlags = HideFlags.DontSave;
            _texCache.Add(tex);
            return tex;
        }

        /// <summary>生成"填充+描边"的圆角方形纹理（透明角落，配合 border 拉伸不变形）。</summary>
        private static Texture2D RoundTex(Color fill, Color border, int radius, int borderW)
        {
            var corner = radius + borderW;
            var size = corner * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var cols = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // 到中央圆角矩形的带符号距离（以中心为原点）
                    var px = x - (size - 1) * 0.5f;
                    var py = y - (size - 1) * 0.5f;
                    var half = (size - 1) * 0.5f - corner;
                    var qx = Mathf.Abs(px) - half;
                    var qy = Mathf.Abs(py) - half;
                    var dist = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0);
                    Color c;
                    if (dist > radius + borderW * 0.5f) c = new Color(0, 0, 0, 0);
                    else if (dist > radius - borderW * 0.5f) c = border;
                    else c = fill;
                    cols[y * size + x] = c;
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            _texCache.Add(tex);
            return tex;
        }

        private static void Dispose()
        {
            Buttons.Clear();
            foreach (var t in _texCache)
            {
                if (t != null) Object.Destroy(t);
            }
            _texCache.Clear();
            if (Skin != null)
            {
                Object.Destroy(Skin);
                Skin = null;
            }
        }
    }
}
