using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ToyFactory.Interfaces;

namespace ToyFactory.UI
{
    /// <summary>
    /// "CHECKPOINT SAVED" with the restarts left, just under the chapter card, when Chapters 2,
    /// 3 and 4 start: the checkpoints after Room 1, Room 2 and Room 3. It fades out with the card.
    /// </summary>
    public sealed class CheckpointToastView : MonoBehaviour
    {
        /// <summary>Seconds the note is on screen, the chapter card's time.</summary>
        public const float TotalSeconds = ChapterCardView.TotalSeconds;

        const float FadeSeconds = 0.6f;

        Canvas _canvas;
        CanvasGroup _group;
        Text _text;
        RectTransform _plate;
        float _shownAt = float.NegativeInfinity;
        bool _pending;

        /// <summary>True while the note is on screen.</summary>
        public bool IsShowing => _canvas != null && _canvas.gameObject.activeSelf;

        /// <summary>The note's text, for tests.</summary>
        public string Text => _text != null ? _text.text : string.Empty;

        /// <summary>The note's plate while it is up, for the layout tests.</summary>
        internal IEnumerable<KeyValuePair<string, RectTransform>> Rects()
        {
            if (_plate != null && IsShowing)
                yield return new KeyValuePair<string, RectTransform>("Checkpoint toast", _plate);
        }

        /// <summary>Builds the note, hidden, under this view's object.</summary>
        internal void Build(int sortingOrder)
        {
            _canvas = HudWidgets.ScreenCanvas("Checkpoint Canvas", transform, sortingOrder);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            Image plate = HudWidgets.Plate("Checkpoint", _canvas.transform);
            _plate = plate.rectTransform;
            HudWidgets.Place(plate.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -396f), new Vector2(640f, 54f));
            _text = HudWidgets.Label("Text", plate.transform, string.Empty, 26, HudWidgets.Mint, TextAnchor.MiddleCenter);

            _canvas.gameObject.SetActive(false);
        }

        void OnEnable() => ChapterEvents.OnChapterStarted += HandleChapterStarted;

        void OnDisable() => ChapterEvents.OnChapterStarted -= HandleChapterStarted;

        // Like the chapter card, the note waits for a cutscene to end.
        void HandleChapterStarted(int chapter)
        {
            if (chapter >= 2)
                _pending = true;
        }

        static bool InCutscene => GameClock.Current != null && GameClock.Current.State == GameState.Cutscene;

        void ShowPending()
        {
            _pending = false;
            IRunControl run = RunControl.Current;
            string restarts = run == null ? string.Empty
                : run.RestartsLeft == 1 ? "   1 RESTART LEFT" : "   " + run.RestartsLeft + " RESTARTS LEFT";
            Show("CHECKPOINT SAVED" + restarts);
        }

        /// <summary>Shows <paramref name="text"/> for <see cref="TotalSeconds"/>.</summary>
        public void Show(string text)
        {
            if (_canvas == null)
                return;
            _text.text = text;
            _shownAt = Time.unscaledTime;
            _group.alpha = 1f;
            _canvas.gameObject.SetActive(true);
        }

        void Update()
        {
            if (_pending && !InCutscene)
                ShowPending();
            if (!IsShowing)
                return;
            float elapsed = Time.unscaledTime - _shownAt;
            if (elapsed >= TotalSeconds)
            {
                _canvas.gameObject.SetActive(false);
                return;
            }
            _group.alpha = Mathf.Clamp01((TotalSeconds - elapsed) / FadeSeconds);
        }
    }
}
