using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.UI;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>The HUD's arithmetic, its data model and the timing of its chapter card.</summary>
    public class HudTests
    {
        // ---- HudMath

        [Test]
        public void ChapterTagReadsChapterOfFour()
        {
            Assert.AreEqual("CHAPTER 2 OF 4", HudMath.ChapterTag(2, 4));
        }

        [TestCase(0.5f, 0.5f)]
        [TestCase(-1f, 0f)]
        [TestCase(7f, 1f)]
        [TestCase(float.NaN, 0f)]
        public void FractionIsClampedAndNaNReadsAsZero(float value, float expected)
        {
            Assert.AreEqual(expected, HudMath.Fraction(value));
        }

        [Test]
        public void OverchargeBarStartsFullAndDrains()
        {
            Assert.AreEqual(1f, HudMath.OverchargeFraction(8f, 8f));
            Assert.AreEqual(0.25f, HudMath.OverchargeFraction(2f, 8f), 1e-6f);
            Assert.AreEqual(0f, HudMath.OverchargeFraction(3f, 0f), "No known length, no bar.");
            Assert.AreEqual(1f, HudMath.OverchargeFraction(12f, 8f), "A longer overcharge than seen so far cannot overfill.");
        }

        [Test]
        public void HealthyUnhitPlayerHasNoVignette()
        {
            Assert.AreEqual(0f, HudMath.VignetteAlpha(float.PositiveInfinity, 1f));
        }

        [Test]
        public void HitFlashIsStrongestAtOnceAndFadesToNothing()
        {
            float now = HudMath.VignetteAlpha(0f, 1f);
            float later = HudMath.VignetteAlpha(HudMath.HitFlashSeconds * 0.5f, 1f);

            Assert.AreEqual(HudMath.HitFlashAlpha, now, 1e-6f);
            Assert.AreEqual(HudMath.HitFlashAlpha * 0.5f, later, 1e-6f);
            Assert.AreEqual(0f, HudMath.VignetteAlpha(HudMath.HitFlashSeconds, 1f));
        }

        [Test]
        public void LowIntegrityKeepsASteadyGlowThatGrowsAsIntegrityFalls()
        {
            float atThreshold = HudMath.VignetteAlpha(float.PositiveInfinity, HudMath.LowHealthFraction);
            float half = HudMath.VignetteAlpha(float.PositiveInfinity, HudMath.LowHealthFraction * 0.5f);
            float empty = HudMath.VignetteAlpha(float.PositiveInfinity, 0f);

            Assert.AreEqual(0f, atThreshold);
            Assert.Greater(half, 0f);
            Assert.AreEqual(HudMath.LowHealthAlpha, empty, 1e-6f);
            Assert.Greater(empty, half);
        }

        [Test]
        public void VignetteTakesTheStrongerOfFlashAndGlow()
        {
            Assert.AreEqual(HudMath.HitFlashAlpha, HudMath.VignetteAlpha(0f, 0.2f), 1e-6f);
            Assert.AreEqual(HudMath.LowHealthAlpha, HudMath.VignetteAlpha(HudMath.HitFlashSeconds, 0f), 1e-6f);
        }

        [Test]
        public void ObjectiveOnScreenHidesTheArrow()
        {
            Assert.IsFalse(HudMath.TryPlaceArrow(new Vector3(0.5f, 0.5f, 10f), 0.08f, out _, out _));
            Assert.IsFalse(HudMath.TryPlaceArrow(new Vector3(0.2f, 0.8f, 3f), 0.08f, out _, out _));
        }

        [Test]
        public void ObjectiveToTheRightPutsTheArrowOnTheRightEdgePointingRight()
        {
            Assert.IsTrue(HudMath.TryPlaceArrow(new Vector3(1.6f, 0.5f, 10f), 0.08f, out Vector2 edge, out float angle));

            Assert.AreEqual(0.92f, edge.x, 1e-5f);
            Assert.AreEqual(0.5f, edge.y, 1e-5f);
            Assert.AreEqual(0f, angle, 1e-3f);
        }

        [Test]
        public void ObjectiveAboveAndToTheLeftPointsUpLeftAtTheTopEdge()
        {
            Assert.IsTrue(HudMath.TryPlaceArrow(new Vector3(-0.1f, 2.5f, 10f), 0.08f, out Vector2 edge, out float angle));

            Assert.AreEqual(0.92f, edge.y, 1e-5f, "The vertical limit is reached first.");
            Assert.Less(edge.x, 0.5f);
            Assert.Greater(angle, 90f);
            Assert.Less(angle, 180f);
        }

        [Test]
        public void ObjectiveBehindThePlayerPointsTheWayToTurn()
        {
            // Behind and a little to the left on screen: turning right brings it round, so the arrow points left-ish
            // as the projection is mirrored. The arrow must sit on an edge either way.
            Assert.IsTrue(HudMath.TryPlaceArrow(new Vector3(0.4f, 0.5f, -5f), 0.08f, out Vector2 edge, out float angle));

            Assert.AreEqual(0.92f, edge.x, 1e-5f);
            Assert.AreEqual(0f, angle, 1e-3f);
        }

        [Test]
        public void ObjectiveStraightBehindPointsDown()
        {
            Assert.IsTrue(HudMath.TryPlaceArrow(new Vector3(0.5f, 0.5f, -5f), 0.08f, out Vector2 edge, out float angle));

            Assert.AreEqual(-90f, angle, 1e-3f);
            Assert.AreEqual(0.08f, edge.y, 1e-5f);
        }

        [Test]
        public void ArrowAlwaysStaysInsideTheMargin()
        {
            for (float x = -2f; x <= 3f; x += 0.7f)
            {
                for (float y = -2f; y <= 3f; y += 0.7f)
                {
                    foreach (float z in new[] { -4f, 4f })
                    {
                        if (!HudMath.TryPlaceArrow(new Vector3(x, y, z), 0.08f, out Vector2 edge, out _))
                            continue;
                        Assert.GreaterOrEqual(edge.x, 0.08f - 1e-4f);
                        Assert.LessOrEqual(edge.x, 0.92f + 1e-4f);
                        Assert.GreaterOrEqual(edge.y, 0.08f - 1e-4f);
                        Assert.LessOrEqual(edge.y, 0.92f + 1e-4f);
                    }
                }
            }
        }

        [Test]
        public void DistanceTextIsWholeMetres()
        {
            Assert.AreEqual("12 m", HudMath.DistanceText(12.4f));
            Assert.AreEqual("13 m", HudMath.DistanceText(12.6f));
            Assert.AreEqual("0 m", HudMath.DistanceText(-3f));
        }

        // ---- HudModel

        static HudTaskRow Row(string id, string text, bool done = false) => new HudTaskRow(id, text, done);

        static readonly HudTaskRow[] Chapter1 =
        {
            new HudTaskRow("ch1.lever", "Pull the conveyor lever", false),
            new HudTaskRow("ch1.relay", "Reconnect the relay", false)
        };

        [Test]
        public void ModelStartsEmpty()
        {
            var model = new HudModel();

            Assert.AreEqual(0, model.Chapter);
            Assert.AreEqual(0, model.Rows.Count);
            Assert.AreEqual(0, model.SwitchesRestored);
            Assert.AreEqual(LampState.Sealed, model.Lamp(1));
        }

        [Test]
        public void StartingAChapterFillsTheRowsAndAddsTheSwitchRow()
        {
            var model = new HudModel();

            model.StartChapter(1, "Assembly Floor", "Wake the line", Chapter1, 1);

            Assert.AreEqual(1, model.Chapter);
            Assert.AreEqual("Assembly Floor", model.Title);
            Assert.AreEqual(3, model.Rows.Count);
            Assert.AreEqual("switch.1", model.Rows[2].Id);
            Assert.AreEqual("Restore switch 1", model.Rows[2].Text);
            Assert.AreEqual(0, model.RowsDone);
        }

        [Test]
        public void ChapterWithoutASwitchHasOnlyItsTasks()
        {
            var model = new HudModel();

            model.StartChapter(4, "Control Room", string.Empty, Chapter1, 0);

            Assert.AreEqual(2, model.Rows.Count);
            Assert.AreEqual(0, model.SwitchNumber);
        }

        [Test]
        public void CompletingATaskTicksItOnceAndOnlyIfItIsThere()
        {
            var model = new HudModel();
            model.StartChapter(1, "Assembly Floor", string.Empty, Chapter1, 1);

            Assert.IsTrue(model.CompleteTask("ch1.relay"));
            Assert.IsTrue(model.Rows[1].Done);
            Assert.IsFalse(model.CompleteTask("ch1.relay"), "Already done.");
            Assert.IsFalse(model.CompleteTask("ch9.nothing"), "Not in this chapter.");
            Assert.AreEqual(1, model.RowsDone);
        }

        [Test]
        public void EveryChangeRaisesTheVersionAndNoChangeDoesNot()
        {
            var model = new HudModel();
            int v0 = model.Version;

            model.StartChapter(1, "A", "B", Chapter1, 1);
            int v1 = model.Version;
            model.CompleteTask("ch1.lever");
            int v2 = model.Version;
            model.CompleteTask("ch1.lever");
            model.SetLamp(1, LampState.Sealed);

            Assert.Greater(v1, v0);
            Assert.Greater(v2, v1);
            Assert.AreEqual(v2, model.Version, "Repeating a change is not a change.");
        }

        [Test]
        public void RestoringTheSwitchLightsTheLampAndTicksItsRow()
        {
            var model = new HudModel();
            model.StartChapter(1, "Assembly Floor", string.Empty, Chapter1, 1);

            model.SetLamp(1, LampState.Ready);
            Assert.AreEqual(LampState.Ready, model.Lamp(1));
            Assert.IsFalse(model.Rows[2].Done);

            model.SetLamp(1, LampState.Restored);
            Assert.AreEqual(LampState.Restored, model.Lamp(1));
            Assert.IsTrue(model.Rows[2].Done);
            Assert.AreEqual(1, model.SwitchesRestored);
        }

        [Test]
        public void LampsSurviveTheNextChapter()
        {
            var model = new HudModel();
            model.StartChapter(1, "Assembly Floor", string.Empty, Chapter1, 1);
            model.SetLamp(1, LampState.Restored);

            model.StartChapter(2, "Painting Room", string.Empty, Chapter1, 2);

            Assert.AreEqual(LampState.Restored, model.Lamp(1));
            Assert.AreEqual(1, model.SwitchesRestored);
            Assert.IsFalse(model.Rows[2].Done, "Switch 2 is not restored.");
        }

        [Test]
        public void SwitchRowOfAnAlreadyRestoredSwitchStartsTicked()
        {
            var model = new HudModel();
            model.SetLamp(2, LampState.Restored);

            model.StartChapter(2, "Painting Room", string.Empty, new List<HudTaskRow>(), 2);

            Assert.IsTrue(model.Rows[0].Done);
        }

        [Test]
        public void ModelRejectsBadArguments()
        {
            var model = new HudModel();

            Assert.Throws<System.ArgumentOutOfRangeException>(() => model.StartChapter(0, "A", "B", Chapter1, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => model.StartChapter(5, "A", "B", Chapter1, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => model.StartChapter(1, "A", "B", Chapter1, 4));
            Assert.Throws<System.ArgumentNullException>(() => model.StartChapter(1, "A", "B", null, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => model.Lamp(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => model.SetLamp(4, LampState.Ready));
        }

        [TestCase(GameState.Playing, false, true)]
        [TestCase(GameState.Paused, false, true)]
        [TestCase(GameState.Playing, true, false)]
        [TestCase(GameState.Cutscene, false, false)]
        [TestCase(GameState.Title, false, false)]
        [TestCase(GameState.Results, false, false)]
        public void HudShowsOnlyWhilePlayingOrPausedOutsideCutscenes(GameState state, bool cutscene, bool expected)
        {
            Assert.AreEqual(expected, HudModel.IsVisible(state, cutscene));
        }

        // ---- The chapter card and the subtitle text

        [Test]
        public void ChapterCardFadesInHoldsAndFadesOut()
        {
            Assert.AreEqual(0f, ChapterCardView.AlphaAt(0f));
            Assert.AreEqual(0.5f, ChapterCardView.AlphaAt(ChapterCardView.FadeInSeconds * 0.5f), 1e-5f);
            Assert.AreEqual(1f, ChapterCardView.AlphaAt(2f));
            Assert.AreEqual(0.5f, ChapterCardView.AlphaAt(ChapterCardView.TotalSeconds - ChapterCardView.FadeOutSeconds * 0.5f), 1e-5f);
            Assert.AreEqual(0f, ChapterCardView.AlphaAt(ChapterCardView.TotalSeconds));
            Assert.AreEqual(0f, ChapterCardView.AlphaAt(-1f));
        }

        [Test]
        public void ChapterCardLastsFourAndAHalfSeconds()
        {
            Assert.AreEqual(4.5f, ChapterCardView.TotalSeconds);
        }

        [Test]
        public void SubtitleShowsTheSpeakerInItsColourThenTheTypedText()
        {
            var line = new DialogueLineView("UNIT 047", new Color(1f, 0f, 0f), "Hello there", 5);

            string text = SubtitleView.Format(line);

            Assert.AreEqual("<b><color=#FF0000>UNIT 047</color></b>  Hello", text);
        }
    }
}
