using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using ToyFactory.Interfaces;

namespace ToyFactory.UI
{
    /// <summary>
    /// Shows the title screen while the game is in <see cref="GameState.Title"/>, the Results
    /// screen once it reaches <see cref="GameState.Results"/>, and the pause screen (or the recall
    /// screen, after a recall with a restart left) while <see cref="GameState.Paused"/>. Start asks
    /// the game clock for Playing; Play again reloads the Bootstrap scene; Quit leaves the game; the
    /// pause screen's choices go to <see cref="IRunControl"/>. It also owns the "checkpoint saved"
    /// note. Built by <see cref="HudPresenter"/> in code, like the HUD, so <c>UI.unity</c> needs no
    /// new objects.
    /// </summary>
    /// <remarks>
    /// <para><b>It polls the state</b> instead of listening, so a clock that is replaced (a new
    /// Bootstrap load) is picked up and the order of the listeners never matters. It reads the run from
    /// <see cref="ScoreManager"/> when Results begins; with no manager the screen shows an empty recall.</para>
    /// <para><b>Mouse and keys.</b> Buttons need an event system, and the project has none, so one is
    /// created the first time a screen is shown (the Input System UI module, because the project only
    /// uses the new Input System). The cursor is freed for the screens and locked again on Start.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ScreensPresenter : MonoBehaviour
    {
        const string BootstrapScene = "Bootstrap";

        // The game clock starts in Title and the scene loader sets the real state a frame or so after
        // this scene wakes. Waiting a few frames keeps the title from flashing on a game that is
        // about to be Playing.
        const int TitleSettleFrames = 3;

        [Tooltip("Sorting order of the title and results canvases. They sit above the subtitle bar (50).")]
        [SerializeField] int sortingOrder = 60;

        enum Screen
        {
            None,
            Title,
            Results,
            Pause
        }

        TitleView _title;
        ResultsView _results;
        PauseView _pause;
        CheckpointToastView _checkpointToast;
        bool _pauseShowsRecall;
        Leaderboard _board;
        IGameClock _clock;
        Screen _screen;
        int _titleFrames;

        /// <summary>The title screen, for tests.</summary>
        public TitleView Title => _title;

        /// <summary>The results screen, for tests.</summary>
        public ResultsView Results => _results;

        /// <summary>The pause and recall screen, for tests.</summary>
        public PauseView Pause => _pause;

        /// <summary>The "checkpoint saved" note, for tests.</summary>
        public CheckpointToastView CheckpointToast => _checkpointToast;

        /// <summary>The leaderboard the Results screen reads and writes.</summary>
        public Leaderboard Board => _board;

        /// <summary>Raised when Play again is pressed, before the scene is reloaded. Tests use it to stand in for the reload.</summary>
        public event System.Action PlayAgainPressed;

        /// <summary>Replaces the leaderboard, for tests that point it at a temporary file.</summary>
        public void UseLeaderboard(Leaderboard board) => _board = board ?? new Leaderboard(Leaderboard.DefaultPath);

        void Awake()
        {
            _board = new Leaderboard(Leaderboard.DefaultPath);

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(transform, false);
            _title = titleObject.AddComponent<TitleView>();
            _title.Build(sortingOrder);
            _title.StartRequested += HandleStart;

            var resultsObject = new GameObject("Results");
            resultsObject.transform.SetParent(transform, false);
            _results = resultsObject.AddComponent<ResultsView>();
            _results.Build(sortingOrder);
            _results.PlayAgainRequested += HandlePlayAgain;
            _results.QuitRequested += HandleQuit;

            var pauseObject = new GameObject("Pause");
            pauseObject.transform.SetParent(transform, false);
            _pause = pauseObject.AddComponent<PauseView>();
            _pause.Build(sortingOrder);
            _pause.ResumeRequested += HandleResume;
            _pause.CheckpointRequested += HandleCheckpoint;
            _pause.RestartRequested += HandleRestartGame;
            _pause.GiveUpRequested += HandleGiveUp;

            var toastObject = new GameObject("Checkpoint Toast");
            toastObject.transform.SetParent(transform, false);
            _checkpointToast = toastObject.AddComponent<CheckpointToastView>();
            _checkpointToast.Build(sortingOrder - 15);   // under the screens, over the HUD
        }

        void OnDestroy()
        {
            if (_title != null)
                _title.StartRequested -= HandleStart;
            if (_results != null)
            {
                _results.PlayAgainRequested -= HandlePlayAgain;
                _results.QuitRequested -= HandleQuit;
            }
            if (_pause != null)
            {
                _pause.ResumeRequested -= HandleResume;
                _pause.CheckpointRequested -= HandleCheckpoint;
                _pause.RestartRequested -= HandleRestartGame;
                _pause.GiveUpRequested -= HandleGiveUp;
            }
        }

        void Update()
        {
            IGameClock clock = GameClock.Current;
            if (clock is Object unityObject && unityObject == null)
                clock = null;
            _clock = clock;

            // No clock means no game around the UI (a test scene): show nothing, like the HUD's default.
            GameState state = clock != null ? clock.State : GameState.Playing;
            Screen wanted = state == GameState.Title ? Screen.Title
                : state == GameState.Results ? Screen.Results
                : state == GameState.Paused ? Screen.Pause
                : Screen.None;
            _titleFrames = wanted == Screen.Title ? _titleFrames + 1 : 0;
            if (wanted == Screen.Title && _screen != Screen.Title && _titleFrames < TitleSettleFrames)
                return;

            IRunControl run = RunControl.Current;
            bool recalled = run != null && run.IsRecalled;
            if (wanted != _screen || (wanted == Screen.Pause && recalled != _pauseShowsRecall))
                Switch(wanted);
            else if (_screen == Screen.Pause)
                _pause.Refresh(run);
        }

        void Switch(Screen next)
        {
            _screen = next;
            if (next == Screen.Title)
                _title.Show();
            else
                _title.Hide();

            if (next == Screen.Results)
                _results.Show(CurrentSummary(), _board);
            else
                _results.Hide();

            if (next == Screen.Pause)
            {
                IRunControl run = RunControl.Current;
                _pauseShowsRecall = run != null && run.IsRecalled;
                _pause.Show(_pauseShowsRecall, run);
            }
            else
                _pause.Hide();

            if (next != Screen.None)
            {
                EnsureEventSystem();
                SetCursorLocked(false);
            }
        }

        static RunSummary CurrentSummary()
        {
            ScoreManager score = ScoreManager.Current;
            return score != null
                ? score.CreateSummary()
                : new RunSummary(RunOutcome.Recalled, 0f, 0, 0, 0, 0, 0, ScoreGrade.D, null);
        }

        void HandleStart()
        {
            if (_clock != null)
                _clock.RequestState(GameState.Playing);

            _title.Hide();
            _screen = Screen.None;
            _titleFrames = 0;
            SetCursorLocked(true);
        }

        void HandlePlayAgain()
        {
            PlayAgainPressed?.Invoke();
            Time.timeScale = 1f;
            if (Application.CanStreamedLevelBeLoaded(BootstrapScene))
                SceneManager.LoadScene(BootstrapScene);
        }

        // The pause screen's choices go to the run control; with none (a test scene) they do nothing.
        static void HandleResume() => RunControl.Current?.Resume();

        static void HandleCheckpoint() => RunControl.Current?.RestartFromCheckpoint();

        static void HandleRestartGame() => RunControl.Current?.RestartGame();

        static void HandleGiveUp() => RunControl.Current?.GiveUp();

        static void HandleQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // Buttons and the name field do nothing without an event system.
        void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
                return;

            var go = new GameObject("EventSystem");
            go.transform.SetParent(transform, false);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
