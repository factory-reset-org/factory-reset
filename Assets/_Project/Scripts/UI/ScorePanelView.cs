using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ToyFactory.UI
{
    /// <summary>
    /// The score, run time and combo panel at the top right. It sits on its own nested canvas, so a
    /// ticking clock rebuilds nothing else, and it only rewrites a text when its value changed.
    /// </summary>
    internal sealed class ScorePanelView
    {
        Text _score, _time, _combo;
        RectTransform _plate;
        int _shownScore = -1;
        int _shownSecond = -1;
        int _shownCombo = -1;

        /// <summary>The score text, for tests.</summary>
        public string ScoreText => _score != null ? _score.text : string.Empty;

        /// <summary>The run time text, for tests.</summary>
        public string TimeText => _time != null ? _time.text : string.Empty;

        /// <summary>The combo text (empty when there is no combo), for tests.</summary>
        public string ComboText => _combo != null ? _combo.text : string.Empty;

        public void Build(Transform parent)
        {
            Image plate = HudWidgets.Plate("Score", parent);
            plate.gameObject.AddComponent<Canvas>();
            _plate = plate.rectTransform;
            HudWidgets.Place(plate.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -32f), new Vector2(340f, 160f));
            Transform p = plate.transform;

            Label(p, "SCORE", HudWidgets.Caption, HudWidgets.InkDim, TextAnchor.MiddleLeft, HudWidgets.Pad, -12f, 160f, 24f);
            _score = Label(p, "0", 56, HudWidgets.Sun, TextAnchor.MiddleLeft, HudWidgets.Pad, -36f, 292f, 66f);
            Label(p, "TIME", HudWidgets.Caption, HudWidgets.InkDim, TextAnchor.MiddleLeft, HudWidgets.Pad, -108f, 70f, 30f);
            _time = Label(p, "0:00", 28, HudWidgets.Ink, TextAnchor.MiddleLeft, 96f, -108f, 100f, 30f);
            _combo = Label(p, string.Empty, HudWidgets.Body + 2, HudWidgets.Sun, TextAnchor.MiddleRight, 196f, -108f, 120f, 30f);
        }

        /// <summary>Redraws from the score manager; zeroes when there is none.</summary>
        public IEnumerable<KeyValuePair<string, RectTransform>> Rects()
        {
            if (_plate != null && _plate.gameObject.activeInHierarchy)
                yield return new KeyValuePair<string, RectTransform>("Score", _plate);
        }

        public void Refresh(ScoreManager score)
        {
            int points = score != null ? score.Score : 0;
            int seconds = score != null ? Mathf.FloorToInt(score.RunSeconds) : 0;
            int combo = score != null ? score.ComboNow : 1;

            if (points != _shownScore)
            {
                _shownScore = points;
                _score.text = points.ToString("N0", CultureInfo.InvariantCulture);
            }

            if (seconds != _shownSecond)
            {
                _shownSecond = seconds;
                _time.text = RunSummary.FormatTime(seconds);
            }

            if (combo != _shownCombo)
            {
                _shownCombo = combo;
                _combo.text = HudMath.ComboText(combo);
            }
        }

        static Text Label(Transform parent, string text, int size, Color colour, TextAnchor anchor, float x, float y, float width, float height)
        {
            Text label = HudWidgets.Label("Text", parent, text, size, colour, anchor);
            HudWidgets.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, height));
            return label;
        }
    }
}
