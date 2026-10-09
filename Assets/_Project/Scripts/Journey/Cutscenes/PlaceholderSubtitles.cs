using UnityEngine;
using UnityEngine.UI;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// A plain subtitle bar for cutscene dialogue, drawn until S3's UI scene draws the real one.
    /// It only listens to <see cref="DialogueEvents"/>, exactly as the UI will, so switching to
    /// the real subtitles is a matter of turning this component off.
    /// </summary>
    /// <remarks>
    /// Drawn on its own screen-space canvas that scales with the screen from a 1920 x 1080
    /// reference, with a dynamic font. A dynamic font draws its glyphs at the size they end up
    /// on screen, so the text stays sharp at any resolution or Game view scale. The first
    /// version drew with IMGUI at a fixed 18 px, which was blurred and blocky once the view
    /// was scaled. The panel sits just above the letterbox's bottom bar and sorts above it.
    /// </remarks>
    public sealed class PlaceholderSubtitles : MonoBehaviour
    {
        [Tooltip("Canvas sorting order: above the letterbox (40).")]
        [SerializeField] int sortingOrder = 50;

        [Tooltip("Text size at the 1920 x 1080 reference.")]
        [SerializeField, Min(8)] int fontSize = 34;

        [Tooltip("Widest the panel gets at the reference resolution.")]
        [SerializeField, Min(200f)] float maxWidth = 1300f;

        [Tooltip("Gap under the panel, as a fraction of the screen height (clears the letterbox bar).")]
        [SerializeField, Range(0f, 0.4f)] float bottomGap = 0.125f;

        [SerializeField] Color panelColour = new Color(0.03f, 0.02f, 0.07f, 0.72f);

        Canvas _canvas;
        Text _text;

        /// <summary>True while a line is on screen.</summary>
        public bool IsShowing => _canvas != null && _canvas.enabled;

        /// <summary>The text on screen, with its rich-text tags (for tests).</summary>
        public string Shown => _text != null ? _text.text : string.Empty;

        void Awake() => Build();

        void OnEnable()
        {
            DialogueEvents.OnLineShown += Show;
            DialogueEvents.OnLineCleared += Hide;
        }

        void OnDisable()
        {
            DialogueEvents.OnLineShown -= Show;
            DialogueEvents.OnLineCleared -= Hide;
            Hide();
        }

        void Show(DialogueLineView line)
        {
            string tag = ColorUtility.ToHtmlStringRGB(line.TagColour);
            _text.text = $"<b><color=#{tag}>{line.SpeakerTag}</color></b>\n{line.VisibleText}";
            _canvas.enabled = true;
        }

        void Hide()
        {
            if (_canvas != null)
                _canvas.enabled = false;
        }

        void Build()
        {
            var canvasObject = new GameObject("Subtitles Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;
            _canvas.pixelPerfect = true;
            _canvas.enabled = false;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;   // size by height, so wide screens do not shrink the text

            // The panel grows upwards from its bottom edge to fit the text.
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasObject.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, bottomGap);
            panelRect.anchorMax = new Vector2(0.5f, bottomGap);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = new Vector2(maxWidth, 0f);
            Image background = panel.AddComponent<Image>();
            background.color = panelColour;
            background.raycastTarget = false;
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 16, 18);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var fit = panel.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textObject = new GameObject("Line", typeof(RectTransform));
            textObject.transform.SetParent(panel.transform, false);
            _text = textObject.AddComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = fontSize;
            _text.lineSpacing = 1.1f;
            _text.color = new Color(0.96f, 0.95f, 1f);
            _text.alignment = TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.supportRichText = true;
            _text.raycastTarget = false;
            var shadow = textObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);
        }
    }
}
