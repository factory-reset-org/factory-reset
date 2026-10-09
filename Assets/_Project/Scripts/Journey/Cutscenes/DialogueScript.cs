using System;
using UnityEngine;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>When a line is said. Most lines always are; a few depend on the game so far.</summary>
    public enum DialogueCondition
    {
        Always,

        /// <summary>Only while Saboteur A (the keycard carrier) is still active.</summary>
        IfSaboteurAActive,

        /// <summary>Only once Saboteur A has been scrapped and its keycard is still on the floor.</summary>
        IfSaboteurAScrapped,

        /// <summary>Only once the keycard Saboteur A dropped has been picked up.</summary>
        IfKeycardCollected
    }

    /// <summary>One line: who says it, what they say, and when.</summary>
    [Serializable]
    public struct DialogueLine
    {
        public DialogueSpeaker speaker;
        [TextArea(1, 3)] public string text;
        public DialogueCondition condition;

        public DialogueLine(DialogueSpeaker speaker, string text, DialogueCondition condition = DialogueCondition.Always)
        {
            this.speaker = speaker;
            this.text = text;
            this.condition = condition;
        }
    }

    /// <summary>
    /// The lines of one cutscene, in order, grouped into shots so a Timeline can start each
    /// shot's lines when its camera cuts in. Lives in Data/Dialogue; the words come from
    /// Docs/Story.md, which must be updated whenever a line changes.
    /// </summary>
    [CreateAssetMenu(menuName = "Factory Reset/Dialogue Script", fileName = "DialogueScript")]
    public sealed class DialogueScript : ScriptableObject
    {
        /// <summary>The lines of one shot.</summary>
        [Serializable]
        public struct Shot
        {
            public DialogueLine[] lines;
        }

        [SerializeField] Shot[] shots = new Shot[0];

        /// <summary>Number of shots.</summary>
        public int ShotCount => shots.Length;

        /// <summary>The lines of shot <paramref name="index"/> (0-based).</summary>
        public DialogueLine[] LinesOf(int index) => shots[index].lines ?? new DialogueLine[0];

        /// <summary>Replaces the shots. For the builder and tests.</summary>
        public void SetShots(Shot[] newShots) => shots = newShots ?? new Shot[0];

        /// <summary>A script built in code, for tests.</summary>
        public static DialogueScript Create(params DialogueLine[][] shotLines)
        {
            var script = CreateInstance<DialogueScript>();
            var built = new Shot[shotLines.Length];
            for (int i = 0; i < shotLines.Length; i++)
                built[i] = new Shot { lines = shotLines[i] };
            script.shots = built;
            return script;
        }
    }
}
