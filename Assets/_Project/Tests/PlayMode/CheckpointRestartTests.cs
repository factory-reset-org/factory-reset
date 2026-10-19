using System;
using System.Collections;
using System.Linq;
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
    /// Checkpoints and restarts on the real game from Bootstrap: a recall with a restart left
    /// waits on the recall screen; going back to the checkpoint puts the player at the room's
    /// entrance with full integrity and sends the robots home without scoring a takedown; the
    /// pause menu's restart uses one too; with none left a recall ends the run; Give up ends it
    /// at once; and the pause screen's sliders set the volumes.
    /// </summary>
    public sealed class CheckpointRestartTests
    {
        const float LoadTimeout = 60f;
        static readonly Vector3 PaintingRoomEntrance = new Vector3(22.6f, 0.1f, 10.5f);

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
            while (GameClock.Current.State != GameState.Playing)
            {
                CutsceneDirector director = CutsceneDirector.Current;
                if (director != null && director.IsPlaying)
                    director.Skip();
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for the intro to end");
                yield return null;
            }
        }

        static IEnumerator JumpTo(int chapter)
        {
            ChapterJump jump = UnityEngine.Object.FindAnyObjectByType<ChapterJump>();
            jump.JumpTo(chapter);
            yield return Until(() => !jump.IsJumping, 20f, "the jump to Chapter " + chapter);
            yield return Until(() => GameClock.Current.State == GameState.Playing, 20f, "Playing");
        }

        static void Recall() => PlayerState.Current.TakeDamage(10000f, -1);

        static float DistanceFlat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        [UnityTest]
        public IEnumerator EachRestartGoesBackToTheCheckpointAndTheFourthRecallEndsTheRun()
        {
            yield return LoadGame();
            IRunControl run = RunControl.Current;
            Assert.AreEqual(3, run.RestartsPerGame, "Three restarts a game.");
            Assert.AreEqual(3, run.RestartsLeft);

            // Room 1 done: the checkpoint is the Painting Room.
            yield return JumpTo(2);
            Assert.AreEqual("Painting Room", run.CheckpointName);

            AgentController tracker = AgentSpawner.Instance.SpawnedAgents.First(a => a.Type == AgentType.Tracker);
            tracker.Follower.Stop();
            Vector3 away = tracker.HomePosition + new Vector3(0f, 0f, 3f);
            var body = tracker.GetComponent<CharacterController>();
            body.enabled = false;
            tracker.transform.position = away;
            body.enabled = true;
            int takedowns = ScoreManager.Current.Rules.Takedowns;

            // A recall with restarts left waits on the recall screen.
            Recall();
            Assert.AreEqual(GameState.Paused, GameClock.Current.State, "The run waits instead of ending.");
            Assert.IsTrue(run.IsRecalled);
            Assert.AreEqual(0f, Time.timeScale, "Time stands still on the recall screen.");
            yield return null;
            yield return null;
            ScreensPresenter screens = UnityEngine.Object.FindAnyObjectByType<ScreensPresenter>();
            Assert.IsTrue(screens.Pause.IsRecallScreen, "The recall screen is up.");

            // Back to the checkpoint: one restart used.
            Assert.IsTrue(run.RestartFromCheckpoint());
            Assert.AreEqual(2, run.RestartsLeft);
            Assert.IsFalse(run.IsRecalled);
            Assert.AreEqual(GameState.Playing, GameClock.Current.State);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(PlayerState.Current.IsAlive);
            Assert.AreEqual(1f, PlayerState.Current.HealthFraction, 1e-4f, "Full integrity.");
            Assert.Less(DistanceFlat(PlayerState.Current.Position, PaintingRoomEntrance), 0.6f, "At the Painting Room's entrance.");
            Assert.Less(DistanceFlat(tracker.transform.position, tracker.HomePosition), 0.3f, "The Tracker is back at its post.");
            Assert.IsTrue(tracker.IsDisabled, "Powered down for a moment.");
            Assert.AreEqual(takedowns, ScoreManager.Current.Rules.Takedowns, "Sending it home is not a takedown.");
            yield return null;
            Assert.IsFalse(screens.Pause.IsShowing, "The screen goes away.");

            // A hit straight after the restart does nothing: the player gets a moment.
            PlayerState.Current.TakeDamage(30f, -1);
            Assert.AreEqual(1f, PlayerState.Current.HealthFraction, 1e-4f, "Safe for a moment after the restart.");

            // The pause menu's restart uses one as well.
            GameClock.Current.RequestState(GameState.Paused);
            yield return null;
            Assert.IsTrue(run.RestartFromCheckpoint());
            Assert.AreEqual(1, run.RestartsLeft);

            // The last one, after a recall (once the safe moment has passed).
            yield return new WaitForSeconds(2.2f);
            Recall();
            Assert.IsTrue(run.IsRecalled);
            Assert.IsTrue(run.RestartFromCheckpoint());
            Assert.AreEqual(0, run.RestartsLeft);

            // None left: the pause menu cannot go back, and a recall ends the run.
            GameClock.Current.RequestState(GameState.Paused);
            yield return null;
            yield return null;
            Assert.IsFalse(screens.Pause.CheckpointButton.interactable, "No restarts left to use.");
            Assert.IsFalse(run.RestartFromCheckpoint());
            run.Resume();
            Assert.AreEqual(GameState.Playing, GameClock.Current.State);
            yield return new WaitForSeconds(2.2f);   // past the safe moment
            Recall();
            Assert.IsFalse(run.IsRecalled);
            Assert.AreEqual(GameState.Results, GameClock.Current.State, "With no restart left the run ends.");
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
