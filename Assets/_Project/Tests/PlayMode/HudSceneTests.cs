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
using ToyFactory.UI;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The HUD in the real scenes: Bootstrap loads Env, Interactables, Agents and UI, the intro is
    /// skipped, and the HUD must show the chapter, follow the player, hide in a cutscene and draw
    /// the cutscene's subtitles.
    /// </summary>
    public sealed class HudSceneTests
    {
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
        public IEnumerator HudShowsChapterOneItsTasksAndThreeSealedSwitches()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;

            Assert.IsTrue(hud.IsVisible);
            Assert.AreEqual("CHAPTER 1 OF 4", hud.ChapterText);
            Assert.AreEqual(ChapterManager.Current.Flow.CurrentDefinition.Title, hud.TitleText);
            Assert.Greater(hud.Model.Rows.Count, 1, "The chapter's tasks and its switch row.");
            Assert.IsNotNull(hud.RowText(0));
            Assert.AreEqual(0, hud.Model.SwitchesRestored);
            Assert.AreEqual(LampState.Sealed, hud.Model.Lamp(1));
        }

        [UnityTest]
        public IEnumerator ChapterCardAppearsWhenTheChapterStarts()
        {
            yield return StartPlaying();

            Assert.IsTrue(HudPresenter.Current.Card.IsShowing, "The card shows for 4.5 s after Chapter 1 starts.");
        }

        [UnityTest]
        public IEnumerator OnlyOneChapterCardShowsBecauseTheDirectorsIsSwitchedOff()
        {
            yield return StartPlaying();

            ChapterCard directorCard = UnityEngine.Object.FindFirstObjectByType<ChapterCard>(FindObjectsInactive.Include);
            if (directorCard != null)
            {
                Assert.IsFalse(directorCard.enabled, "The director's own card is switched off once the HUD draws one.");
                Assert.IsFalse(directorCard.IsShowing);
            }

            Assert.IsTrue(HudPresenter.Current.Card.IsShowing);
        }

        [UnityTest]
        public IEnumerator CompletingATaskTicksItsRow()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            string taskId = hud.Model.Rows[0].Id;
            Assert.IsFalse(hud.Model.Rows[0].Done);

            ChapterManager.Current.Flow.CompleteTask(taskId);
            yield return null;

            Assert.IsTrue(hud.Model.Rows[0].Done);
        }

        [UnityTest]
        public IEnumerator BarsFollowThePlayersIntegrityAndCharge()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            IPlayerState player = PlayerState.Current;

            Assert.AreEqual(player.HealthFraction, hud.IntegrityShown, 0.01f);
            Assert.AreEqual(player.AmmoFraction, hud.ChargeShown, 0.01f);

            player.TakeDamage(20f, -1);
            yield return null;
            yield return null;

            Assert.AreEqual(player.HealthFraction, hud.IntegrityShown, 0.01f);
            Assert.Less(hud.IntegrityShown, 1f);
            Assert.Greater(hud.VignetteShown, 0f, "A hit flashes the vignette.");
        }

        [UnityTest]
        public IEnumerator HudHidesDuringACutsceneAndReturnsAfterIt()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;

            CutsceneEvents.RaiseCutsceneStarted("test");
            yield return null;
            Assert.IsFalse(hud.IsVisible, "Hidden in a cutscene.");

            CutsceneEvents.RaiseCutsceneEnded("test");
            yield return null;
            Assert.IsTrue(hud.IsVisible);
        }

        [UnityTest]
        public IEnumerator SubtitlesShowDialogueAndSwitchOffThePlaceholder()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            PlaceholderSubtitles placeholder = UnityEngine.Object.FindFirstObjectByType<PlaceholderSubtitles>();

            DialogueEvents.RaiseLineShown(new DialogueLineView("TEST", Color.white, "Hello there", 5));
            yield return null;

            Assert.IsTrue(hud.Subtitles.IsShowing);
            StringAssert.Contains("Hello", hud.Subtitles.DisplayedText);
            if (placeholder != null)
                Assert.IsFalse(placeholder.enabled, "The placeholder bar stops once the real one draws.");

            DialogueEvents.RaiseLineCleared();
            yield return null;
            Assert.IsFalse(hud.Subtitles.IsShowing);
        }

        [UnityTest]
        public IEnumerator HudSurvivesAMissingPlayer()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;

            PlayerState.Publish(null);
            yield return null;
            yield return null;

            Assert.IsTrue(hud.IsVisible);
            Assert.AreEqual(0f, hud.IntegrityShown, 1e-4f, "No player, empty bar, no exception.");
        }
    }
}
