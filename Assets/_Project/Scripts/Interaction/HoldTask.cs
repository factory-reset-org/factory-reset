using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A task done by holding Interact on the prop for a set time: the colour terminal
    /// (6 s, beeping loudly all the while) and the shutdown console (3 s, silent, and only
    /// once the three cores are destroyed). Letting go loses the progress.
    /// </summary>
    /// <remarks>
    /// While it is on, its glow grows with the progress; each beep of the terminal is a
    /// comic word and a tick; when it completes the glow turns green.
    /// </remarks>
    public sealed class HoldTask : TaskProp, IHoldable
    {
        static readonly string[] BeepWords = { "beep", "boop", "bzzt" };
        static readonly Color BlueGlow = new Color(0.23f, 0.61f, 1f);
        static readonly Color DoneGlow = new Color(0.24f, 0.86f, 0.69f);

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
        PropGlow _glow;

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

        void Start()
        {
            bool terminal = beepInterval > 0f;
            _glow = PropGlow.Attach(gameObject, terminal ? BlueGlow : new Color(1f, 0.2f, 0.33f),
                terminal ? 2f : 2.4f, terminal ? 0.5f : 0.5f, 0.2f, 4f, new Vector3(0f, 0.8f, 0f));
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
                PropEffects.Word(BeepWords[Random.Range(0, BeepWords.Length)], transform.position + Vector3.up * 2f);
                GameSfx.Play(Sfx.Tick);
                _nextBeep += beepInterval;
            }

            _held += deltaTime;
            if (_held >= holdSeconds)
            {
                Complete();
                if (beepInterval > 0f)
                    PropEffects.Word("hacked", transform.position + Vector3.up * 2.4f);
                GameSfx.Play(Sfx.Switch);
            }
            else
            {
                ReportProgress(_held / holdSeconds);
            }
        }

        void Update()
        {
            ShowProgress();

            // No Hold for a frame means the player let go or walked off.
            if (_held <= 0f || IsCompleted || Time.frameCount <= _lastHoldFrame + 1)
                return;

            _held = 0f;
            _nextBeep = 0f;
            ReportProgress(0f);
        }

        // Dim while off, brighter while it is the task, brightest as the hold nears its end.
        void ShowProgress()
        {
            if (_glow == null)
                return;

            if (IsCompleted)
            {
                _glow.Colour = DoneGlow;
                _glow.Intensity = 0.8f;
                return;
            }
            _glow.Intensity = IsUsable ? 0.6f + 0.4f * Mathf.Clamp01(_held / holdSeconds) : 0.25f;
        }
    }
}
