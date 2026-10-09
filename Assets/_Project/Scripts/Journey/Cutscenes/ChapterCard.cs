using UnityEngine;
using UnityEngine.UI;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// The chapter card: when a chapter starts, a card shows "CHAPTER n OF 4", the chapter's
    /// title and subtitle (from its chapter data) and the four-node journey route, for 4.5 s,
    /// without blocking play. It never shows over a cutscene: a chapter that starts during one
    /// gets its card when the cutscene ends, so it follows the intro and each switch cutscene,
    /// as Story.md describes.
    /// </summary>
    /// <remarks>
    /// <para>Built in code on its own screen-space canvas, scaled from 1920 x 1080 with a dynamic
    /// font like the placeholder subtitles, so it needs no assets and stays sharp. It sorts at
    /// 45, above the letterbox (40) and below the subtitles (50). The route's dots are a
    /// circle generated once at start-up.</para>
    /// <para>It fades and slides in over 0.35 s, holds, and fades out over the last 0.6 s.
    /// Times are unscaled, like the letterbox. A cutscene that starts while it is up hides it;
    /// it shows again, from the start, once that cutscene ends.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ChapterCard : MonoBehaviour
    {
        [Tooltip("Seconds the card stays up.")]
        [SerializeField, Min(0.5f)] float seconds = 4.5f;

        [Tooltip("Seconds after a cutscene ends before its chapter card appears, so the letterbox has opened.")]
        [SerializeField, Min(0f)] float afterCutsceneDelay = 0.5f;

        [Tooltip("Canvas sorting order: above the letterbox (40), below the subtitles (50).")]
        [SerializeField] int sortingOrder = 45;

        [SerializeField] Color accent = new Color(0.357f, 0.890f, 0.710f);   // mint
        [SerializeField] Color panelColour = new Color(0.03f, 0.02f, 0.07f, 0.78f);

        const int RouteNodes = 4;
        const float FadeIn = 0.35f;
        const float FadeOut = 0.6f;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _panel;
        Text _chapterLine, _title, _subtitle;
        readonly Image[] _rings = new Image[RouteNodes];
        readonly Image[] _dots = new Image[RouteNodes];
        readonly Image[] _links = new Image[RouteNodes - 1];
        Texture2D _circleTexture;
        Sprite _circle;
        Vector2 _panelRest;

        bool _inCutscene;
        int _pending;          // a chapter whose card waits for a cutscene to end (0 = none)
        float _pendingAt;      // unscaled time it may show
        float _time = -1f;     // seconds the card has been up (negative = hidden)

        /// <summary>True while the card is on screen.</summary>
        public bool IsShowing => _time >= 0f;

        /// <summary>The chapter the card is for, or waiting to show (0 = none).</summary>
        public int Chapter { get; private set; }

        /// <summary>The card's text: "CHAPTER n OF 4", title and subtitle (for tests).</summary>
        public string ChapterLine => _chapterLine != null ? _chapterLine.text : string.Empty;
        public string Title => _title != null ? _title.text : string.Empty;
        public string Subtitle => _subtitle != null ? _subtitle.text : string.Empty;

        /// <summary>Chapters already finished on the route, and the current one, as shown (for tests).</summary>
        public int RouteDone { get; private set; }

        void Awake() => Build();

        void OnEnable()
        {
            ChapterEvents.OnChapterStarted += HandleChapterStarted;
            CutsceneEvents.OnCutsceneStarted += HandleCutsceneStarted;
            CutsceneEvents.OnCutsceneEnded += HandleCutsceneEnded;
        }

        void OnDisable()
        {
            ChapterEvents.OnChapterStarted -= HandleChapterStarted;
            CutsceneEvents.OnCutsceneStarted -= HandleCutsceneStarted;
            CutsceneEvents.OnCutsceneEnded -= HandleCutsceneEnded;
            Hide();
        }

        void OnDestroy()
        {
            if (_circle != null)
                Destroy(_circle);
            if (_circleTexture != null)
                Destroy(_circleTexture);
        }

        void HandleChapterStarted(int chapter)
        {
            if (_inCutscene)
            {
                _pending = chapter;
                _pendingAt = float.PositiveInfinity;   // set when the cutscene ends
                return;
            }
            Show(chapter);
        }

        void HandleCutsceneStarted(string cutsceneId)
        {
            _inCutscene = true;
            if (IsShowing)
            {
                // Step aside for the cutscene; come back after it.
                _pending = Chapter;
                _pendingAt = float.PositiveInfinity;
                Hide();
            }
        }

        void HandleCutsceneEnded(string cutsceneId)
        {
            _inCutscene = false;
            if (_pending > 0)
                _pendingAt = Time.unscaledTime + afterCutsceneDelay;
        }

        /// <summary>Shows the card for <paramref name="chapter"/> now, replacing any card up.</summary>
        public void Show(int chapter)
        {
            _pending = 0;
            Chapter = chapter;

            ChapterFlow flow = ChapterManager.Current != null ? ChapterManager.Current.Flow : null;
            int count = flow != null && flow.ChapterCount > 0 ? flow.ChapterCount : RouteNodes;
            ChapterDefinition definition = flow != null && chapter >= 1 && chapter <= flow.ChapterCount ? flow.GetDefinition(chapter) : null;

            _chapterLine.text = $"CHAPTER {chapter} OF {count}";
            _title.text = definition != null ? definition.Title : string.Empty;
            _subtitle.text = definition != null ? definition.Subtitle : string.Empty;
            PaintRoute(chapter);

            _time = 0f;
            _canvas.enabled = true;
            Animate();
        }

        void Hide()
        {
            _time = -1f;
            if (_canvas != null)
                _canvas.enabled = false;
        }

        void Update()
        {
            if (!IsShowing && _pending > 0 && !_inCutscene && Time.unscaledTime >= _pendingAt)
                Show(_pending);
            if (!IsShowing)
                return;

            _time += Time.unscaledDeltaTime;
            if (_time >= seconds)
                Hide();
            else
                Animate();
        }

        // Slides down into place and fades in, holds, fades out at the end.
        void Animate()
        {
            float fadeIn = Mathf.Clamp01(_time / FadeIn);
            float fadeOut = Mathf.Clamp01((seconds - _time) / FadeOut);
            float ease = fadeIn * fadeIn * (3f - 2f * fadeIn);
            _group.alpha = Mathf.Min(ease, fadeOut);
            _panel.anchoredPosition = _panelRest + Vector2.up * (24f * (1f - ease));
        }

        // Finished chapters are solid accent, the current one white with an accent ring, the
        // ones ahead dark; the links are accent up to the current chapter.
        void PaintRoute(int chapter)
        {
            RouteDone = Mathf.Clamp(chapter, 0, RouteNodes);
            Color ahead = new Color(0.25f, 0.24f, 0.3f, 1f);
            for (int i = 0; i < RouteNodes; i++)
            {
                int node = i + 1;
                bool done = node < chapter;
                bool current = node == chapter;
                _rings[i].color = current ? accent : done ? accent : new Color(0.45f, 0.44f, 0.5f, 1f);
                _dots[i].color = current ? Color.white : done ? accent : ahead;
                _rings[i].rectTransform.localScale = Vector3.one * (current ? 1.25f : 1f);
            }
            for (int i = 0; i < _links.Length; i++)
                _links[i].color = i + 2 <= chapter ? accent : ahead;
        }

        // ---- Building the card ----------------------------------------------------------

        void Build()
        {
            _circleTexture = CircleTexture(64);
            _circle = Sprite.Create(_circleTexture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);

            var canvasObject = new GameObject("Chapter Card Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;
            _canvas.enabled = false;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            _group = canvasObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;   // never blocks play

            _panel = Rect("Card", canvasObject.transform, new Vector2(0.5f, 0.78f), new Vector2(980f, 270f));
            _panelRest = _panel.anchoredPosition;
            Image panel = _panel.gameObject.AddComponent<Image>();
            panel.color = panelColour;
            panel.raycastTarget = false;
            Image line = Rect("Accent", _panel, new Vector2(0.5f, 1f), new Vector2(980f, 5f)).gameObject.AddComponent<Image>();
            line.color = accent;
            line.raycastTarget = false;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _chapterLine = Label("Chapter", _panel, new Vector2(0f, 95f), 28, accent, FontStyle.Bold, font);
            _title = Label("Title", _panel, new Vector2(0f, 38f), 66, Color.white, FontStyle.Bold, font);
            _subtitle = Label("Subtitle", _panel, new Vector2(0f, -22f), 32, new Color(0.86f, 0.86f, 0.9f), FontStyle.Normal, font);

            // The four-node route along the bottom of the card.
            const float spacing = 120f;
            float left = -spacing * (RouteNodes - 1) * 0.5f;
            for (int i = 0; i < RouteNodes - 1; i++)
            {
                RectTransform link = Rect($"Link{i + 1}", _panel, new Vector2(0.5f, 0.5f), new Vector2(spacing - 30f, 5f));
                link.anchoredPosition = new Vector2(left + spacing * (i + 0.5f), -88f);
                _links[i] = link.gameObject.AddComponent<Image>();
                _links[i].raycastTarget = false;
            }
            for (int i = 0; i < RouteNodes; i++)
            {
                RectTransform ring = Rect($"Node{i + 1}", _panel, new Vector2(0.5f, 0.5f), new Vector2(30f, 30f));
                ring.anchoredPosition = new Vector2(left + spacing * i, -88f);
                _rings[i] = ring.gameObject.AddComponent<Image>();
                _rings[i].sprite = _circle;
                _rings[i].raycastTarget = false;
                RectTransform dot = Rect("Fill", ring, new Vector2(0.5f, 0.5f), new Vector2(20f, 20f));
                _dots[i] = dot.gameObject.AddComponent<Image>();
                _dots[i].sprite = _circle;
                _dots[i].raycastTarget = false;
            }
        }

        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return rect;
        }

        static Text Label(string name, RectTransform parent, Vector2 position, int size, Color colour, FontStyle style, Font font)
        {
            RectTransform rect = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(940f, size * 1.4f));
            rect.anchoredPosition = position;
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = colour;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        // A white disc with a soft one-pixel edge, so the dots are round at any size.
        static Texture2D CircleTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ChapterCardCircle", wrapMode = TextureWrapMode.Clamp };
            float radius = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius, dy = y + 0.5f - radius;
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
