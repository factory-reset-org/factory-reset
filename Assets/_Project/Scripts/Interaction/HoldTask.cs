using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A task done by holding Interact on the prop for a set time: the colour terminal
    /// (6 s, beeping loudly all the while) and the shutdown console (3 s, silent, and only
    /// once the three cores are destroyed). Letting go loses the progress.
    /// </summary>
    public sealed class HoldTask : TaskProp, IHoldable
    {
        [Tooltip("Must equal the task id in the chapter data.")]
        [SerializeField] string taskId = "ch2.terminal";

        [Tooltip("The chapter this task belongs to. Before it, the prop does nothing.")]
        [SerializeField, Range(1, ChapterEvents.ChapterCount)] int chapter = 2;

        [SerializeField, Min(0.1f)] float holdSeconds = 6f;

        [Header("Beeps")]
        [Tooltip("Seconds between beeps while held. 0 = silent.")]
        [SerializeField, Min(0f)] float beepInterval = 0.8f;
        [SerializeField, Min(0f)] float beepLoudness = NoiseLoudness.TerminalBeep;

        [Tooltip("Tasks that must be done before this one can be used (the console needs the three cores).")]
        [SerializeField] TaskProp[] requires = new TaskProp[0];

        float _held;
        float _nextBeep;
        int _lastHoldFrame;

        public override string Id => taskId;

        /// <summary>True if holding it would make progress right now.</summary>
        public bool IsUsable
        {
            get
            {
                if (IsCompleted || !ChapterIsActive(chapter))
                    return false;
                foreach (TaskProp required in requires)
                    if (required != null && !required.IsCompleted)
                        return false;
                return true;
            }
        }

        // The press itself does nothing; the first Hold arrives in the same frame.
        public void Interact()
        {
        }

        public void Hold(float deltaTime)
        {
            if (!IsUsable)
                return;

            _lastHoldFrame = Time.frameCount;
            if (beepInterval > 0f && _held >= _nextBeep)
            {
                EmitNoise(beepLoudness);
                _nextBeep += beepInterval;
            }

            _held += deltaTime;
            if (_held >= holdSeconds)
                Complete();
            else
                ReportProgress(_held / holdSeconds);
        }

        void Update()
        {
            // No Hold for a frame means the player let go or walked off.
            if (_held <= 0f || IsCompleted || Time.frameCount <= _lastHoldFrame + 1)
                return;

            _held = 0f;
            _nextBeep = 0f;
            ReportProgress(0f);
        }
    }
}
