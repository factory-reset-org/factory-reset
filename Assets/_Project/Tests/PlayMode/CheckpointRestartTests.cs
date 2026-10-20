using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Journey.Debugging;
using ToyFactory.Runtime.Agents;
using ToyFactory.UI;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Checkpoints and restarts on the real game from Bootstrap: going back puts the room as
    /// it was when its chapter began (its props and tasks unfinished, the score and the
    /// Saboteurs scrapped before the checkpoint kept, the player at the entrance); each restart
    /// is used once, after a recall or from the pause menu, and with none left a recall ends
    /// the run; Restart game gives them back; Give up ends the run; the pause screen's sliders
    /// set the volumes.
    /// </summary>
    public sealed class CheckpointRestartTests
    {
        const float LoadTimeout = 60f;
        const float RestoreTimeout = 90f;
        static readonly Vector3 StorageEntrance = new Vector3(31f, 0.1f, 22.6f);

        float _sound;
        float _music;

        [SetUp]
        public void KeepVolumes()
        {
            _sound = GameAudio.SoundVolume;
            _music = GameAudio.MusicVolume;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameAudio.SoundVolume = _sound;
            GameAudio.MusicVolume = _music;
            Time.timeScale = 1f;
            GameObject cover = GameObject.Find("Checkpoint Cover");
            if (cover != null)
                UnityEngine.Object.Destroy(cover);
            Scene empty = SceneManager.CreateScene("Empty after checkpoints " + Guid.NewGuid());
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }

            if (GameClock.Current is UnityEngine.Object clock && clock == null)
                GameClock.Publish(null);
            if (PlayerState.Current is UnityEngine.Object player && player == null)
                PlayerState.Publish(null);
        }

        static IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        static IEnumerator LoadGame()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => ChapterManager.Current != null && ChapterManager.Current.Flow != null
                && ChapterManager.Current.Flow.HasBegun && UnityEngine.Object.FindAnyObjectByType<ChapterJump>() != null
                && AgentSpawner.Instance != null && AgentSpawner.Instance.SpawnedAgents.Count > 0
                && PlayerState.Current != null && RunControl.Current != null, LoadTimeout, "the level");
            GameClock.Current.RequestState(GameState.Playing);
            yield return null;

            // Skip the intro as Escape would, so the run is being played.
            float until = Time.realtimeSinceStartup + 20f;
            while (GameClock.Current.State != GameState.Playing || IsCutscenePlaying())
            {
                if (IsCutscenePlaying())
                    CutsceneDirector.Current.Skip();
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for the intro to end");
                yield return null;
            }
        }

        static bool IsCutscenePlaying() => CutsceneDirector.Current != null && CutsceneDirector.Current.IsPlaying;

        static IEnumerator JumpTo(int chapter)
        {
            ChapterJump jump = UnityEngine.Object.FindAnyObjectByType<ChapterJump>();
            jump.JumpTo(chapter);
            yield return Until(() => !jump.IsJumping, 20f, "the jump to Chapter " + chapter);
            yield return Until(() => GameClock.Current.State == GameState.Playing, 20f, "Playing");
        }

        // The restore reloads the scenes, so the run control is a new one once it is done.
        static IEnumerator WaitForRestore(IRunControl before)
        {
            yield return Until(() => RunControl.Current != null && !ReferenceEquals(RunControl.Current, before)
                && !RunControl.Current.IsRestoring && GameClock.Current != null
                && GameClock.Current.State == GameState.Playing, RestoreTimeout, "the checkpoint to be restored");
        }

        static void Recall() => PlayerState.Current.TakeDamage(10000f, -1);

        static float DistanceFlat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static AgentController Saboteur(char letter) =>
            AgentSpawner.Instance.SpawnedAgents.First(a => a.Type == AgentType.Saboteur && a.Identity.SquadLetter == letter);

        // The task props live in the default assembly, which tests cannot see: reach them by name.
        static bool IsTaskProp(Type type)
        {
            for (Type t = type; t != null; t = t.BaseType)
                if (t.Name == "TaskProp")
                    return true;
            return false;
        }

        static MonoBehaviour FindTaskProp(string id) =>
            UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude)
                .FirstOrDefault(m => IsTaskProp(m.GetType()) && (string)m.GetType().GetProperty("Id").GetValue(m) == id);

        static bool IsCompleted(MonoBehaviour prop) => (bool)prop.GetType().GetProperty("IsCompleted").GetValue(prop);

        static void CompleteAsThePlayerWould(MonoBehaviour prop)
        {
            for (Type t = prop.GetType(); t != null; t = t.BaseType)
            {
                MethodInfo complete = t.GetMethod("Complete", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (complete != null)
                {
                    complete.Invoke(prop, null);
                    return;
                }
            }
            Assert.Fail("No Complete on " + prop.name);
        }

        [UnityTest]
        public IEnumerator GoingBackPutsTheRoomAsItWasWhenItsChapterBegan()
        {
            yield return LoadGame();
            IRunControl run = RunControl.Current;
            Assert.AreEqual(3, run.RestartsLeft);

            // Saboteur B is scrapped in Room 1, before the checkpoint.
            Saboteur('B').Scrap();
            yield return JumpTo(3);
            Assert.AreEqual("Storage Area", run.CheckpointName);
            int scoreAtCheckpoint = ScoreManager.Current.Score;

            // In Room 3 the player finishes a prop, and scraps Saboteur A, after the checkpoint.
            ChapterFlow flow = ChapterManager.Current.Flow;
            string taskId = flow.GetDefinition(3).Tasks.Select(t => t.TaskId).First(id => FindTaskProp(id) != null);
            CompleteAsThePlayerWould(FindTaskProp(taskId));
            Assert.IsTrue(flow.IsTaskComplete(taskId), "The task counts.");
            Saboteur('A').Scrap();
            Assert.Greater(ScoreManager.Current.Score, scoreAtCheckpoint);

            // Recalled: back to the checkpoint.
            yield return new WaitForSeconds(0.1f);
            Recall();
            Assert.IsTrue(run.IsRecalled);
            Assert.IsTrue(run.RestartFromCheckpoint());
            Assert.AreEqual(2, run.RestartsLeft);
            yield return WaitForRestore(run);

            run = RunControl.Current;
            flow = ChapterManager.Current.Flow;
            Assert.AreEqual(3, flow.CurrentChapter, "Back in Chapter 3.");
            Assert.IsFalse(flow.IsTaskComplete(taskId), "The room's task is not done.");
            Assert.IsFalse(IsCompleted(FindTaskProp(taskId)), "Its prop is as it was when the chapter began.");
            Assert.AreEqual(scoreAtCheckpoint, ScoreManager.Current.Score, "The score is the checkpoint's.");
            Assert.IsTrue(Saboteur('B').IsDead, "Scrapped before the checkpoint: stays scrapped.");
            Assert.IsFalse(Saboteur('A').IsDead, "Scrapped after it: back.");
            Assert.AreEqual(2, run.RestartsLeft, "The restarts carry over the reload.");
            Assert.AreEqual(1f, PlayerState.Current.HealthFraction, 1e-4f);
            Assert.Less(DistanceFlat(PlayerState.Current.Position, StorageEntrance), 0.6f, "At the Storage Area's entrance.");
            Assert.AreEqual(1f, Time.timeScale);
            yield return null;   // a destroyed object goes at the end of the frame
            Assert.IsNull(GameObject.Find("Checkpoint Cover"), "The cover is gone.");

            // A hit straight after the restart does nothing: the player gets a moment.
            PlayerState.Current.TakeDamage(30f, -1);
            Assert.AreEqual(1f, PlayerState.Current.HealthFraction, 1e-4f, "Safe for a moment after the restart.");
        }

        static MonoBehaviour Prop(string typeName, Func<MonoBehaviour, bool> where = null) =>
            UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude)
                .FirstOrDefault(m => m.GetType().Name == typeName && (where == null || where(m)));

        static MonoBehaviour Relay(int number) => Prop("Relay", m => (int)m.GetType().GetProperty("Number").GetValue(m) == number);

        static bool IsOn(MonoBehaviour relay) => (bool)relay.GetType().GetProperty("IsOn").GetValue(relay);

        static void Press(MonoBehaviour relay) => relay.GetType().GetMethod("Interact").Invoke(relay, null);

        static int[] RelayOrder()
        {
            Assert.IsTrue(ChapterManager.Current.Flow.TryGetSequenceOrder("ch3.relays", out IReadOnlyList<int> order));
            return order.ToArray();
        }

        [UnityTest]
        public IEnumerator TheRelayTheRingMarksWorksWithoutGoingToTheBoard()
        {
            yield return LoadGame();
            yield return JumpTo(3);
            int[] order = RelayOrder();

            // The player is at the Storage Area's entrance, far from the board in the back corner,
            // as when following the objective ring straight to the first relay.
            MonoBehaviour board = Prop("RelayBoard");
            Assert.Greater(DistanceFlat(PlayerState.Current.Position, board.transform.position), 8f);
            Assert.IsFalse((bool)board.GetType().GetProperty("IsRead").GetValue(board), "The board has not been read.");

            Press(Relay(order[0]));
            Assert.IsTrue(IsOn(Relay(order[0])), "The first relay turns on without a visit to the board.");
            Press(Relay(order[1]));
            Assert.IsTrue(IsOn(Relay(order[1])), "And so does the second, in order.");
            Press(Relay(order[2]));
            Assert.IsTrue(ChapterManager.Current.Flow.IsTaskComplete("ch3.relays"), "All three in order finish the task.");
        }

        [UnityTest]
        public IEnumerator TheRelayOrderIsTheSameAfterGoingBack()
        {
            yield return LoadGame();
            yield return JumpTo(3);
            int[] before = RelayOrder();

            yield return new WaitForSeconds(0.1f);
            IRunControl run = RunControl.Current;
            Recall();
            Assert.IsTrue(run.RestartFromCheckpoint());
            yield return WaitForRestore(run);

            CollectionAssert.AreEqual(before, RelayOrder(), "The order the board showed before the restart.");
            Assert.IsNull(ChapterManager.NextRunSeed, "The handed-over seed is used once.");
        }

        [UnityTest]
        public IEnumerator AWrongRelaySetsOffTheAlarmAndSwitchesThemAllOff()
        {
            yield return LoadGame();
            yield return JumpTo(3);
            int[] order = RelayOrder();

            Press(Relay(order[0]));
            Assert.IsTrue(IsOn(Relay(order[0])));
            Press(Relay(order[2]));   // the third before the second
            Assert.IsFalse(IsOn(Relay(order[0])), "A wrong relay switches them all off.");
            Assert.IsFalse(IsOn(Relay(order[2])));
            Assert.IsFalse(ChapterManager.Current.Flow.IsTaskComplete("ch3.relays"));
        }

        [UnityTest]
        public IEnumerator AKeycardTakenBeforeTheCheckpointStillCountsAfterGoingBack()
        {
            yield return LoadGame();

            // Saboteur A goes down in Room 1 and its keycard is picked up early.
            Saboteur('A').Scrap();
            yield return null;
            CompleteAsThePlayerWould(FindTaskProp("ch3.keycard"));
            yield return JumpTo(3);
            Assert.IsTrue(ChapterManager.Current.Flow.IsTaskComplete("ch3.keycard"));
            int scoreAtCheckpoint = ScoreManager.Current.Score;

            yield return new WaitForSeconds(0.1f);
            IRunControl run = RunControl.Current;
            Recall();
            Assert.IsTrue(run.RestartFromCheckpoint());
            yield return WaitForRestore(run);

            Assert.IsTrue(ChapterManager.Current.Flow.IsTaskComplete("ch3.keycard"), "The keycard still counts.");
            Assert.IsNull(FindTaskProp("ch3.keycard"), "No second keycard lying where A fell.");
            Assert.IsTrue(Saboteur('A').IsDead);
            Assert.AreEqual(scoreAtCheckpoint, ScoreManager.Current.Score);
        }

        [UnityTest]
        public IEnumerator EachWayBackUsesARestartAndWithNoneLeftARecallEndsTheRun()
        {
            yield return LoadGame();
            IRunControl run = RunControl.Current;

            // From the pause menu.
            GameClock.Current.RequestState(GameState.Paused);
            yield return null;
            Assert.IsTrue(run.RestartFromCheckpoint());
            yield return WaitForRestore(run);
            run = RunControl.Current;
            Assert.AreEqual(2, run.RestartsLeft);
            Assert.AreEqual(1, ChapterManager.Current.Flow.CurrentChapter, "Before the first checkpoint: the start of the game.");

            // Twice after a recall.
            for (int left = 1; left >= 0; left--)
            {
                yield return new WaitForSeconds(2.2f);   // past the safe moment
                Recall();
                Assert.IsTrue(run.IsRecalled);
                Assert.IsTrue(run.RestartFromCheckpoint());
                yield return WaitForRestore(run);
                run = RunControl.Current;
                Assert.AreEqual(left, run.RestartsLeft);
            }

            // None left: the pause menu cannot go back, and a recall ends the run.
            GameClock.Current.RequestState(GameState.Paused);
            yield return null;
            yield return null;
            ScreensPresenter screens = UnityEngine.Object.FindAnyObjectByType<ScreensPresenter>();
            Assert.IsFalse(screens.Pause.CheckpointButton.interactable, "No restarts left to use.");
            Assert.IsFalse(run.RestartFromCheckpoint());
            run.Resume();
            yield return new WaitForSeconds(2.2f);
            Recall();
            Assert.IsFalse(run.IsRecalled);
            Assert.AreEqual(GameState.Results, GameClock.Current.State, "With no restart left the run ends.");
        }

        [UnityTest]
        public IEnumerator RestartGameGivesEveryRestartBack()
        {
            yield return LoadGame();
            IRunControl run = RunControl.Current;
            GameClock.Current.RequestState(GameState.Paused);
            yield return null;
            Assert.IsTrue(run.RestartFromCheckpoint());
            yield return WaitForRestore(run);
            run = RunControl.Current;
            Assert.AreEqual(2, run.RestartsLeft);

            run.RestartGame();
            yield return Until(() => RunControl.Current != null && !ReferenceEquals(RunControl.Current, run)
                && GameClock.Current != null && GameClock.Current.State == GameState.Title, LoadTimeout, "a new game");
            Assert.AreEqual(3, RunControl.Current.RestartsLeft);
            Assert.IsFalse(RunControl.Current.IsRestoring);
        }

        [UnityTest]
        public IEnumerator GiveUpEndsARecalledRun()
        {
            yield return LoadGame();
            yield return new WaitForSeconds(0.1f);
            IRunControl run = RunControl.Current;
            Recall();
            Assert.IsTrue(run.IsRecalled);
            run.GiveUp();
            Assert.IsFalse(run.IsRecalled);
            Assert.AreEqual(GameState.Results, GameClock.Current.State);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(3, run.RestartsLeft, "Giving up uses no restart.");
        }

        [UnityTest]
        public IEnumerator ThePauseScreenShowsTheRestartsAndItsSlidersSetTheVolumes()
        {
            yield return LoadGame();
            GameClock.Current.RequestState(GameState.Paused);
            yield return null;
            yield return null;
            ScreensPresenter screens = UnityEngine.Object.FindAnyObjectByType<ScreensPresenter>();
            Assert.IsTrue(screens.Pause.IsShowing);
            Assert.IsFalse(screens.Pause.IsRecallScreen);
            Assert.IsTrue(screens.Pause.CheckpointButton.interactable);

            screens.Pause.SoundSlider.value = 0.25f;
            screens.Pause.MusicSlider.value = 0.6f;
            Assert.AreEqual(0.25f, GameAudio.SoundVolume, 1e-4f);
            Assert.AreEqual(0.6f, GameAudio.MusicVolume, 1e-4f);

            RunControl.Current.Resume();
            yield return null;
            Assert.AreEqual(GameState.Playing, GameClock.Current.State);
            Assert.IsFalse(screens.Pause.IsShowing);
        }
    }
}
