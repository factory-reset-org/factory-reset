using UnityEngine;
using UnityEngine.UI;
using ToyFactory.Interfaces;

namespace ToyFactory.UI
{
    /// <summary>
    /// The chapter card: "CHAPTER 2 OF 4", the chapter's title and subtitle, and the four-stop
    /// journey route with the current stop lit. It shows for <see cref="TotalSeconds"/> when a
    /// chapter starts, fades in and out, and never blocks play. When it shows is the presenter's
    /// job (it follows <c>ChapterEvents.OnChapterStarted</c>); this view only draws.
    /// </summary>
    public sealed class ChapterCardView : MonoBehaviour
    {
        /// <summary>Seconds the card is on screen, fades included.</summary>
        public const float TotalSeconds = 4.5f;

        /// <summary>Seconds of the fade in.</summary>
        public const float FadeInSeconds = 0.35f;

        /// <summary>Seconds of the fade out.</summary>
        public const float FadeOutSeconds = 0.8f;

        CanvasGroup _group;
        Text _tag, _title, _subtitle;
        Image[] _nodes;
        Image[] _links;
        float _shownAt = float.NegativeInfinity;

        /// <summary>The chapter the card was last shown for; 0 before the first time.</summary>
        public int Chapter { get; private set; }

        /// <summary>True while the card is on screen.</summary>
        public bool IsShowing => Time.unscaledTime - _shownAt < TotalSeconds;

        /// <summary>The card's current opacity.</summary>
        public float CurrentAlpha => _group != null ? _group.alpha : 0f;

        /// <summary>The opacity of the card <paramref name="elapsed"/> seconds after it appeared.</summary>
        public static float AlphaAt(float elapsed)
        {
            if (elapsed < 0f || elapsed >= TotalSeconds)
                return 0f;
            if (elapsed < FadeInSeconds)
                return elapsed / FadeInSeconds;
            float untilEnd = TotalSeconds - elapsed;
            return untilEnd < FadeOutSeconds ? untilEnd / FadeOutSeconds : 1f;
        }

        /// <summary>
        /// Builds the card's objects under this view's own rect, which the caller has stretched over
        /// the screen. Call once, before <see cref="Show"/>.
        /// </summary>
        internal void Build(HudSprites sprites)
        {
            Transform root = transform;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var panel = HudWidgets.Panel("Panel", root, HudWidgets.PlumPanel);
            HudWidgets.Place(panel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(800f, 230f));

            Transform p = panel.transform;
            _tag = HudWidgets.Label("Tag", p, string.Empty, 26, HudWidgets.Mint, TextAnchor.MiddleCenter);
            HudWidgets.Place(_tag.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(760f, 34f));
            _title = HudWidgets.Label("Title", p, string.Empty, 54, HudWidgets.Ink, TextAnchor.MiddleCenter);
            HudWidgets.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(780f, 66f));
            _subtitle = HudWidgets.Label("Subtitle", p, string.Empty, 26, HudWidgets.InkDim, TextAnchor.MiddleCenter, FontStyle.Normal);
            HudWidgets.Place(_subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -124f), new Vector2(780f, 34f));

            // The route: four stops joined by three links, centred along the bottom of the card.
            int count = ChapterEvents.ChapterCount;
            const float spacing = 90f;
            _nodes = new Image[count];
            _links = new Image[count - 1];
            for (int i = 0; i < count; i++)
            {
                float x = (i - (count - 1) * 0.5f) * spacing;
                if (i < count - 1)
                {
                    _links[i] = HudWidgets.Panel("Link " + (i + 1), p, HudWidgets.PlumLight);
                    HudWidgets.Place(_links[i].rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(x + spacing * 0.5f, 28f), new Vector2(spacing - 26f, 5f));
                }

                _nodes[i] = HudWidgets.Panel("Stop " + (i + 1), p, HudWidgets.PlumLight, sprites.Circle);
                HudWidgets.Place(_nodes[i].rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(x, 28f), new Vector2(26f, 26f));
            }
        }

        /// <summary>Shows the card for a chapter, restarting it if it is already up.</summary>
        public void Show(int chapter, string title, string subtitle)
        {
            if (_group == null)
                return;

            Chapter = chapter;
            _tag.text = HudMath.ChapterTag(chapter, ChapterEvents.ChapterCount);
            _title.text = title ?? string.Empty;
            _subtitle.text = subtitle ?? string.Empty;
            for (int i = 0; i < _nodes.Length; i++)
            {
                int stop = i + 1;
                _nodes[i].color = stop < chapter ? HudWidgets.Mint : stop == chapter ? HudWidgets.Sun : HudWidgets.PlumLight;
                _nodes[i].rectTransform.localScale = stop == chapter ? Vector3.one * 1.35f : Vector3.one;
                if (i < _links.Length)
                    _links[i].color = stop < chapter ? HudWidgets.Mint : HudWidgets.PlumLight;
            }

            _shownAt = Time.unscaledTime;
            _group.alpha = 0f;
        }

        /// <summary>Takes the card away at once.</summary>
        public void Hide()
        {
            _shownAt = float.NegativeInfinity;
            if (_group != null)
                _group.alpha = 0f;
        }

        void Update()
        {
            if (_group == null)
                return;

            float alpha = AlphaAt(Time.unscaledTime - _shownAt);
            if (!Mathf.Approximately(alpha, _group.alpha))
                _group.alpha = alpha;
        }
    }
}
