using UnityEngine;
using UnityEngine.UI;

namespace ToyFactory.UI
{
    /// <summary>
    /// The prompt line (such as "Press E to use the lever") and the hold bar under the crosshair. The
    /// prompt text is set by whatever knows what the player is looking at; the bar follows the progress
    /// of the hold tasks. Both are hidden when there is nothing to show.
    /// </summary>
    internal sealed class PromptView
    {
        Text _prompt;
        RectTransform _bar;
        Image _fill;
        string _text = string.Empty;
        float _progress;

        /// <summary>The prompt text, empty when none is shown.</summary>
        public string Prompt => _text;

        /// <summary>True while the hold bar is drawn.</summary>
        public bool BarVisible => _bar != null && _bar.gameObject.activeSelf;

        /// <summary>The fraction the hold bar shows.</summary>
        public float BarFraction => _fill != null ? _fill.rectTransform.anchorMax.x : 0f;

        public void Build(Transform parent)
        {
            _prompt = HudWidgets.Label("Prompt", parent, string.Empty, 28, HudWidgets.Ink, TextAnchor.MiddleCenter);
            HudWidgets.Place(_prompt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -86f), new Vector2(900f, 40f));
            var shadow = _prompt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);
            _prompt.gameObject.SetActive(false);

            Image track = HudWidgets.Plate("Hold bar", parent);
            _bar = track.rectTransform;
            HudWidgets.Place(_bar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -132f), new Vector2(280f, 18f));
            _fill = HudWidgets.Panel("Fill", track.transform, HudWidgets.Sun);
            HudWidgets.SetFill(_fill, 0f);
            _bar.gameObject.SetActive(false);
        }

        /// <summary>Sets the prompt line; null or empty hides it.</summary>
        public void SetPrompt(string text)
        {
            text = text ?? string.Empty;
            if (text == _text)
                return;

            _text = text;
            _prompt.text = text;
            _prompt.gameObject.SetActive(text.Length > 0);
        }

        /// <summary>Sets the hold progress in [0, 1]; the bar shows while it is under way.</summary>
        public void SetHold(float progress)
        {
            progress = HudMath.Fraction(progress);
            bool visible = HudMath.HoldBarVisible(progress);
            if (visible != _bar.gameObject.activeSelf)
                _bar.gameObject.SetActive(visible);

            if (visible && !Mathf.Approximately(progress, _progress))
                HudWidgets.SetFill(_fill, progress);
            _progress = progress;
        }
    }
}
