using System;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.UI
{
    /// <summary>How a run ended.</summary>
    public enum RunOutcome
    {
        /// <summary>The run has not ended.</summary>
        None,

        /// <summary>The factory was shut down.</summary>
        Won,

        /// <summary>The player's integrity reached 0.</summary>
        Recalled
    }

    /// <summary>
    /// Turns game events into points. It listens, hands each event to <see cref="ScoreRules"/> with
    /// the game time, and keeps the run's numbers for the HUD and the Results screen. It adds no points
    /// of its own: <see cref="ScoreRules"/> is the one source of every value.
    /// </summary>
    /// <remarks>
    /// <para><b>What it hears.</b> Tasks and switches from <see cref="ChapterEvents"/>; takedowns from
    /// <see cref="AgentEvents"/> (a knocked-out Tracker, Guard or Captain, a scrapped Saboteur); the
    /// factory shutting down from <see cref="CutsceneEvents.OnCriticalSignal"/>; the end of the run
    /// from the game state reaching Results. It never reads a brain.</para>
    /// <para><b>The run.</b> It starts when Chapter 1 starts (which also clears the score) and ends at
    /// Results. The run time is game time, which stops in cutscenes and while paused, so the intro and
    /// the pause menu cost nothing. A run that reaches Results after the factory shut down is won and
    /// gets the win bonus; any other is "Recalled" and does not.</para>
    /// <para><b>Not heard yet.</b> The battery pickup and the blaster's shots fired and hit have no
    /// event in <c>Interfaces</c> yet. <see cref="BatteryPickedUp"/> and <see cref="ReportShots"/> are
    /// the entry points for them; until something calls them there are no battery points and the
    /// accuracy bonus is 0.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ScoreManager : MonoBehaviour, IGameStateListener
    {
        /// <summary>The player's full integrity in HP, for the win bonus (the Player prefab's default).</summary>
        public const float FullHp = 100f;

        readonly ScoreRules _rules = new ScoreRules();

        IGameClock _clock;
        GameState _state = GameState.Playing;
        bool _running;
        bool _shutdown;
        float _runStart;
        float _runEnd;
        int _shotsFired;
        int _shotsHit;

        /// <summary>The score manager in the loaded scenes, or null before it wakes.</summary>
        public static ScoreManager Current { get; private set; }

        /// <summary>The rules and the run's numbers (score, combo, history, breakdown).</summary>
        public ScoreRules Rules => _rules;

        /// <summary>The score so far.</summary>
        public int Score => _rules.Score;

        /// <summary>The grade for the score so far.</summary>
        public ScoreGrade Grade => _rules.Grade;

        /// <summary>How the run ended, or <see cref="RunOutcome.None"/> while it is on.</summary>
        public RunOutcome Outcome { get; private set; }

        /// <summary>The highest chapter started this run.</summary>
        public int ChaptersReached { get; private set; }

        /// <summary>True from the start of Chapter 1 until the run ends.</summary>
        public bool IsRunning => _running;

        /// <summary>Game seconds the run has taken so far, or took in all once it ended.</summary>
        public float RunSeconds => !_running && Outcome != RunOutcome.None ? _runEnd - _runStart : _running ? Now - _runStart : 0f;

        /// <summary>Raised for every award of points.</summary>
        public event Action<ScoreAward> Awarded;

        /// <summary>Raised once when the run ends, after any win bonus.</summary>
        public event Action<RunOutcome> RunFinished;

        float Now => _clock != null ? _clock.GameTime : Time.time;

        void Awake()
        {
            if (Current != null && Current != this)
                Debug.LogWarning($"More than one {nameof(ScoreManager)} is loaded; using the one on {name}.", this);

            Current = this;
            _rules.Awarded += HandleAwarded;
        }

        void OnEnable()
        {
            ChapterEvents.OnChapterStarted += HandleChapterStarted;
            ChapterEvents.OnTaskCompleted += HandleTaskCompleted;
            ChapterEvents.OnSwitchRestored += HandleSwitchRestored;
            AgentEvents.OnDisabled += HandleTakedown;
            AgentEvents.OnDestroyed += HandleTakedown;
            CutsceneEvents.OnCriticalSignal += HandleSignal;
        }

        void OnDisable()
        {
            ChapterEvents.OnChapterStarted -= HandleChapterStarted;
            ChapterEvents.OnTaskCompleted -= HandleTaskCompleted;
            ChapterEvents.OnSwitchRestored -= HandleSwitchRestored;
            AgentEvents.OnDisabled -= HandleTakedown;
            AgentEvents.OnDestroyed -= HandleTakedown;
            CutsceneEvents.OnCriticalSignal -= HandleSignal;
            ReleaseClock();
        }

        void OnDestroy()
        {
            _rules.Awarded -= HandleAwarded;
            if (Current == this)
                Current = null;
        }

        void Update() => TrackClock();

        /// <inheritdoc />
        public void OnGameStateChanged(GameState previous, GameState current)
        {
            _state = current;
            if (current == GameState.Results)
                Finish();
        }

        /// <summary>The player picked up a battery: 50 points. For the pickup's owner to call once an event exists.</summary>
        public void BatteryPickedUp()
        {
            if (_running)
                _rules.BatteryPickedUp(Now);
        }

        /// <summary>Sets the run's blaster shots fired and hit, for the accuracy bonus.</summary>
        public void ReportShots(int fired, int hit)
        {
            _shotsFired = Mathf.Max(0, fired);
            _shotsHit = Mathf.Clamp(hit, 0, _shotsFired);
        }

        /// <summary>Starts a run now: clears the score and the run's state.</summary>
        public void BeginRun()
        {
            TrackClock();
            _rules.Reset();
            _running = true;
            _shutdown = false;
            _shotsFired = 0;
            _shotsHit = 0;
            Outcome = RunOutcome.None;
            ChaptersReached = 0;
            _runStart = Now;
            _runEnd = _runStart;
        }

        // ---- Events

        void HandleChapterStarted(int chapter)
        {
            // Chapter 1 begins a run; a score from an earlier run in the same session is cleared.
            if (chapter == 1 && (!_running || ChaptersReached > 0))
                BeginRun();

            if (chapter > ChaptersReached)
                ChaptersReached = chapter;
        }

        void HandleTaskCompleted(string taskId)
        {
            if (_running)
                _rules.TaskCompleted(Now);
        }

        void HandleSwitchRestored(int number)
        {
            if (_running)
                _rules.SwitchRestored(Now);
        }

        void HandleTakedown(IAgentState agent)
        {
            if (_running && agent != null)
                _rules.Takedown(agent.Type, agent.Identity.Id, Now);
        }

        void HandleSignal(string signalId)
        {
            if (signalId == CutsceneSignals.FactoryShutdown)
                _shutdown = true;
        }

        void HandleAwarded(ScoreAward award) => Awarded?.Invoke(award);

        // The run is over: a shut-down factory is a win with its bonus, anything else is a recall.
        void Finish()
        {
            if (!_running)
                return;

            _running = false;
            _runEnd = Now;
            if (_shutdown)
            {
                IPlayerState player = PlayerState.Current;
                bool alive = player != null && !(player is UnityEngine.Object o && o == null);
                float hp = alive ? player.HealthFraction * FullHp : 0f;
                _rules.Win(_runEnd - _runStart, hp, _shotsFired, _shotsHit, _runEnd);
                Outcome = RunOutcome.Won;
            }
            else
            {
                Outcome = RunOutcome.Recalled;
            }

            RunFinished?.Invoke(Outcome);
        }

        // ---- The game clock

        void TrackClock()
        {
            IGameClock current = GameClock.Current;
            if (current is UnityEngine.Object unityObject && unityObject == null)
                current = null;

            if (ReferenceEquals(current, _clock))
                return;

            ReleaseClock();
            _clock = current;
            if (_clock != null)
            {
                _clock.AddListener(this);
                _state = _clock.State;
            }
        }

        void ReleaseClock()
        {
            if (_clock != null && !(_clock is UnityEngine.Object o && o == null))
                _clock.RemoveListener(this);
            _clock = null;
        }
    }
}
