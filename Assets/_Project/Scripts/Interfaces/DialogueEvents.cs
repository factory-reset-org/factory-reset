using System;
using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// One line of cutscene dialogue as the subtitles should show it right now: the speaker's
    /// name tag and colour, the whole line, and how many characters have been typed so far.
    /// </summary>
    public readonly struct DialogueLineView
    {
        /// <summary>Name tag shown before the line, e.g. "PIP, maintenance radio".</summary>
        public string SpeakerTag { get; }

        /// <summary>Colour of the name tag (from the cast table in Story.md).</summary>
        public Color TagColour { get; }

        /// <summary>The whole line.</summary>
        public string Text { get; }

        /// <summary>How many characters of <see cref="Text"/> are typed out so far.</summary>
        public int VisibleCharacters { get; }

        /// <summary>True once the whole line is typed out.</summary>
        public bool IsComplete => VisibleCharacters >= (Text?.Length ?? 0);

        /// <summary>The part of the line typed out so far, for the subtitle text.</summary>
        public string VisibleText => Text == null ? string.Empty : Text.Substring(0, Mathf.Min(VisibleCharacters, Text.Length));

        public DialogueLineView(string speakerTag, Color tagColour, string text, int visibleCharacters)
        {
            SpeakerTag = speakerTag;
            TagColour = tagColour;
            Text = text;
            VisibleCharacters = visibleCharacters;
        }
    }

    /// <summary>
    /// Cutscene dialogue for the subtitles. Only S4's dialogue runner raises these; S3's UI
    /// listens and draws the subtitle bar, so neither side references the other.
    /// </summary>
    /// <remarks>
    /// <see cref="OnLineShown"/> is raised when a line starts and again each time more of it is
    /// typed (about 38 characters per second), so a listener only has to redraw what it is given.
    /// </remarks>
    public static class DialogueEvents
    {
        /// <summary>A line started, or more of it was typed out.</summary>
        public static event Action<DialogueLineView> OnLineShown;

        /// <summary>The subtitle should disappear: the dialogue ended or the cutscene was skipped.</summary>
        public static event Action OnLineCleared;

        public static void RaiseLineShown(in DialogueLineView view) => OnLineShown?.Invoke(view);

        public static void RaiseLineCleared() => OnLineCleared?.Invoke();

        // Domain reload is off in this project, so static listeners must not survive into the
        // next play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearListeners()
        {
            OnLineShown = null;
            OnLineCleared = null;
        }
    }
}
