using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ToyFactory.AI.Core;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Debugging;
using ToyFactory.Player;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Managers
{
    /// <summary>
    /// Escape pauses the game while it is being played and resumes it again. While paused,
    /// time stands still and the cursor is free. It is also the run's <see cref="IRunControl"/>:
    /// the restarts from the checkpoint, the recall when the player's integrity runs out, and
    /// starting the game again. The UI draws the pause and recall screens and calls back here
    /// through <see cref="RunControl"/>.
    /// </summary>
    /// <remarks>
    /// <para>Escape also skips a cutscene. A press that did that must not pause as well, so the
    /// game only pauses if it was already being played in the frame before the press.</para>
    /// <para><b>Checkpoints.</b> Each chapter's start is a checkpoint: after Room 1, Room 2 and
    /// Room 3 are done, and the start of the game before that. Going back to it puts the player
    /// at the entrance of the room being played (<see cref="ChapterJump.PlacePlayer"/>) with
    /// full integrity, a full battery and a moment safe from hits, and sends every robot that
    /// has moved or is alert back to its post, powered down for a few seconds
    /// (<see cref="AgentController.SendHome"/>, not a takedown). The room's finished tasks stay
    /// finished and scrapped Saboteurs stay scrapped: the world is not reloaded, because the
    /// props keep their own state.</para>
    /// <para><b>Restarts.</b> A game has <see cref="RestartsPerGame"/>. Going back to the
    /// checkpoint uses one, after a recall or from the pause menu. When the player is recalled
    /// with one left, the game pauses on the recall screen instead of ending; with none left
    /// the run ends at Results as before. Restarting the game reloads Bootstrap, so a new game
    /// begins with every restart again.</para>
    /// </remarks>
    public sealed class PauseMenu : MonoBehaviour, IRunControl
    {
        const string BootstrapScene = "Bootstrap";

        [Tooltip("Restarts from the checkpoint a game starts with.")]
        [SerializeField, Min(0)] int restartsPerGame = 3;

        [Tooltip("Game seconds the player is safe from hits after going back to the checkpoint.")]
        [SerializeField, Min(0f)] float safeSeconds = 2f;

        [Tooltip("Seconds the robots stay powered down at their posts after a restart from the checkpoint.")]
        [SerializeField, Min(0f)] float robotsDownSeconds = 3f;

        [Tooltip("A robot further than this from its post, in metres, is sent back on a restart. One at its post and calm is left alone.")]
        [SerializeField, Min(0f)] float homeTolerance = 1f;

        bool _wasPlaying;
        bool _stoppedTime;
        int _restartsLeft;

        /// <inheritdoc />
        public int RestartsPerGame => restartsPerGame;

        /// <inheritdoc />
        public int RestartsLeft => _restartsLeft;

        /// <inheritdoc />
        public bool IsRecalled { get; private set; }

        /// <inheritdoc />
        public string CheckpointName
        {
            get
            {
                ChapterDefinition chapter = ChapterManager.Current != null && ChapterManager.Current.Flow != null
                    ? ChapterManager.Current.Flow.CurrentDefinition
                    : null;
                return chapter != null && !string.IsNullOrEmpty(chapter.Area) ? chapter.Area : "the start";
            }
        }

        void Awake()
        {
            _restartsLeft = restartsPerGame;
            RunControl.Publish(this);
        }

        void OnDestroy()
        {
            if (ReferenceEquals(RunControl.Current, this))
                RunControl.Publish(null);
        }

        void Update()
        {
            GameManager game = GameManager.Instance;
            if (game == null)
                return;

            // The recall screen waits for a choice; Escape does not close it.
            if (!IsRecalled && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (game.State == GameState.Paused)
                    Resume();
                else if (game.State == GameState.Playing && _wasPlaying)
                    Pause();
            }

            _wasPlaying = game.State == GameState.Playing;
        }

        public void Pause()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.State != GameState.Playing)
                return;

            game.SetState(GameState.Paused);
            StopTime();
        }

        /// <inheritdoc />
        public void Resume()
        {
            GameManager game = GameManager.Instance;
            if (IsRecalled || game == null || game.State != GameState.Paused)
                return;

            RestoreTime();
            game.Resume();
            SetCursorLocked(true);
        }

        /// <inheritdoc />
        public bool OfferRestart()
        {
            GameManager game = GameManager.Instance;
            if (_restartsLeft <= 0 || game == null)
                return false;

            IsRecalled = true;
            game.SetState(GameState.Paused);
            StopTime();
            return true;
        }

        /// <inheritdoc />
        public bool RestartFromCheckpoint()
        {
            GameManager game = GameManager.Instance;
            if (_restartsLeft <= 0 || game == null || game.State != GameState.Paused)
                return false;

            _restartsLeft--;
            IsRecalled = false;
            BackToCheckpoint();
            RestoreTime();
            game.Resume();
            SetCursorLocked(true);
            GameSfx.Play(Sfx.Mission);
            return true;
        }

        /// <inheritdoc />
        public void RestartGame()
        {
            IsRecalled = false;
            RestoreTime();
            Time.timeScale = 1f;
            if (Application.CanStreamedLevelBeLoaded(BootstrapScene))
                SceneManager.LoadScene(BootstrapScene);
        }

        /// <inheritdoc />
        public void GiveUp()
        {
            GameManager game = GameManager.Instance;
            if (!IsRecalled || game == null)
                return;

            IsRecalled = false;
            RestoreTime();
            game.SetState(GameState.Results);
        }

        void BackToCheckpoint()
        {
            ChapterFlow flow = ChapterManager.Current != null ? ChapterManager.Current.Flow : null;
            int chapter = flow != null && flow.HasBegun ? Mathf.Max(1, flow.CurrentChapter) : 1;

            ChapterJump entrances = FindAnyObjectByType<ChapterJump>();
            if (entrances != null)
                entrances.PlacePlayer(chapter);
            else
                Debug.LogWarning("PauseMenu: no ChapterJump is loaded, so the player stays where they were recalled.", this);

            if (PlayerState.Current is Component player && player != null)
            {
                if (player.TryGetComponent(out PlayerHealth health))
                    health.Revive(safeSeconds);
                if (player.TryGetComponent(out PlayerBattery battery))
                    battery.RefillAll();
            }

            if (AgentSpawner.Instance == null)
                return;
            foreach (AgentController agent in AgentSpawner.Instance.SpawnedAgents)
            {
                if (agent == null || agent.IsDead)
                    continue;
                bool away = (agent.transform.position - agent.HomePosition).sqrMagnitude > homeTolerance * homeTolerance;
                if (away || agent.Alert != AlertLevel.None)
                    agent.SendHome(robotsDownSeconds);
            }
        }

        void StopTime()
        {
            Time.timeScale = 0f;
            _stoppedTime = true;
            SetCursorLocked(false);
        }

        // Time must never be left stopped if the menu goes away while the game is paused.
        void OnDisable() => RestoreTime();

        void RestoreTime()
        {
            if (!_stoppedTime)
                return;
            _stoppedTime = false;
            Time.timeScale = 1f;
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
