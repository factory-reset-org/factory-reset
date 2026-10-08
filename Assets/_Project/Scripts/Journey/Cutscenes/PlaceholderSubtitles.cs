using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// A plain subtitle bar for cutscene dialogue, drawn until S3's UI scene draws the real one.
    /// It only listens to <see cref="DialogueEvents"/>, exactly as the UI will, so switching to
    /// the real subtitles is a matter of turning this component off.
    /// </summary>
    public sealed class PlaceholderSubtitles : MonoBehaviour
    {
        DialogueLineView _line;
        bool _showing;
        GUIStyle _style;

        void OnEnable()
        {
            DialogueEvents.OnLineShown += Show;
            DialogueEvents.OnLineCleared += Hide;
        }

        void OnDisable()
        {
            DialogueEvents.OnLineShown -= Show;
            DialogueEvents.OnLineCleared -= Hide;
            _showing = false;
        }

        void Show(DialogueLineView line)
        {
            _line = line;
            _showing = true;
        }

        void Hide() => _showing = false;

        void OnGUI()
        {
            if (!_showing)
                return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.box) { fontSize = 18, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft };

            float width = Mathf.Min(900f, Screen.width - 40f);
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - 120f, width, 90f);
            string tag = ColorUtility.ToHtmlStringRGB(_line.TagColour);
            GUI.Box(rect, $"<b><color=#{tag}>{_line.SpeakerTag}</color></b>\n{_line.VisibleText}", _style);
        }
    }
}
