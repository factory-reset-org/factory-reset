using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.UI;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The prototype-layout HUD in the real scenes: Bootstrap loads Env, Interactables, Agents and UI,
    /// the intro is skipped, and the new parts must show what the game is doing.
    /// </summary>
    public sealed class HudPrototypeSceneTests
    {
        sealed class FakeTask : ITask
        {
            public FakeTask(string id) { Id = id; }
            public string Id { get; }
#pragma warning disable CS0067
            public event Action<float> OnProgress;
            public event Action OnCompleted;
#pragma warning restore CS0067
            public void Report(float progress) => OnProgress?.Invoke(progress);
        }

        const float LoadTimeout = 60f;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;

            Scene empty = SceneManager.CreateScene("Empty after HUD " + Guid.NewGuid());
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

        // Bootstrap, then the intro skipped: the game is playing and Chapter 1 has started.
        static IEnumerator StartPlaying()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => HudPresenter.Current != null && ChapterManager.Current != null && ChapterManager.Current.Flow != null
                && ChapterManager.Current.Flow.HasBegun && CutsceneDirector.Current != null && PlayerState.Current != null,
                LoadTimeout, "Bootstrap to load the scenes and the HUD");
            yield return Until(() => CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current != null && GameClock.Current.State == GameState.Playing, 10f, "Playing");
            yield return Until(() => ChapterManager.Current.Flow.CurrentChapter == 1, 10f, "Chapter 1");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScorePanelFollowsTheScoreManager()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            Assert.AreEqual("0", hud.ScoreShown);
            StringAssert.IsMatch(@"^\d+:\d\d$", hud.TimeShown);
            Assert.AreEqual(string.Empty, hud.ComboShown);

            ChapterManager.Current.Flow.CompleteTask("ch1.lever");
            yield return null;
            yield return null;

            Assert.AreEqual("300", hud.ScoreShown, "A task is worth 300.");
        }

        [UnityTest]
        public IEnumerator RoomBannerNamesTheChaptersArea()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;

            Assert.AreEqual(ChapterManager.Current.Flow.CurrentDefinition.Area, hud.RoomShown);
            Assert.IsTrue(hud.RoomBannerShowing);

            hud.SetRoom("Painting Room");
            Assert.AreEqual("Painting Room", hud.RoomShown);
        }

        [UnityTest]
        public IEnumerator CurrentTaskIsMarkedWithItsDistance()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            yield return Until(() => hud.Model.CurrentTaskId != null, 5f, "the current task");

            Assert.AreEqual(ChapterManager.Current.CurrentBeaconTarget.Value.TaskId, hud.Model.CurrentTaskId);
            yield return null;
            yield return null;
            Assert.IsTrue(hud.CurrentMarkerShown);
            StringAssert.IsMatch(@"^\d+ m$", hud.CurrentDistanceShown);
        }

        [UnityTest]
        public IEnumerator SwitchRowIsFadedWhileSealedAndClearsWhenItOpens()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            int last = hud.Model.Rows.Count - 1;
            Assert.IsTrue(hud.Model.Rows[last].Id.StartsWith(HudModel.SwitchRowPrefix), "The chapter ends with its switch row.");
            Assert.AreEqual(HudMath.SealedRowAlpha, hud.RowAlpha(last), 1e-3f);
            Assert.AreEqual(1f, hud.RowAlpha(0), 1e-3f);

            ChapterEvents.RaiseSwitchUnsealed(1);
            yield return null;
            yield return null;

            Assert.AreEqual(1f, hud.RowAlpha(last), 1e-3f);
        }

        [UnityTest]
        public IEnumerator HoldBarFollowsAHoldTasksProgress()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            Assert.IsFalse(hud.HoldBarShown);

            // The chapter manager objects to a second prop with the real terminal's id; the HUD does not.
            var terminal = new FakeTask("ch2.terminal");
            LogAssert.Expect(LogType.Error, new Regex("Two task props share the id"));
            TaskEvents.RaiseTaskSpawned(terminal);
            terminal.Report(0.5f);
            yield return null;

            Assert.IsTrue(hud.HoldBarShown);
            Assert.AreEqual(0.5f, hud.HoldBarFraction, 1e-3f);

            terminal.Report(0f);
            yield return null;
            Assert.IsFalse(hud.HoldBarShown, "A decayed hold empties the bar.");
        }

        [UnityTest]
        public IEnumerator FedReadoutsShowUntilClearedAndStayHiddenBeforeThat()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            Assert.AreEqual(string.Empty, hud.PromptShown);
            Assert.AreEqual(-1, hud.ToysShown, "Nothing has reported a toy count, so no dots.");
            Assert.AreEqual(0f, hud.CrosshairHitAmount);

            hud.SetPrompt("Press E to pull the lever");
            hud.SetToys(2, 3);
            hud.ReportHit();
            yield return null;

            Assert.AreEqual("Press E to pull the lever", hud.PromptShown);
            Assert.AreEqual(2, hud.ToysShown);
            Assert.Greater(hud.CrosshairHitAmount, 0f);

            hud.SetPrompt(null);
            Assert.AreEqual(string.Empty, hud.PromptShown);
        }

        [UnityTest]
        public IEnumerator ChargeReadoutSurvivesAMissingPlayer()
        {
            yield return StartPlaying();
            PlayerState.Publish(null);

            yield return null;
            yield return null;

            Assert.IsTrue(HudPresenter.Current.IsVisible);
            Assert.AreEqual(string.Empty, HudPresenter.Current.ChargeStatusShown);
            Assert.AreEqual(0f, HudPresenter.Current.ChargeShown);
        }
    }
}
