using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal sealed class ConfigEditorTheme
    {
        private static readonly Color WindowColor = new Color(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color BorderColor = new Color(0.48f, 0.48f, 0.48f, 1f);
        private static readonly Color EntryColor = new Color(0.12f, 0.12f, 0.12f, 0.95f);
        private static readonly Color TextColorValue = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color ButtonColor = new Color(0.24f, 0.24f, 0.24f, 1f);
        private static readonly Color AccentColorValue = new Color(0.38f, 0.55f, 0.72f, 1f);

        private GUISkin sourceSkin;
        private GUISkin runtimeSkin;
        private int currentFontSize = -1;

        private Texture2D windowTexture;
        private Texture2D borderTexture;
        private Texture2D entryTexture;
        private Texture2D buttonTexture;
        private Texture2D buttonHoverTexture;
        private Texture2D buttonActiveTexture;
        private Texture2D accentTexture;
        private Texture2D accentHoverTexture;
        private Texture2D scrollbarTrackTexture;
        private Texture2D toggleOffTexture;
        private Texture2D toggleOnTexture;
        private Texture2D toggleOffHoverTexture;
        private Texture2D toggleOnHoverTexture;

        private GUIStyle accentButtonStyle;
        private GUIStyle resizeHandleStyle;
        private GUIStyle compactToggleOffStyle;
        private GUIStyle compactToggleOnStyle;

        internal GUISkin Skin
        {
            get
            {
                EnsureStyles();
                return runtimeSkin ?? GUI.skin;
            }
        }

        internal Texture2D WindowTexture
        {
            get
            {
                EnsureStyles();
                return windowTexture;
            }
        }

        internal Texture2D BorderTexture
        {
            get
            {
                EnsureStyles();
                return borderTexture;
            }
        }

        internal GUIStyle AccentButtonStyle
        {
            get
            {
                EnsureStyles();
                return accentButtonStyle ?? GUI.skin.button;
            }
        }

        internal GUIStyle ResizeHandleStyle
        {
            get
            {
                EnsureStyles();
                return resizeHandleStyle ?? GUI.skin.box;
            }
        }

        internal GUIStyle CompactToggleOffStyle
        {
            get
            {
                EnsureStyles();
                return compactToggleOffStyle ?? GUI.skin.box;
            }
        }

        internal GUIStyle CompactToggleOnStyle
        {
            get
            {
                EnsureStyles();
                return compactToggleOnStyle ?? GUI.skin.box;
            }
        }

        internal float CompactToggleSize
        {
            get
            {
                EnsureStyles();
                return Mathf.Clamp((configEditorFontSize?.Value ?? 13) - 2f, 9f, 16f);
            }
        }

        internal Color TextColor => TextColorValue;

        internal void EnsureStyles()
        {
            GUISkin current = GUI.skin;
            int fontSize = Mathf.Clamp(configEditorFontSize?.Value ?? 13, 9, 28);
            if (runtimeSkin != null && currentFontSize == fontSize && (sourceSkin == current || runtimeSkin == current))
                return;

            Rebuild(current == runtimeSkin ? sourceSkin : current, fontSize);
        }

        internal void Shutdown()
        {
            DestroyResources();
        }

        private void Rebuild(GUISkin baseSkin, int fontSize)
        {
            DestroyResources();
            sourceSkin = baseSkin;
            currentFontSize = fontSize;
            if (baseSkin == null)
                return;

            windowTexture = CreateTexture(WindowColor);
            borderTexture = CreateTexture(BorderColor);
            entryTexture = CreateTexture(EntryColor);
            buttonTexture = CreateTexture(ButtonColor);
            buttonHoverTexture = CreateTexture(Lighten(ButtonColor, 0.12f));
            buttonActiveTexture = CreateTexture(Darken(AccentColorValue, 0.1f));
            accentTexture = CreateTexture(AccentColorValue);
            accentHoverTexture = CreateTexture(Lighten(AccentColorValue, 0.12f));
            scrollbarTrackTexture = CreateTexture(Darken(EntryColor, 0.08f));
            Color toggleOff = CreateNeutralToggleColor(EntryColor, ButtonColor);
            toggleOffTexture = CreateBorderedTexture(toggleOff, BorderColor);
            toggleOnTexture = CreateBorderedTexture(AccentColorValue, BorderColor);
            toggleOffHoverTexture = CreateBorderedTexture(Lighten(toggleOff, 0.12f), Lighten(BorderColor, 0.2f));
            toggleOnHoverTexture = CreateBorderedTexture(Lighten(AccentColorValue, 0.12f), Lighten(BorderColor, 0.2f));

            runtimeSkin = UnityEngine.Object.Instantiate(baseSkin);
            runtimeSkin.name = "TradersExtendedConfigEditorSkin";
            runtimeSkin.hideFlags = HideFlags.HideAndDontSave;

            ConfigureTextStyle(runtimeSkin.label, TextColorValue, fontSize);
            ConfigureTextStyle(runtimeSkin.box, TextColorValue, fontSize);
            ConfigureTextStyle(runtimeSkin.window, Color.white, fontSize);
            ConfigureTextStyle(runtimeSkin.button, Color.white, fontSize);
            ConfigureTextStyle(runtimeSkin.toggle, TextColorValue, fontSize);
            ConfigureTextStyle(runtimeSkin.textField, TextColorValue, fontSize);
            ConfigureTextStyle(runtimeSkin.textArea, TextColorValue, fontSize);

            SetAllBackgrounds(runtimeSkin.window, windowTexture);
            runtimeSkin.window.padding = new RectOffset(4, 4, 21, 4);
            runtimeSkin.window.margin = new RectOffset(0, 0, 0, 0);
            runtimeSkin.window.border = new RectOffset(0, 0, 0, 0);

            SetAllBackgrounds(runtimeSkin.box, entryTexture);
            runtimeSkin.box.padding = new RectOffset(4, 4, 3, 3);
            runtimeSkin.box.margin = new RectOffset(1, 1, 1, 1);
            runtimeSkin.box.border = new RectOffset(0, 0, 0, 0);

            SetAllBackgrounds(runtimeSkin.textField, entryTexture);
            runtimeSkin.textField.padding = new RectOffset(4, 4, 1, 1);
            runtimeSkin.textField.margin = new RectOffset(1, 1, 1, 1);
            runtimeSkin.textField.border = new RectOffset(0, 0, 0, 0);
            runtimeSkin.textField.alignment = TextAnchor.MiddleLeft;

            runtimeSkin.button.padding = new RectOffset(5, 5, 2, 2);
            runtimeSkin.button.margin = new RectOffset(2, 2, 1, 1);
            runtimeSkin.button.border = new RectOffset(0, 0, 0, 0);
            runtimeSkin.toggle.margin = new RectOffset(1, 1, 1, 1);
            runtimeSkin.label.margin = new RectOffset(1, 1, 0, 0);

            SetButtonBackgrounds(runtimeSkin.button);
            ConfigureSquareScrollbars(runtimeSkin);

            accentButtonStyle = new GUIStyle(runtimeSkin.button);
            SetAllBackgrounds(accentButtonStyle, accentTexture);
            accentButtonStyle.hover.background = accentHoverTexture;
            accentButtonStyle.onHover.background = accentHoverTexture;
            accentButtonStyle.active.background = buttonActiveTexture;
            accentButtonStyle.onActive.background = buttonActiveTexture;

            resizeHandleStyle = new GUIStyle(runtimeSkin.box)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(0, 0, 0, 0)
            };
            SetAllBackgrounds(resizeHandleStyle, accentTexture);
            resizeHandleStyle.hover.background = accentHoverTexture;
            resizeHandleStyle.active.background = buttonActiveTexture;

            compactToggleOffStyle = new GUIStyle(runtimeSkin.box)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(1, 1, 1, 1)
            };
            SetAllBackgrounds(compactToggleOffStyle, toggleOffTexture);
            compactToggleOffStyle.hover.background = toggleOffHoverTexture;
            compactToggleOffStyle.active.background = toggleOffHoverTexture;

            compactToggleOnStyle = new GUIStyle(compactToggleOffStyle)
            {
                border = new RectOffset(1, 1, 1, 1)
            };
            SetAllBackgrounds(compactToggleOnStyle, toggleOnTexture);
            compactToggleOnStyle.hover.background = toggleOnHoverTexture;
            compactToggleOnStyle.active.background = toggleOnHoverTexture;
        }

        private void ConfigureSquareScrollbars(GUISkin skin)
        {
            const float size = 13f;
            ConfigureScrollbarTrack(skin.horizontalScrollbar, true, size);
            ConfigureScrollbarTrack(skin.verticalScrollbar, false, size);
            ConfigureScrollbarThumb(skin.horizontalScrollbarThumb, true, size);
            ConfigureScrollbarThumb(skin.verticalScrollbarThumb, false, size);
            HideScrollbarButton(skin.horizontalScrollbarLeftButton);
            HideScrollbarButton(skin.horizontalScrollbarRightButton);
            HideScrollbarButton(skin.verticalScrollbarUpButton);
            HideScrollbarButton(skin.verticalScrollbarDownButton);
        }

        private void ConfigureScrollbarTrack(GUIStyle style, bool horizontal, float size)
        {
            if (style == null)
                return;
            SetAllBackgrounds(style, scrollbarTrackTexture);
            style.border = new RectOffset(0, 0, 0, 0);
            style.margin = horizontal ? new RectOffset(0, 0, 0, 0) : new RectOffset(0, 1, 0, 0);
            style.padding = new RectOffset(0, 0, 0, 0);
            if (horizontal)
                style.fixedHeight = size;
            else
                style.fixedWidth = size;
        }

        private void ConfigureScrollbarThumb(GUIStyle style, bool horizontal, float size)
        {
            if (style == null)
                return;
            SetAllBackgrounds(style, accentTexture);
            style.hover.background = accentHoverTexture;
            style.active.background = buttonActiveTexture;
            style.border = new RectOffset(0, 0, 0, 0);
            style.margin = new RectOffset(1, 1, 1, 1);
            style.padding = new RectOffset(0, 0, 0, 0);
            if (horizontal)
                style.fixedHeight = Mathf.Max(1f, size - 2f);
            else
                style.fixedWidth = Mathf.Max(1f, size - 2f);
        }

        private static void HideScrollbarButton(GUIStyle style)
        {
            if (style == null)
                return;
            SetAllBackgrounds(style, null);
            style.border = new RectOffset(0, 0, 0, 0);
            style.margin = new RectOffset(0, 0, 0, 0);
            style.padding = new RectOffset(0, 0, 0, 0);
            style.fixedWidth = 0f;
            style.fixedHeight = 0f;
            style.stretchWidth = false;
            style.stretchHeight = false;
        }

        private void SetButtonBackgrounds(GUIStyle style)
        {
            style.normal.background = buttonTexture;
            style.onNormal.background = accentTexture;
            style.hover.background = buttonHoverTexture;
            style.onHover.background = buttonHoverTexture;
            style.active.background = buttonActiveTexture;
            style.onActive.background = buttonActiveTexture;
            style.focused.background = buttonTexture;
            style.onFocused.background = accentTexture;
        }

        private static void SetAllBackgrounds(GUIStyle style, Texture2D texture)
        {
            if (style == null)
                return;
            style.normal.background = texture;
            style.hover.background = texture;
            style.active.background = texture;
            style.focused.background = texture;
            style.onNormal.background = texture;
            style.onHover.background = texture;
            style.onActive.background = texture;
            style.onFocused.background = texture;
        }

        private static void ConfigureTextStyle(GUIStyle style, Color color, int fontSize)
        {
            if (style == null)
                return;
            style.fontSize = fontSize;
            SetTextColor(style.normal, color);
            SetTextColor(style.hover, color);
            SetTextColor(style.active, color);
            SetTextColor(style.focused, color);
            SetTextColor(style.onNormal, color);
            SetTextColor(style.onHover, color);
            SetTextColor(style.onActive, color);
            SetTextColor(style.onFocused, color);
        }

        private static void SetTextColor(GUIStyleState state, Color color)
        {
            if (state != null)
                state.textColor = color;
        }

        private static Texture2D CreateTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "TradersExtendedConfigEditorColor"
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateBorderedTexture(Color fill, Color border)
        {
            Texture2D texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "TradersExtendedConfigEditorBorderedColor"
            };
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                texture.SetPixel(x, y, x == 0 || y == 0 || x == 2 || y == 2 ? border : fill);
            texture.Apply(false, true);
            return texture;
        }

        private static Color Lighten(Color color, float amount) =>
            new Color(Mathf.Lerp(color.r, 1f, amount), Mathf.Lerp(color.g, 1f, amount), Mathf.Lerp(color.b, 1f, amount), color.a);

        private static Color Darken(Color color, float amount) =>
            new Color(color.r * (1f - amount), color.g * (1f - amount), color.b * (1f - amount), color.a);

        private static Color CreateNeutralToggleColor(Color entry, Color button)
        {
            Color mixed = Color.Lerp(entry, button, 0.55f);
            float gray = mixed.r * 0.299f + mixed.g * 0.587f + mixed.b * 0.114f;
            gray = Mathf.Lerp(gray, 1f, 0.08f);
            return new Color(gray, gray, gray, 1f);
        }

        private void DestroyResources()
        {
            Destroy(runtimeSkin);
            Destroy(windowTexture);
            Destroy(borderTexture);
            Destroy(entryTexture);
            Destroy(buttonTexture);
            Destroy(buttonHoverTexture);
            Destroy(buttonActiveTexture);
            Destroy(accentTexture);
            Destroy(accentHoverTexture);
            Destroy(scrollbarTrackTexture);
            Destroy(toggleOffTexture);
            Destroy(toggleOnTexture);
            Destroy(toggleOffHoverTexture);
            Destroy(toggleOnHoverTexture);
            runtimeSkin = null;
            sourceSkin = null;
            accentButtonStyle = null;
            resizeHandleStyle = null;
            compactToggleOffStyle = null;
            compactToggleOnStyle = null;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value != null)
                UnityEngine.Object.Destroy(value);
        }
    }

    internal static class ConfigEditorGui
    {
        internal static bool ToggleLayout(ConfigEditorTheme theme, bool value, GUIContent content, float width, GUIStyle labelStyle = null, float verticalOffset = 1f, float minimumHeight = 0f)
        {
            GUIStyle textStyle = labelStyle ?? GUI.skin.label;
            float height = Mathf.Max(18f, textStyle.lineHeight + 4f, minimumHeight);
            Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            return Toggle(theme, rect, value, content, textStyle, verticalOffset);
        }

        internal static bool Toggle(ConfigEditorTheme theme, Rect rect, bool value, GUIContent content, GUIStyle labelStyle = null, float verticalOffset = 1f)
        {
            GUIStyle textStyle = labelStyle ?? GUI.skin.label;
            string tooltip = content?.tooltip ?? string.Empty;
            bool result = GUI.Toggle(rect, value, new GUIContent(string.Empty, tooltip), GUIStyle.none);
            float size = theme?.CompactToggleSize ?? 10f;
            float centeredOffset = Mathf.Ceil(Mathf.Max(0f, (rect.height - size) * 0.5f));
            Rect checkRect = new Rect(rect.x + 1f, rect.y + centeredOffset + verticalOffset, size, size);
            GUI.Box(checkRect, new GUIContent(string.Empty, tooltip), result ? theme.CompactToggleOnStyle : theme.CompactToggleOffStyle);
            string text = content?.text ?? string.Empty;
            if (!string.IsNullOrEmpty(text))
            {
                float textX = checkRect.xMax + 4f;
                GUI.Label(new Rect(textX, rect.y, Mathf.Max(0f, rect.xMax - textX), rect.height), new GUIContent(text, tooltip), textStyle);
            }
            return result;
        }
    }
}
