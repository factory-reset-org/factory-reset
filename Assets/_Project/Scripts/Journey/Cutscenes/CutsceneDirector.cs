using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Timeline;
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
    /// so the journey plays through before the real Timelines exist. When a Timeline starts,
    /// its unbound tracks are bound to objects in other scenes by <see cref="CutsceneBindingId"/>
    /// (<see cref="CutsceneBindings"/>) and the gameplay camera is handed to Cinemachine
    /// (<see cref="CutsceneCameraRig"/>) until the cutscene ends.
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

        // Task id of the keycard Saboteur A drops (Data/Chapters/Tasks/ch3.keycard).
        const string KeycardTaskId = "ch3.keycard";

        [Tooltip("Let the player skip a playing cutscene with Escape.")]
        [SerializeField] bool allowSkip = true;

        [Tooltip("Play the intro the first time the game is in the Playing state (straight away in scenes without a game clock). Once per run.")]
        [SerializeField] bool playIntroOnStart = true;

        [Tooltip("Id of the cutscene played on start.")]
        [SerializeField] string introId = "intro";

        [Tooltip("Plays the voice blips as lines type out. Optional.")]
        [SerializeField] AudioSource blipSource;

        [Tooltip("Blend between cameras that a Timeline does not time itself. Shot blends are set on the Timeline's Cinemachine clips.")]
        [SerializeField] CinemachineBlendDefinition cameraBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.8f);

        PlayableDirector _director;
        CutsceneRunner _runner;
        DialogueRunner _dialogue;
        VoiceBlips _blips;
        CutsceneCameraRig _camera;
        CutsceneCuePlayer _cues;
        readonly List<string> _missingBindings = new List<string>();
        CutsceneDefinition _playing;
        bool _usingTimeline;
        bool _timelineFinished;
        bool _waitingForLines;
        bool _shotOpen;
        readonly HashSet<int> _shotsStarted = new HashSet<int>();
        bool _held;
        bool _saboteurAScrapped;
        bool _keycardCollected;
        bool _introRequested;

        /// <summary>True once the intro has been asked for this run (it is never asked for twice).</summary>
        public bool IntroRequested => _introRequested;

        /// <summary>True while a cutscene is on screen.</summary>
        public bool IsPlaying => _runner != null && _runner.IsPlaying;

        /// <summary>The cutscene on screen, or null.</summary>
        public string CurrentCutsceneId => _runner?.CurrentId;

        /// <summary>The cutscene dialogue, for tests and the debug overlay.</summary>
        public DialogueRunner Dialogue => _dialogue;

        /// <summary>
        /// Raised once per shot as its dialogue marker is reached, with the cutscene id and the
        /// shot index (0 = the first). Cutscene-only actors use it to time their beats, such as
        /// Unit 047's eyes switching on. Not raised for shots cut by a skip.
        /// </summary>
        public event System.Action<string, int> ShotStarted;

        /// <summary>The gameplay camera hand-over, for tests.</summary>
        public CutsceneCameraRig CameraRig => _camera;

        /// <summary>Track names of the playing Timeline that found no object with that binding id.</summary>
        public IReadOnlyList<string> MissingBindings => _missingBindings;

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
            _dialogue = new DialogueRunner(ConditionHolds);
            _camera = new CutsceneCameraRig(transform, cameraBlend);
            _cues = GetComponent<CutsceneCuePlayer>();
            if (blipSource != null)
            {
                _blips = new VoiceBlips(blipSource);
                _dialogue.Typed += _blips.OnTyped;
            }
        }

        void OnEnable()
        {
            ChapterEvents.OnSwitchRestored += HandleSwitchRestored;
            ChapterEvents.OnTaskCompleted += HandleTaskCompleted;
            AgentEvents.OnDestroyed += HandleAgentDestroyed;
            TaskEvents.OnTaskSpawned += HandleTaskSpawned;
            CutsceneEvents.OnCutsceneEnded += HandleCutsceneEnded;
        }

        void OnDisable()
        {
            ChapterEvents.OnSwitchRestored -= HandleSwitchRestored;
            ChapterEvents.OnTaskCompleted -= HandleTaskCompleted;
            AgentEvents.OnDestroyed -= HandleAgentDestroyed;
            TaskEvents.OnTaskSpawned -= HandleTaskSpawned;
            CutsceneEvents.OnCutsceneEnded -= HandleCutsceneEnded;
        }

        void OnDestroy()
        {
            if (_director != null)
                _director.stopped -= HandleTimelineStopped;
            _camera?.Dispose();
            if (Current == this)
                Current = null;
        }

        void Update()
        {
            RequestIntroOnce();
            if (allowSkip && _runner.IsPlaying && SkipPressed())
                _runner.Skip();
            else if (_runner.IsPlaying && AdvancePressed())
                _dialogue.Advance();

            // Dialogue runs in real time while a cutscene plays, and holds with the pause menu.
            if (!_held)
                _dialogue.Tick(Time.deltaTime);

            // A Timeline waiting at a dialogue marker carries on once those lines are said.
            if (_waitingForLines && !_dialogue.IsBusy && !_held)
            {
                _waitingForLines = false;
                HoldTimeline(false);
            }
            // A shot whose lines are over before its end (the player clicked through them) moves
            // the Timeline straight to that end, so the camera never lingers on an empty subtitle.
            else if (_shotOpen && !_dialogue.IsBusy && !_held && !_waitingForLines)
                SkipToShotEnd();

            _runner.Tick(Time.deltaTime);
        }

        // Click, Space or E: finish typing the line, then advance to the next one.
        static bool AdvancePressed() =>
            (Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.eKey.wasPressedThisFrame))
            || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);

        bool ConditionHolds(DialogueCondition condition)
        {
            switch (condition)
            {
                case DialogueCondition.IfSaboteurAActive: return !_saboteurAScrapped && !_keycardCollected;
                case DialogueCondition.IfSaboteurAScrapped: return _saboteurAScrapped && !_keycardCollected;
                case DialogueCondition.IfKeycardCollected: return _keycardCollected;
                default: return true;
            }
        }

        // Saboteur A carries the keycard; the Chapter 3 cutscene words its line by whether it is gone.
        void HandleAgentDestroyed(IAgentState agent)
        {
            if (agent != null && agent.Type == AgentType.Saboteur && agent.Identity.SquadIndex == 0)
                _saboteurAScrapped = true;
        }

        // The keycard is a task created during play (S2's KeycardDrop announces it); once it is
        // picked up, Pip stops telling the player to go and get it.
        void HandleTaskSpawned(ITask task)
        {
            if (task != null && task.Id == KeycardTaskId)
                task.OnCompleted += () => _keycardCollected = true;
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

        // With a Timeline, its dialogue markers start each shot's lines. Without one, but with a
        // dialogue script, every shot is said in order and the cutscene ends with the last line,
        // so a cutscene is watchable before its Timeline exists. With neither, the runner holds
        // for the placeholder time.
        bool ICutscenePlayback.Play(CutsceneDefinition cutscene)
        {
            _playing = cutscene;
            _held = false;
            _waitingForLines = false;
            _shotOpen = false;
            _shotsStarted.Clear();
            _usingTimeline = cutscene.Timeline != null;

            if (_usingTimeline)
            {
                _timelineFinished = false;
                _director.playableAsset = cutscene.Timeline;
                _camera.Begin();
                _missingBindings.Clear();
                _missingBindings.AddRange(CutsceneBindings.Resolve(_director, _camera.Brain));
                if (_missingBindings.Count > 0)
                    Debug.LogWarning($"Cutscene \"{cutscene.Id}\": no loaded object has the binding id of track(s) {string.Join(", ", _missingBindings)}. Those tracks do nothing.", this);
                _director.time = 0;
                _director.Play();
                // Show the first shot this frame: until the Timeline is evaluated the brain would
                // render the gameplay view, inside the Unit 047 stand-in.
                _director.Evaluate();
                return true;
            }

            DialogueScript script = cutscene.Dialogue;
            if (script == null || script.ShotCount == 0)
                return false;
            for (int i = 0; i < script.ShotCount; i++)
                _dialogue.Enqueue(script.LinesOf(i));
            return true;
        }

        void ICutscenePlayback.SetHeld(bool held)
        {
            _held = held;
            if (!_usingTimeline)
                return;
            if (held)
                HoldTimeline(true);
            else if (!_waitingForLines)
                HoldTimeline(false);
        }

        void ICutscenePlayback.Stop()
        {
            _waitingForLines = false;
            _shotOpen = false;
            _dialogue.Clear();
            if (_usingTimeline)
                _director.Stop();
        }

        // Finished once the Timeline (if any) has ended and the last line has been said.
        bool ICutscenePlayback.IsFinished => (!_usingTimeline || _timelineFinished) && !_dialogue.IsBusy;

        // The camera goes back to the player once the cutscene is over (watched or skipped).
        void HandleCutsceneEnded(string cutsceneId) => _camera.End();

        // With the wrap mode set to None, the director stops by itself at the Timeline's end.
        void HandleTimelineStopped(PlayableDirector director) => _timelineFinished = true;

        /// <summary>A Timeline marker was passed; Critical signals are forwarded to the runner.</summary>
        public void OnNotify(Playable origin, INotification notification, object context)
        {
            if (notification is CriticalSignalMarker marker)
            {
                _runner.SignalReached(marker.SignalId);
                return;
            }

            // Show only: an alarm or a comic word. Nothing depends on it.
            if (notification is CutsceneCueMarker cue)
            {
                if (_cues != null)
                    _cues.Play(cue);
                return;
            }

            if (notification is ShotEndMarker)
            {
                _shotOpen = false;
                if (_dialogue.IsBusy)
                {
                    _waitingForLines = true;
                    HoldTimeline(true);
                }
                return;
            }

            // Each shot is said once, even if the playhead is moved back over its marker.
            if (notification is DialogueMarker line && _playing?.Dialogue != null && line.Shot < _playing.Dialogue.ShotCount
                && _shotsStarted.Add(line.Shot))
            {
                ShotStarted?.Invoke(_playing.Id, line.Shot);
                _dialogue.Enqueue(_playing.Dialogue.LinesOf(line.Shot));
                if (line.WaitForLines && _dialogue.IsBusy)
                {
                    _waitingForLines = true;
                    HoldTimeline(true);
                }
                else if (!line.WaitForLines)
                {
                    _shotOpen = true;
                }
            }
        }

        // Holds the Timeline on its current frame without stopping it: the graph keeps being
        // evaluated at speed 0, so the shot camera and every other track stay as they are.
        // PlayableDirector.Pause() stops evaluating, and Cinemachine then loses the shot and
        // shows another camera for as long as the hold lasts.
        void HoldTimeline(bool hold)
        {
            if (_director.playableGraph.IsValid() && _director.playableGraph.GetRootPlayableCount() > 0)
                _director.playableGraph.GetRootPlayable(0).SetSpeed(hold ? 0d : 1d);
        }

        // Moves the playhead to the next shot end. Timeline does not notify markers it jumps
        // over, so they are passed on here in order: a Critical signal inside the shot still
        // fires (the runner ignores one it has already raised).
        void SkipToShotEnd()
        {
            _shotOpen = false;
            if (!(_director.playableAsset is TimelineAsset timeline))
                return;
            double now = _director.time;
            if (!TimelineMarkers.NextShotEnd(timeline, now, out double end))
                return;

            List<IMarker> skipped = TimelineMarkers.Between(timeline, now, end);
            _director.time = end;
            foreach (IMarker marker in skipped)
                if (marker is INotification notification)
                    OnNotify(Playable.Null, notification, null);
        }
    }
}
