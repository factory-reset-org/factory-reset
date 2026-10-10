using UnityEngine;
using UnityEngine.UI;

namespace ToyFactory.UI
{
    /// <summary>The Art Bible's UI colours and the small helpers that build the HUD's uGUI objects in code.</summary>
    /// <remarks>
    /// The HUD builds its own objects, like the cutscene letterbox, so the scene holds one component
    /// and no hand-edited YAML. Text uses Unity's built-in font: TextMeshPro would need its essential
    /// resources imported into the project, which this HUD does not need.
    /// </remarks>
    internal static class HudWidgets
    {
        public static readonly Color Plum = new Color32(0x1E, 0x16, 0x38, 0xFF);
        public static readonly Color PlumPanel = new Color32(0x1E, 0x16, 0x38, 0xCC);
        public static readonly Color PlumLight = new Color32(0x3A, 0x2E, 0x62, 0xFF);
        public static readonly Color Ink = new Color32(0xFF, 0xF8, 0xEC, 0xFF);
        public static readonly Color InkDim = new Color32(0xFF, 0xF8, 0xEC, 0x99);
        public static readonly Color Tomato = new Color32(0xFF, 0x5A, 0x4E, 0xFF);
        public static readonly Color Sun = new Color32(0xFF, 0xC9, 0x33, 0xFF);
        public static readonly Color Mint = new Color32(0x3D, 0xDB, 0xB0, 0xFF);
        public static readonly Color Cobalt = new Color32(0x3A, 0x6C, 0xF4, 0xFF);

        static Font _font;

        /// <summary>Unity's built-in UI font.</summary>
        public static Font Font
        {
            get
            {
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// <summary>A new empty rect under <paramref name="parent"/>, stretched to fill it.</summary>
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>Puts <paramref name="rect"/> at a pixel position and size relative to one anchor point.</summary>
        public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>A flat coloured image that ignores the pointer.</summary>
        public static Image Panel(string name, Transform parent, Color colour, Sprite sprite = null)
        {
            RectTransform rect = Rect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A text label that ignores the pointer.</summary>
        public static Text Label(string name, Transform parent, string text, int size, Color colour,
            TextAnchor alignment, FontStyle style = FontStyle.Bold)
        {
            RectTransform rect = Rect(name, parent);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = colour;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        /// <summary>A full-screen overlay canvas scaled to a 1920 x 1080 layout.</summary>
        public static Canvas ScreenCanvas(string name, Transform parent, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>Sets a bar's fill, a child image anchored to the left of its track.</summary>
        public static void SetFill(Image fill, float fraction)
        {
            RectTransform rect = fill.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(HudMath.Fraction(fraction), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
