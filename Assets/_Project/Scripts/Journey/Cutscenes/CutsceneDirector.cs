using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Plays the journey's cutscenes. The intro plays by itself the first time the game is
    /// Playing, a restored switch plays its cutscene after a short delay, the console hold
    /// plays the ending, and <see cref="Play"/> plays any other. While one plays the game is in the Cutscene state, so agents and the
    /// game clock stop. Escape skips. The rules live in <see cref="CutsceneRunner"/>; this
    /// component connects them to the scene, the PlayableDirector and the input.
    /// </summary>
    /// <remarks>
    /// Lives in the Agents scene. Critical signals come from <see cref="CriticalSignalMarker"/>s
    /// on the Timelines, which notify this component because it sits on the same object as
    /// the PlayableDirector. A cutscene without a Timeline holds for its placeholder time,
    /// so the journey plays through before the real Timelines exist.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayableDirector))]
    public sealed class CutsceneDirector : MonoBehaviour, ICutscenePlayback, INotificationReceiver
    {
        /// <summary>The director in the loaded Agents scene, or null.</summary>
        public static CutsceneDirector Current { get; private set; }

        [Tooltip("Every cutscene. The ids of the switch cutscenes must match the chapter data (ch2, ch3, ch4).")]
        [SerializeField] CutsceneDefinition[] cutscenes = CutsceneDefinition.JourneyDefaults();

        [Tooltip("Seconds between a switch being restored (or the console finishing) and its cutscene starting.")]
        [Min(0f)]
        [SerializeField] float startDelay = CutsceneRunner.DefaultStartDelay;

        [Tooltip("Let the player skip a playing cutscene with Escape.")]
        [SerializeField] bool allowSkip = true;

        [Tooltip("Play the intro the first time the game is in the Playing state (straight away in scenes without a game clock). Once per run.")]
        [SerializeField] bool playIntroOnStart = true;

        [Tooltip("Id of the cutscene played on start.")]
        [SerializeField] string introId = "intro";

        PlayableDirector _director;
        CutsceneRunner _runner;
        bool _timelineFinished;
        bool _introRequested;

        /// <summary>True once the intro has been asked for this run (it is never asked for twice).</summary>
        public bool IntroRequested => _introRequested;

        /// <summary>True while a cutscene is on screen.</summary>
        public bool IsPlaying => _runner != null && _runner.IsPlaying;

        /// <summary>The cutscene on screen, or null.</summary>
        public string CurrentCutsceneId => _runner?.CurrentId;

        void Awake()
        {
            if (Current != null && Current != this)
                Debug.LogWarning($"More than one {nameof(CutsceneDirector)} is loaded; using the one on {name}.", this);
            Current = this;

            _director = GetComponent<PlayableDirector>();
            _director.playOnAwake = false;
            _director.extrapolationMode = DirectorWrapMode.None;
            _director.stopped += HandleTimelineStopped;

            _runner = new CutsceneRunner(cutscenes, this, () => GameClock.Current);
        }

        void OnEnable()
        {
            ChapterEvents.OnSwitchRestored += HandleSwitchRestored;
            ChapterEvents.OnTaskCompleted += HandleTaskCompleted;
        }

        void OnDisable()
        {
            ChapterEvents.OnSwitchRestored -= HandleSwitchRestored;
            ChapterEvents.OnTaskCompleted -= HandleTaskCompleted;
        }

        void OnDestroy()
        {
            if (_director != null)
                _director.stopped -= HandleTimelineStopped;
            if (Current == this)
                Current = null;
        }

        void Update()
        {
            RequestIntroOnce();
            if (allowSkip && _runner.IsPlaying && SkipPressed())
                _runner.Skip();
            _runner.Tick(Time.deltaTime);
        }

        // The intro waits for the first Playing state, so it starts after the scene loader has
        // finished (and after a title screen, once there is one), and never again in this run:
        // not after the pause menu, not after another cutscene hands the game back.
        void RequestIntroOnce()
        {
            if (!playIntroOnStart || _introRequested)
                return;
            IGameClock clock = GameClock.Current;
            if (clock != null && clock.State != GameState.Playing)
                return;
            _introRequested = true;
            _runner.Request(introId, 0f);
        }

        static bool SkipPressed() =>
            Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        /// <summary>Plays a cutscene by id straight away (the intro). False if the id is unknown or it is already queued.</summary>
        public bool Play(string cutsceneId) => _runner.Request(cutsceneId, 0f);

        /// <summary>Skips the playing cutscene, firing its Critical signals not yet reached.</summary>
        public void Skip() => _runner.Skip();

        void HandleSwitchRestored(int switchNumber) => _runner.RequestForSwitch(switchNumber, startDelay);

        void HandleTaskCompleted(string taskId)
        {
            if (taskId == ChapterEvents.ConsoleTaskId)
                _runner.RequestForTrigger(CutsceneTrigger.ConsoleCompleted, startDelay);
        }

        // ---- Timeline playback, for the runner ----

        bool ICutscenePlayback.Play(CutsceneDefinition cutscene)
        {
            if (cutscene.Timeline == null)
                return false;

            _timelineFinished = false;
            _director.playableAsset = cutscene.Timeline;
            _director.time = 0;
            _director.Play();
            return true;
        }

        void ICutscenePlayback.SetHeld(bool held)
        {
            if (held)
                _director.Pause();
            else
                _director.Resume();
        }

        void ICutscenePlayback.Stop() => _director.Stop();

        bool ICutscenePlayback.IsFinished => _timelineFinished;

        // With the wrap mode set to None, the director stops by itself at the Timeline's end.
        void HandleTimelineStopped(PlayableDirector director) => _timelineFinished = true;

        /// <summary>A Timeline marker was passed; Critical signals are forwarded to the runner.</summary>
        public void OnNotify(Playable origin, INotification notification, object context)
        {
            if (notification is CriticalSignalMarker marker)
                _runner.SignalReached(marker.SignalId);
        }
    }
}
