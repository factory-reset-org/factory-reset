using System.Collections.Generic;
using NUnit.Framework;
using ToyFactory.UI;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>The parts of the prototype-layout HUD that are plain arithmetic and data: task counts, the current task, relay chips and the small readouts.</summary>
    public class HudPrototypeTests
    {
        static HudModel ChapterWith(params HudTaskRow[] rows)
        {
            var model = new HudModel();
            model.StartChapter(2, "Painting Room", "Shoot the targets", new List<HudTaskRow>(rows), 2);
            return model;
        }

        // ---- Counts

        [TestCase(0f, 4, 0)]
        [TestCase(0.25f, 4, 1)]
        [TestCase(0.5f, 4, 2)]
        [TestCase(0.999f, 4, 3)]
        [TestCase(1f, 4, 4)]
        [TestCase(1f / 3f, 3, 1)]
        [TestCase(2f / 3f, 3, 2)]
        [TestCase(-1f, 3, 0)]
        [TestCase(5f, 3, 3)]
        [TestCase(float.NaN, 3, 0)]
        [TestCase(0.5f, 0, 0)]
        public void ProgressBecomesTheNumberOfPartsDone(float progress, int total, int expected)
        {
            Assert.AreEqual(expected, HudMath.PartsDone(progress, total));
        }

        [Test]
        public void CountReadsDoneOverTotal()
        {
            Assert.AreEqual("1/3", HudMath.CountText(1, 3));
        }

        [Test]
        public void OnlyATaskWithSeveralPartsHasACount()
        {
            Assert.IsFalse(new HudTaskRow("a", "Pull the lever", false).HasCount);
            Assert.IsTrue(new HudTaskRow("b", "Shoot 4 targets", false, 4, 0).HasCount);
        }

        [Test]
        public void ReportedProgressMovesTheRowsCount()
        {
            HudModel model = ChapterWith(new HudTaskRow("ch2.targets", "Shoot 4 targets", false, 4, 0));
            int before = model.Version;

            Assert.IsTrue(model.SetProgress("ch2.targets", 0.5f));

            Assert.AreEqual(2, model.Rows[0].Count);
            Assert.Greater(model.Version, before, "The panel redraws.");
        }

        [Test]
        public void ProgressThatChangesNoCountDoesNotRedrawThePanel()
        {
            HudModel model = ChapterWith(new HudTaskRow("ch2.targets", "Shoot 4 targets", false, 4, 0));
            model.SetProgress("ch2.targets", 0.5f);
            int version = model.Version;

            Assert.IsFalse(model.SetProgress("ch2.targets", 0.6f), "Still 2 of 4.");
            Assert.AreEqual(version, model.Version);
        }

        [Test]
        public void ProgressForAnotherChaptersTaskOrADoneTaskIsIgnored()
        {
            HudModel model = ChapterWith(new HudTaskRow("ch2.targets", "Shoot 4 targets", false, 4, 0));
            model.CompleteTask("ch2.targets");

            Assert.IsFalse(model.SetProgress("ch3.relays", 0.5f));
            Assert.IsFalse(model.SetProgress("ch2.targets", 0.25f));
            Assert.AreEqual(4, model.Rows[0].Count, "A done task keeps every part counted.");
        }

        [Test]
        public void CompletingATaskCountsEveryPart()
        {
            HudModel model = ChapterWith(new HudTaskRow("ch2.targets", "Shoot 4 targets", false, 4, 1));

            model.CompleteTask("ch2.targets");

            Assert.IsTrue(model.Rows[0].Done);
            Assert.AreEqual(4, model.Rows[0].Count);
        }

        // ---- The current task

        [Test]
        public void CurrentTaskIsSetAndClearedByTheModel()
        {
            HudModel model = ChapterWith(new HudTaskRow("ch2.terminal", "Hack the terminal", false));
            int version = model.Version;

            model.SetCurrentTask("ch2.terminal");
            Assert.AreEqual("ch2.terminal", model.CurrentTaskId);
            Assert.Greater(model.Version, version);

            version = model.Version;
            model.SetCurrentTask("ch2.terminal");
            Assert.AreEqual(version, model.Version, "The same task again changes nothing.");

            model.SetCurrentTask(null);
            Assert.IsNull(model.CurrentTaskId);
        }

        [Test]
        public void StartingAChapterForgetsTheCurrentTask()
        {
            HudModel model = ChapterWith(new HudTaskRow("a", "A", false));
            model.SetCurrentTask("a");

            model.StartChapter(3, "Storage", "", new List<HudTaskRow>(), 3);

            Assert.IsNull(model.CurrentTaskId);
        }

        // ---- Relay order chips

        [Test]
        public void SequenceChipsFollowTheRowsProgress()
        {
            HudModel model = ChapterWith(new HudTaskRow("ch3.relays", "Activate the 3 relays", false, 3, 0));
            model.SetSequence("ch3.relays", new[] { 2, 3, 1 });

            CollectionAssert.AreEqual(new[] { 2, 3, 1 }, model.SequenceOrder);
            Assert.AreEqual(0, model.SequenceStep);

            model.SetProgress("ch3.relays", 2f / 3f);
            Assert.AreEqual(2, model.SequenceStep, "Two relays used; the third chip is next.");

            model.CompleteTask("ch3.relays");
            Assert.AreEqual(3, model.SequenceStep);
        }

        [Test]
        public void AWrongPressThatResetsTheSequenceMovesTheChipsBack()
        {
            HudModel model = ChapterWith(new HudTaskRow("ch3.relays", "Activate the 3 relays", false, 3, 0));
            model.SetSequence("ch3.relays", new[] { 1, 2, 3 });
            model.SetProgress("ch3.relays", 2f / 3f);

            model.SetProgress("ch3.relays", 0f);

            Assert.AreEqual(0, model.SequenceStep);
        }

        [Test]
        public void NoSequenceMeansNoChips()
        {
            HudModel model = ChapterWith(new HudTaskRow("a", "A", false));
            model.SetSequence("ch3.relays", new[] { 1, 2 });

            model.SetSequence(null, null);

            Assert.AreEqual(0, model.SequenceOrder.Count);
            Assert.IsNull(model.SequenceTaskId);
        }

        // ---- Small readouts

        [Test]
        public void ComboTextIsEmptyUntilThereIsACombo()
        {
            Assert.AreEqual(string.Empty, HudMath.ComboText(1));
            Assert.AreEqual("COMBO x3", HudMath.ComboText(3));
        }

        [TestCase(8f, false, "OVERCHARGE 8s")]
        [TestCase(7.2f, false, "OVERCHARGE 8s")]
        [TestCase(0.1f, false, "OVERCHARGE 1s")]
        [TestCase(5f, true, "OVERCHARGE 5s")]
        [TestCase(0f, true, "Charging...")]
        [TestCase(0f, false, "")]
        public void ChargeStatusReadsOverchargeThenCharging(float overcharge, bool reloading, string expected)
        {
            Assert.AreEqual(expected, HudMath.ChargeStatus(overcharge, reloading));
        }

        [TestCase(0f, false)]
        [TestCase(0.0005f, false)]
        [TestCase(0.3f, true)]
        [TestCase(0.998f, true)]
        [TestCase(1f, false)]
        public void HoldBarShowsOnlyWhileTheHoldIsUnderWay(float progress, bool expected)
        {
            Assert.AreEqual(expected, HudMath.HoldBarVisible(progress));
        }

        [Test]
        public void CrosshairIsFullyYellowAtTheHitAndPlainAfterwards()
        {
            Assert.AreEqual(1f, HudMath.HitMarkerAmount(0f));
            Assert.AreEqual(0.5f, HudMath.HitMarkerAmount(HudMath.HitMarkerSeconds * 0.5f), 1e-5f);
            Assert.AreEqual(0f, HudMath.HitMarkerAmount(HudMath.HitMarkerSeconds));
            Assert.AreEqual(0f, HudMath.HitMarkerAmount(float.PositiveInfinity), "Never hit.");
            Assert.AreEqual(0f, HudMath.HitMarkerAmount(-1f));
        }

        [Test]
        public void RoomBannerFadesInHoldsAndFadesOut()
        {
            Assert.AreEqual(0f, HudMath.RoomBannerAlpha(0f));
            Assert.AreEqual(0.5f, HudMath.RoomBannerAlpha(HudMath.RoomBannerFadeSeconds * 0.5f), 1e-5f);
            Assert.AreEqual(1f, HudMath.RoomBannerAlpha(1.5f));
            Assert.AreEqual(0.5f, HudMath.RoomBannerAlpha(HudMath.RoomBannerSeconds - HudMath.RoomBannerFadeSeconds * 0.5f), 1e-5f);
            Assert.AreEqual(0f, HudMath.RoomBannerAlpha(HudMath.RoomBannerSeconds));
            Assert.AreEqual(0f, HudMath.RoomBannerAlpha(-1f));
        }

        [Test]
        public void TheGlowPulsesWithinItsRange()
        {
            for (float t = 0f; t < 10f; t += 0.1f)
            {
                float alpha = HudMath.GlowAlpha(t);
                Assert.GreaterOrEqual(alpha, 0.2f - 1e-5f);
                Assert.LessOrEqual(alpha, 0.7f + 1e-5f);
            }
        }

        [Test]
        public void KeyHintsListTheKeysTheTitleScreenTeaches()
        {
            foreach (string key in new[] { "E", "Q", "R", "Tab", "H", "Esc" })
                StringAssert.Contains(key, HudMath.KeyHints);
        }
    }
}
