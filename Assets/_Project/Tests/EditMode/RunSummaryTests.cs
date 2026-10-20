using System;
using System.Linq;
using NUnit.Framework;
using ToyFactory.UI;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>The Results screen's run summary: its formatting, clamping and breakdown.</summary>
    public class RunSummaryTests
    {
        static RunSummary Summary(RunOutcome outcome = RunOutcome.Won, float seconds = 754.9f, int cleared = 4, int takedowns = 7,
            int fired = 40, int hit = 30, int score = 9000, ScoreGrade grade = ScoreGrade.B, ScoreRules rules = null) =>
            new RunSummary(outcome, seconds, cleared, takedowns, fired, hit, score, grade, rules != null ? rules.History : null);

        [TestCase(0f, "0:00")]
        [TestCase(59.9f, "0:59")]
        [TestCase(60f, "1:00")]
        [TestCase(754.9f, "12:34")]
        [TestCase(3600f, "60:00")]
        [TestCase(-5f, "0:00")]
        [TestCase(float.NaN, "0:00")]
        public void RunTimeReadsAsMinutesAndSeconds(float seconds, string expected)
        {
            Assert.AreEqual(expected, RunSummary.FormatTime(seconds));
        }

        [Test]
        public void AccuracyShowsPercentWithTheShotsBehindIt()
        {
            Assert.AreEqual("75% (30 of 40)", RunSummary.FormatAccuracy(40, 30));
            Assert.AreEqual("100% (3 of 3)", RunSummary.FormatAccuracy(3, 99), "Hits above the shots fired are capped.");
            Assert.AreEqual("0% (0 of 12)", RunSummary.FormatAccuracy(12, 0));
        }

        [Test]
        public void NoReportedShotsDoNotReadAsZeroPercent()
        {
            Assert.AreEqual("No shots recorded", RunSummary.FormatAccuracy(0, 0));
        }

        [Test]
        public void WonRunIsFactoryShutDownAndEverythingElseIsRecalled()
        {
            Assert.AreEqual("FACTORY SHUT DOWN", Summary(RunOutcome.Won).Headline);
            Assert.AreEqual("RECALLED", Summary(RunOutcome.Recalled).Headline);
            Assert.AreEqual("RECALLED", Summary(RunOutcome.None).Headline, "A run that never ended reads as a recall.");
        }

        [Test]
        public void SummaryClampsNumbersThatCannotBeRight()
        {
            RunSummary summary = Summary(seconds: -3f, cleared: 9, takedowns: -1, fired: 5, hit: 8);

            Assert.AreEqual(0f, summary.Seconds);
            Assert.AreEqual(RunSummary.ChapterCount, summary.ChaptersCleared);
            Assert.AreEqual(0, summary.Takedowns);
            Assert.AreEqual(5, summary.ShotsHit);
            Assert.AreEqual("4 of 4", summary.ChaptersText);
        }

        [Test]
        public void BreakdownSumsEachKindOfAwardFromTheHistory()
        {
            var rules = new ScoreRules();
            rules.SwitchRestored(1f);
            rules.SwitchRestored(2f);
            rules.TaskCompleted(3f);
            rules.Takedown(ToyFactory.Interfaces.AgentType.Guard, 1, 10f);
            rules.Takedown(ToyFactory.Interfaces.AgentType.Tracker, 2, 30f);

            RunSummary summary = Summary(rules: rules, score: rules.Score);

            Assert.AreEqual(3, summary.Breakdown.Count);
            Assert.AreEqual(ScoreReason.Switch, summary.Breakdown[0].Reason);
            Assert.AreEqual(2, summary.Breakdown[0].Count);
            Assert.AreEqual(2000, summary.Breakdown[0].Points);
            Assert.AreEqual(300, summary.Breakdown[1].Points);
            Assert.AreEqual(2, summary.Breakdown[2].Count);
            Assert.AreEqual(400, summary.Breakdown[2].Points, "250 for the Guard and 150 for the Tracker, 20 s apart so no combo.");
            Assert.AreEqual("Toys disabled", summary.Breakdown[2].Label);
            Assert.AreEqual(rules.Score, summary.Breakdown.Sum(line => line.Points), "The lines add up to the score.");
        }

        [Test]
        public void WinBonusLinesAppearEvenWhenTheyCameToZero()
        {
            var rules = new ScoreRules();
            rules.Win(2000f, 0f, 0, 0, 2000f);

            RunSummary summary = Summary(rules: rules, score: rules.Score);

            Assert.AreEqual(4, summary.Breakdown.Count);
            Assert.IsTrue(summary.Breakdown.Any(line => line.Reason == ScoreReason.WinTime && line.Points == 0));
            Assert.IsTrue(summary.Breakdown.Any(line => line.Reason == ScoreReason.WinAccuracy && line.Points == 0));
            Assert.AreEqual(2000, rules.Score);
        }

        [Test]
        public void EmptyRunHasAnEmptyBreakdown()
        {
            Assert.AreEqual(0, Summary(rules: new ScoreRules()).Breakdown.Count);
            Assert.AreEqual(0, Summary().Breakdown.Count, "No history at all.");
        }

        [Test]
        public void EveryKindOfAwardHasALabel()
        {
            foreach (ScoreReason reason in Enum.GetValues(typeof(ScoreReason)))
                Assert.IsNotEmpty(RunSummary.LabelFor(reason), reason.ToString());
        }
    }
}
