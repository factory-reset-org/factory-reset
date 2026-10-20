using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ToyFactory.Interaction;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Journey.Debugging;
using ToyFactory.Player;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Effects;
using ToyFactory.UI;

namespace ToyFactory.Managers
{
    /// <summary>
    /// Escape pauses the game while it is being played and resumes it again. While paused,
    /// time stands still and the cursor is free. It is also the run's <see cref="IRunControl"/>:
    /// the checkpoints, the restarts, the recall when the player's integrity runs out, and
    /// starting the game again. The UI draws the pause and recall screens and calls back here
    /// through <see cref="RunControl"/>.
    /// </summary>
    /// <remarks>
    /// <para>Escape also skips a cutscene. A press that did that must not pause as well, so the
    /// game only pauses if it was already being played in the frame before the press.</para>
    /// <para><b>Checkpoints.</b> Each chapter's start is a checkpoint: after Room 1, Room 2 and
    /// Room 3 are done, and the start of the game before that. When a chapter starts, the run's
    /// numbers (<see cref="ScoreManager.SaveCheckpoint"/>) and the Saboteurs already scrapped
    /// are kept.</para>
    /// <para><b>Going back</b> puts the room exactly as it was when its chapter began. The
    /// props keep their own state (a relay switched on, a crate on its plate, a fuse picked up),
    /// so the scenes are reloaded, and behind a cover the journey is taken forward to the
    /// checkpoint's chapter the way the game finishes a chapter (<see cref="ChapterJump"/>).
    /// The run's seed is handed to the reloaded chapter manager (<see cref="ChapterManager.NextRunSeed"/>),
    /// so the relay order is the one the board showed before. Then the kept numbers are put back:
    /// the score, the Saboteurs scrapped before the checkpoint, the restarts left, and the run
    /// time, which carries on. The player starts at
    /// the room's entrance with full integrity and a moment safe from hits. The rooms already
    /// done are finished in the chapter flow but their props are fresh, as with the debug
    /// chapter keys; only the room being played matters.</para>
    /// <para><b>Restarts.</b> A game has <see cref="RestartsPerGame"/>. Going back to the
    /// checkpoint uses one, after a recall or from the pause menu. When the player is recalled
    /// with one left, the game pauses on the recall screen instead of ending; with none left
    /// the run ends at Results as before. Restarting the game reloads Bootstrap to the title,
    /// so a new game begins with every restart again.</para>
    /// </remarks>
    public sealed class PauseMenu : MonoBehaviour, IRunControl
    {
        const string BootstrapScene = "Bootstrap";

        // Everything a restart carries over the reload. Static because the scenes, and this
        // menu with them, are replaced.
        sealed class Checkpoint
        {
            public int Chapter = 1;
            public ScoreCheckpoint Score;   // null at the start of the game: the run begins afresh
            public readonly List<char> ScrappedSaboteurs = new List<char>();
            // Every task done by then, including pickups for later chapters collected early (the keycard).
            public readonly List<string> CompletedTasks = new List<string>();
        }

        sealed class PendingRestore
        {
            public Checkpoint Checkpoint;
            public int RestartsLeft;
            public float RunSeconds;
        }

        static PendingRestore s_pending;
        static GameObject s_cover;

        [Tooltip("Restarts from the checkpoint a game starts with.")]
        [SerializeField, Min(0)] int restartsPerGame = 3;

        [Tooltip("Game seconds the player is safe from hits after going back to the checkpoint.")]
        [SerializeField, Min(0f)] float safeSeconds = 2f;

        [Tooltip("Real seconds to wait for the level, or for a chapter, while restoring, before giving up.")]
        [SerializeField, Min(5f)] float restoreTimeout = 40f;

        bool _wasPlaying;
        bool _stoppedTime;
        int _restartsLeft;
        Checkpoint _checkpoint = new Checkpoint();

        /// <inheritdoc />
        public int RestartsPerGame => restartsPerGame;

        /// <inheritdoc />
        public int RestartsLeft => _restartsLeft;

        /// <inheritdoc />
        public bool IsRecalled { get; private set; }

        /// <inheritdoc />
        public bool IsRestoring { get; private set; }

        /// <summary>The chapter the player goes back to.</summary>
        public int CheckpointChapter => _checkpoint.Chapter;

        /// <inheritdoc />
        public string CheckpointName
        {
            get
            {
                ChapterFlow flow = ChapterManager.Current != null ? ChapterManager.Current.Flow : null;
                ChapterDefinition chapter = flow != null && flow.HasBegun ? flow.GetDefinition(_checkpoint.Chapter) : null;
                return chapter != null && !string.IsNullOrEmpty(chapter.Area) ? chapter.Area : "the start";
            }
        }

        void Awake()
        {
            _restartsLeft = restartsPerGame;
            RunControl.Publish(this);
        }

        void OnEnable() => ChapterEvents.OnChapterStarted += HandleChapterStarted;

        void OnDisable()
        {
            ChapterEvents.OnChapterStarted -= HandleChapterStarted;
            RestoreTime();   // time must never be left stopped if the menu goes away while paused
        }

        void Start()
        {
            PendingRestore pending = s_pending;
            s_pending = null;
            if (pending != null)
                StartCoroutine(Restore(pending));
            else
                HideCover();
        }

        void OnDestroy()
        {
            if (ReferenceEquals(RunControl.Current, this))
                RunControl.Publish(null);
        }

        // Domain reload is off in this project: forget a restore left over from the last session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOnLoad()
        {
            s_pending = null;
            s_cover = null;
        }

        void Update()
        {
            GameManager game = GameManager.Instance;
            if (game == null)
                return;

            // The recall screen waits for a choice and a restore runs to the end; Escape leaves both.
            if (!IsRecalled && !IsRestoring && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (game.State == GameState.Paused)
                    Resume();
                else if (game.State == GameState.Playing && _wasPlaying)
                    Pause();
            }

            _wasPlaying = game.State == GameState.Playing;
        }

        // ---- Pause

        public void Pause()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.State != GameState.Playing || IsRestoring)
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

        // ---- Checkpoints and restarts

        // A chapter's start is a checkpoint. The chapters a restore passes through on its way are not.
        void HandleChapterStarted(int chapter)
        {
            if (IsRestoring)
                return;

            var checkpoint = new Checkpoint { Chapter = Mathf.Max(1, chapter) };
            if (chapter > 1 && ScoreManager.Current != null)
                checkpoint.Score = ScoreManager.Current.SaveCheckpoint();
            if (AgentSpawner.Instance != null)
                foreach (AgentController agent in AgentSpawner.Instance.SpawnedAgents)
                    if (agent != null && agent.IsDead && agent.Type == AgentType.Saboteur)
                        checkpoint.ScrappedSaboteurs.Add(agent.Identity.SquadLetter);
            ChapterFlow flow = ChapterManager.Current != null ? ChapterManager.Current.Flow : null;
            if (flow != null)
                foreach (string task in AllTasks(flow))
                    if (flow.IsTaskComplete(task))
                        checkpoint.CompletedTasks.Add(task);
            _checkpoint = checkpoint;
        }

        static IEnumerable<string> AllTasks(ChapterFlow flow)
        {
            for (int chapter = 1; chapter <= flow.ChapterCount; chapter++)
                foreach (TaskDefinition task in flow.GetDefinition(chapter).Tasks)
                    yield return task.TaskId;
        }

        // A pickup collected before its chapter (the keycard off Saboteur A) counts when its
        // chapter comes. The journey's jump only finishes the chapters before the checkpoint,
        // so such a task is completed again here, and the pickup that was dropped again on the
        // re-scrap is taken away.
        static void CompleteEarlyTasks(Checkpoint checkpoint)
        {
            ChapterFlow flow = ChapterManager.Current != null ? ChapterManager.Current.Flow : null;
            if (flow == null)
                return;
            foreach (string task in checkpoint.CompletedTasks)
            {
                if (flow.IsTaskComplete(task))
                    continue;
                flow.CompleteTask(task);
                foreach (Collectible pickup in FindObjectsByType<Collectible>(FindObjectsInactive.Exclude))
                    if (pickup.Id == task)
                        pickup.gameObject.SetActive(false);
            }
        }

        /// <inheritdoc />
        public bool OfferRestart()
        {
            GameManager game = GameManager.Instance;
            if (_restartsLeft <= 0 || game == null || IsRestoring)
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
            if (_restartsLeft <= 0 || game == null || game.State != GameState.Paused || IsRestoring)
                return false;

            _restartsLeft--;
            IsRecalled = false;
            s_pending = new PendingRestore
            {
                Checkpoint = _checkpoint,
                RestartsLeft = _restartsLeft,
                RunSeconds = ScoreManager.Current != null ? ScoreManager.Current.RunSeconds : 0f
            };
            // The same seed picks the same relay order, so the order the board showed before the
            // restart, and the objective ring follows, is still the order after it.
            ChapterManager.NextRunSeed = ChapterManager.Current != null ? ChapterManager.Current.SequenceSeed : (int?)null;
            ShowCover(CheckpointName);
            Reload();
            return true;
        }

        /// <inheritdoc />
        public void RestartGame()
        {
            IsRecalled = false;
            s_pending = null;
            ChapterManager.NextRunSeed = null;   // a new game picks a new order
            Reload();
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

        void Reload()
        {
            RestoreTime();
            Time.timeScale = 1f;
            if (Application.CanStreamedLevelBeLoaded(BootstrapScene))
                SceneManager.LoadScene(BootstrapScene);
        }

        // Runs in the reloaded game: takes the journey to the checkpoint's chapter, then puts the run back.
        IEnumerator Restore(PendingRestore pending)
        {
            IsRestoring = true;
            _restartsLeft = pending.RestartsLeft;
            Checkpoint checkpoint = pending.Checkpoint;

            float until = Time.realtimeSinceStartup + restoreTimeout;
            ChapterJump jump = null;
            while (!LevelReady(ref jump))
            {
                if (Time.realtimeSinceStartup > until)
                {
                    FailRestore("the level did not load");
                    yield break;
                }
                yield return null;
            }

            // Nothing may hurt the player while the journey is taken forward.
            PlayerHealth health = PlayerState.Current is Component player && player != null ? player.GetComponent<PlayerHealth>() : null;
            if (health != null)
                health.Revive(3600f);

            GameManager.Instance.SetState(GameState.Playing);
            yield return null;

            if (checkpoint.Chapter > 1)
            {
                jump.JumpTo(checkpoint.Chapter);
                yield return null;
            }
            else
            {
                // The intro starts with play; skip it as Escape would.
                float introBy = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < introBy && !IsCutscenePlaying())
                    yield return null;
            }

            until = Time.realtimeSinceStartup + restoreTimeout;
            while (jump.IsJumping || IsCutscenePlaying() || GameManager.Instance.State != GameState.Playing)
            {
                if (!jump.IsJumping && IsCutscenePlaying())
                    CutsceneDirector.Current.Skip();
                if (Time.realtimeSinceStartup > until)
                {
                    FailRestore("the journey did not reach chapter " + checkpoint.Chapter);
                    yield break;
                }
                yield return null;
            }

            // The Saboteurs scrapped before the checkpoint stay scrapped.
            foreach (AgentController agent in AgentSpawner.Instance.SpawnedAgents)
                if (agent != null && !agent.IsDead && agent.Type == AgentType.Saboteur
                    && checkpoint.ScrappedSaboteurs.Contains(agent.Identity.SquadLetter))
                    agent.Scrap();
            CompleteEarlyTasks(checkpoint);

            // After the scraps and tasks, so the points they raise are replaced by the kept score.
            if (checkpoint.Score != null && ScoreManager.Current != null)
                ScoreManager.Current.RestoreCheckpoint(checkpoint.Score, pending.RunSeconds);

            jump.PlacePlayer(checkpoint.Chapter);
            if (health != null)
                health.Revive(safeSeconds);

            _checkpoint = checkpoint;
            IsRestoring = false;
            HideCover();
            SetCursorLocked(true);
            GameSfx.Play(Sfx.Mission);
        }

        static bool LevelReady(ref ChapterJump jump)
        {
            if (jump == null)
                jump = FindAnyObjectByType<ChapterJump>();
            ChapterManager chapters = ChapterManager.Current;
            return jump != null && GameManager.Instance != null && GameManager.Instance.State == GameState.Title
                   && chapters != null && chapters.Flow != null && chapters.Flow.HasBegun
                   && AgentSpawner.Instance != null && AgentSpawner.Instance.SpawnedAgents.Count > 0
                   && PlayerState.Current != null && ScoreManager.Current != null;
        }

        static bool IsCutscenePlaying() => CutsceneDirector.Current != null && CutsceneDirector.Current.IsPlaying;

        // Better a fresh game than a half-restored one.
        void FailRestore(string why)
        {
            Debug.LogError("PauseMenu: could not go back to the checkpoint, because " + why + ". Starting the game again.", this);
            IsRestoring = false;
            HideCover();
            if (GameManager.Instance != null)
                GameManager.Instance.SetState(GameState.Title);
        }

        // ---- The cover over the reload

        // It lives outside the scenes so it stays up from the old game to the restored one.
        static void ShowCover(string room)
        {
            HideCover();
            s_cover = new GameObject("Checkpoint Cover");
            DontDestroyOnLoad(s_cover);
            var canvas = s_cover.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;   // above every game screen
            var scaler = s_cover.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            s_cover.AddComponent<GraphicRaycaster>();   // takes the clicks meant for the screens beneath

            var backdrop = new GameObject("Backdrop", typeof(RectTransform)).AddComponent<Image>();
            backdrop.transform.SetParent(s_cover.transform, false);
            Stretch(backdrop.rectTransform);
            backdrop.color = new Color32(0x1E, 0x16, 0x38, 0xFF);

            var label = new GameObject("Label", typeof(RectTransform)).AddComponent<Text>();
            label.transform.SetParent(s_cover.transform, false);
            Stretch(label.rectTransform);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 56;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color32(0xFF, 0xC9, 0x33, 0xFF);
            label.text = "RESTORING CHECKPOINT\n<size=34><color=#FFF8EC>" + room + "</color></size>";
        }

        static void HideCover()
        {
            if (s_cover != null)
                Destroy(s_cover);
            s_cover = null;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ---- Time and cursor

        void StopTime()
        {
            Time.timeScale = 0f;
            _stoppedTime = true;
            SetCursorLocked(false);
        }

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
