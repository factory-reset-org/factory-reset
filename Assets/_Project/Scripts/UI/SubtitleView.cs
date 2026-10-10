using UnityEngine;
using UnityEngine.UI;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.UI
{
    /// <summary>
    /// The subtitle bar for cutscene dialogue. It draws what <see cref="DialogueEvents"/> says and
    /// nothing more: the words, their timing and when they clear are the cutscene director's.
    /// </summary>
    /// <remarks>
    /// Sorts above the cutscene letterbox, so the bar is readable over the black bands. The first time
    /// a line is shown it switches off the director's <see cref="PlaceholderSubtitles"/>, which exists
    /// only until this view does, so the line is not drawn twice.
    /// </remarks>
    public sealed class SubtitleView : MonoBehaviour
    {
        Canvas _canvas;
        Text _text;
        bool _placeholderChecked;

        /// <summary>True while a line is on screen.</summary>
        public bool IsShowing => _canvas != null && _canvas.enabled;

        /// <summary>The text on screen, with its rich-text tags; empty when nothing shows.</summary>
        public string DisplayedText => _text != null ? _text.text : string.Empty;

        /// <summary>The rich text for a line: the speaker's tag in its colour, then what has been typed so far.</summary>
        public static string Format(in DialogueLineView line) =>
            $"<b><color=#{ColorUtility.ToHtmlStringRGB(line.TagColour)}>{line.SpeakerTag}</color></b>  {line.VisibleText}";

        /// <summary>Builds the bar's objects. Call once, before the view is enabled.</summary>
        internal void Build(int sortingOrder)
        {
            _canvas = HudWidgets.ScreenCanvas("Subtitle Canvas", transform, sortingOrder);
            _canvas.enabled = false;

            var panel = HudWidgets.Panel("Bar", _canvas.transform, HudWidgets.PlumPanel);
            HudWidgets.Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 140f), new Vector2(1280f, 120f));

            _text = HudWidgets.Label("Line", panel.transform, string.Empty, 34, HudWidgets.Ink, TextAnchor.MiddleLeft, FontStyle.Normal);
            _text.supportRichText = true;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.rectTransform.offsetMin = new Vector2(28f, 10f);
            _text.rectTransform.offsetMax = new Vector2(-28f, -10f);
        }

        void OnEnable()
        {
            DialogueEvents.OnLineShown += HandleShown;
            DialogueEvents.OnLineCleared += HandleCleared;
        }

        void OnDisable()
        {
            DialogueEvents.OnLineShown -= HandleShown;
            DialogueEvents.OnLineCleared -= HandleCleared;
            if (_canvas != null)
                _canvas.enabled = false;
        }

        void HandleShown(DialogueLineView line)
        {
            if (_canvas == null)
                return;

            if (!_placeholderChecked)
            {
                _placeholderChecked = true;
                PlaceholderSubtitles placeholder = FindFirstObjectByType<PlaceholderSubtitles>();
                if (placeholder != null)
                    placeholder.enabled = false;
            }

            _text.text = Format(line);
            _canvas.enabled = true;
        }

        void HandleCleared()
        {
            if (_canvas == null)
                return;

            _canvas.enabled = false;
            _text.text = string.Empty;
        }
    }
}
