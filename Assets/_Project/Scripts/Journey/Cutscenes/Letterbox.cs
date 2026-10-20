using UnityEngine;
using UnityEngine.UI;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Black bars that close in at the top and bottom of the screen while a cutscene plays and
    /// open again when it ends. It listens to <see cref="CutsceneEvents"/>, so skipping takes
    /// the bars away like a normal ending.
    /// </summary>
    /// <remarks>
    /// Builds its own screen-space canvas at start (two plain images, no assets). The canvas
    /// sorts at <see cref="sortingOrder"/>: subtitles must draw above it. When the bars are
    /// fully open the canvas is switched off, so gameplay pays nothing for it.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class Letterbox : MonoBehaviour
    {
        [Tooltip("Height of each bar as a fraction of the screen height.")]
        [SerializeField, Range(0f, 0.3f)] float barFraction = 0.11f;

        [Tooltip("Seconds for the bars to close in or open out.")]
        [SerializeField, Min(0.01f)] float slideSeconds = 0.6f;

        [Tooltip("Canvas sorting order. Subtitles and the chapter card must sort above this.")]
        [SerializeField] int sortingOrder = 40;

        [SerializeField] Color barColour = new Color(0.027f, 0.02f, 0.06f, 1f);

        Canvas _canvas;
        RectTransform _top, _bottom;
        float _target;

        /// <summary>0 when the bars are open, 1 when fully closed in.</summary>
        public float Amount { get; private set; }

        /// <summary>How much of the screen each bar covers right now.</summary>
        public float Coverage => barFraction * Ease(Amount);

        /// <summary>True while the bars are on screen.</summary>
        public bool IsShowing => _canvas != null && _canvas.enabled;

        /// <summary>Smoothstep, so the bars start and stop gently.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        void Awake() => Build();

        void OnEnable()
        {
            CutsceneEvents.OnCutsceneStarted += HandleStarted;
            CutsceneEvents.OnCutsceneEnded += HandleEnded;
        }

        void OnDisable()
        {
            CutsceneEvents.OnCutsceneStarted -= HandleStarted;
            CutsceneEvents.OnCutsceneEnded -= HandleEnded;
        }

        void HandleStarted(string cutsceneId) => _target = 1f;

        void HandleEnded(string cutsceneId) => _target = 0f;

        void Update()
        {
            if (Mathf.Approximately(Amount, _target) && _canvas.enabled == (_target > 0f))
                return;
            Amount = Mathf.MoveTowards(Amount, _target, Time.unscaledDeltaTime / slideSeconds);
            Apply();
        }

        void Apply()
        {
            float cover = Coverage;
            _top.anchorMin = new Vector2(0f, 1f - cover);
            _bottom.anchorMax = new Vector2(1f, cover);
            _canvas.enabled = Amount > 0f;
        }

        void Build()
        {
            var canvasObject = new GameObject("Letterbox Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;
            _canvas.enabled = false;

            _top = Bar("Top Bar", canvasObject.transform, new Vector2(0f, 1f), new Vector2(1f, 1f));
            _bottom = Bar("Bottom Bar", canvasObject.transform, new Vector2(0f, 0f), new Vector2(1f, 0f));
        }

        RectTransform Bar(string barName, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var bar = new GameObject(barName, typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            var rect = (RectTransform)bar.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = bar.AddComponent<Image>();
            image.color = barColour;
            image.raycastTarget = false;
            return rect;
        }
    }
}
