using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ToyFactory.Interfaces;
using ToyFactory.UI;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>The scoring table: each row, the combo, the repeat decay, the one-off bonuses and the grades.</summary>
    public class ScoreRulesTests
    {
        ScoreRules _rules;

        [SetUp]
        public void SetUp() => _rules = new ScoreRules();

        // ---- The simple rows

        [Test]
        public void ARestoredSwitchIsWorthAThousand()
        {
            ScoreAward award = _rules.SwitchRestored(10f);

            Assert.AreEqual(1000, award.Points);
            Assert.AreEqual(ScoreReason.Switch, award.Reason);
            Assert.AreEqual(1000, _rules.Score);
        }

        [Test]
        public void ATaskOrPowerCoreIsWorthThreeHundred()
        {
            Assert.AreEqual(300, _rules.TaskCompleted(1f).Points);
            Assert.AreEqual(300, _rules.Score);
        }

        [Test]
        public void ABatteryIsWorthFifty()
        {
            Assert.AreEqual(50, _rules.BatteryPickedUp(1f).Points);
        }

        [Test]
        public void PointsAddUpAndAreGroupedByReason()
        {
            _rules.TaskCompleted(1f);
            _rules.TaskCompleted(2f);
            _rules.SwitchRestored(3f);
            _rules.BatteryPickedUp(4f);

            Assert.AreEqual(600 + 1000 + 50, _rules.Score);
            Assert.AreEqual(600, _rules.PointsFor(ScoreReason.Task));
            Assert.AreEqual(1000, _rules.PointsFor(ScoreReason.Switch));
            Assert.AreEqual(50, _rules.PointsFor(ScoreReason.Battery));
            Assert.AreEqual(0, _rules.PointsFor(ScoreReason.Takedown));
            Assert.AreEqual(4, _rules.History.Count);
        }

        [Test]
        public void EveryAwardRaisesTheEventWithTheNewScoreAlreadyInPlace()
        {
            var seen = new List<(int points, int score)>();
            _rules.Awarded += a => seen.Add((a.Points, _rules.Score));

            _rules.TaskCompleted(1f);
            _rules.SwitchRestored(2f);

            Assert.AreEqual(new[] { (300, 300), (1000, 1300) }, seen.ToArray());
        }

        // ---- Takedowns

        [TestCase(AgentType.Tracker, 150)]
        [TestCase(AgentType.Guard, 250)]
        [TestCase(AgentType.Saboteur, 400)]
        [TestCase(AgentType.Captain, 600)]
        public void FirstTakedownIsWorthItsBase(AgentType type, int points)
        {
            ScoreAward award = _rules.Takedown(type, 1, 10f);

            Assert.AreEqual(points, award.Points);
            Assert.AreEqual(ScoreReason.Takedown, award.Reason);
            Assert.AreEqual(1, _rules.Takedowns);
        }

        // ---- The combo

        [Test]
        public void NextTakedownWithinSixSecondsRaisesTheMultiplier()
        {
            Assert.AreEqual(150, _rules.Takedown(AgentType.Tracker, 1, 0f).Points);
            Assert.AreEqual(250 * 2, _rules.Takedown(AgentType.Guard, 2, 3f).Points);
            Assert.AreEqual(150 * 3, _rules.Takedown(AgentType.Tracker, 3, 6f).Points);
            Assert.AreEqual(600 * 4, _rules.Takedown(AgentType.Captain, 4, 11f).Points);
        }

        [Test]
        public void ComboIsCappedAtFour()
        {
            for (int i = 0; i < 4; i++)
                _rules.Takedown(AgentType.Tracker, i, i * 1f);

            Assert.AreEqual(4, _rules.Combo);
            Assert.AreEqual(150 * 4, _rules.Takedown(AgentType.Tracker, 10, 5f).Points, "A fifth in the chain is still x4.");
            Assert.AreEqual(4, _rules.Combo);
        }

        [Test]
        public void ComboWindowIncludesExactlySixSecondsAndNotMore()
        {
            _rules.Takedown(AgentType.Tracker, 1, 0f);
            Assert.AreEqual(150 * 2, _rules.Takedown(AgentType.Tracker, 2, ScoreRules.ComboWindowSeconds).Points, "Exactly 6 s is in time.");

            _rules.Takedown(AgentType.Tracker, 3, 20f);
            ScoreAward late = _rules.Takedown(AgentType.Tracker, 4, 20f + ScoreRules.ComboWindowSeconds + 0.01f);
            Assert.AreEqual(150, late.Points, "Just past 6 s starts the chain again.");
        }

        [Test]
        public void ComboWindowRunsFromThePreviousTakedownNotTheFirst()
        {
            _rules.Takedown(AgentType.Tracker, 1, 0f);
            _rules.Takedown(AgentType.Tracker, 2, 5f);

            Assert.AreEqual(150 * 3, _rules.Takedown(AgentType.Tracker, 3, 10f).Points, "10 s after the first, 5 s after the last.");
        }

        [Test]
        public void ComboFallsBackToOneOnceTheWindowHasRunOut()
        {
            _rules.Takedown(AgentType.Tracker, 1, 0f);
            _rules.Takedown(AgentType.Tracker, 2, 2f);

            Assert.AreEqual(2, _rules.ComboAt(5f));
            Assert.AreEqual(1, _rules.ComboAt(2f + ScoreRules.ComboWindowSeconds + 0.1f));
        }

        [Test]
        public void ClockGoingBackwardsStartsANewChain()
        {
            _rules.Takedown(AgentType.Tracker, 1, 50f);

            Assert.AreEqual(150, _rules.Takedown(AgentType.Tracker, 2, 10f).Points);
        }

        [Test]
        public void ComboAppearsInTheFeedLabel()
        {
            _rules.Takedown(AgentType.Tracker, 1, 0f);

            Assert.AreEqual("Guard takedown x2", _rules.Takedown(AgentType.Guard, 2, 1f).Label);
            Assert.AreEqual("Tracker takedown", new ScoreRules().Takedown(AgentType.Tracker, 1, 0f).Label);
        }

        // ---- Repeat takedowns

        [Test]
        public void RepeatTakedownsOfTheSameAgentLoseTwentyFivePercentEachDownToAFloor()
        {
            // Far apart, so the combo stays at x1 and only the decay shows.
            Assert.AreEqual(250, _rules.Takedown(AgentType.Guard, 7, 0f).Points);
            Assert.AreEqual(188, _rules.Takedown(AgentType.Guard, 7, 100f).Points, "75% of 250 is 187.5, rounded up.");
            Assert.AreEqual(125, _rules.Takedown(AgentType.Guard, 7, 200f).Points);
            Assert.AreEqual(63, _rules.Takedown(AgentType.Guard, 7, 300f).Points, "25% of 250 is 62.5, rounded up.");
            Assert.AreEqual(63, _rules.Takedown(AgentType.Guard, 7, 400f).Points, "The floor holds at 25%.");
            Assert.AreEqual(63, _rules.Takedown(AgentType.Guard, 7, 500f).Points);
        }

        [Test]
        public void RepeatFactorIsLinearWithAFloor()
        {
            Assert.AreEqual(1f, _rules.RepeatFactor(3));
            _rules.Takedown(AgentType.Tracker, 3, 0f);
            Assert.AreEqual(0.75f, _rules.RepeatFactor(3), 1e-6f);
            _rules.Takedown(AgentType.Tracker, 3, 100f);
            Assert.AreEqual(0.5f, _rules.RepeatFactor(3), 1e-6f);
            _rules.Takedown(AgentType.Tracker, 3, 200f);
            Assert.AreEqual(0.25f, _rules.RepeatFactor(3), 1e-6f);
            _rules.Takedown(AgentType.Tracker, 3, 300f);
            Assert.AreEqual(0.25f, _rules.RepeatFactor(3), 1e-6f);
        }

        [Test]
        public void RepeatsAreCountedPerAgentNotPerKind()
        {
            _rules.Takedown(AgentType.Tracker, 1, 0f);

            Assert.AreEqual(150, _rules.Takedown(AgentType.Tracker, 2, 100f).Points, "A different Tracker is a first takedown.");
            Assert.AreEqual(250, _rules.Takedown(AgentType.Guard, 3, 200f).Points);
        }

        [Test]
        public void ComboAndRepeatDecayMultiplyWhateverTheOrder()
        {
            // Guard 7 a second time (75%) as the second takedown of a chain (x2): 250 x 0.75 x 2 = 375.
            _rules.Takedown(AgentType.Guard, 7, 0f);
            _rules.Takedown(AgentType.Tracker, 1, 100f);
            ScoreAward both = _rules.Takedown(AgentType.Guard, 7, 101f);

            Assert.AreEqual(375, both.Points);
        }

        [Test]
        public void ComboOfThreeOnARepeatRoundsOnce()
        {
            // 150 x 0.75 x 3 = 337.5: rounded once, at the end, to 338 (and not 113 x 3 = 339).
            _rules.Takedown(AgentType.Tracker, 1, 0f);
            _rules.Takedown(AgentType.Tracker, 2, 100f);
            _rules.Takedown(AgentType.Guard, 3, 101f);
            ScoreAward award = _rules.Takedown(AgentType.Tracker, 1, 102f);

            Assert.AreEqual(150 * 0.75f * 3f, 337.5f);
            Assert.AreEqual(338, award.Points);
        }

        // ---- All four Saboteurs

        [Test]
        public void DestroyingAllFourSaboteursAwardsTheBonusOnceRightAfterTheFourth()
        {
            for (int i = 1; i <= 3; i++)
                _rules.Takedown(AgentType.Saboteur, i, i * 100f);
            Assert.AreEqual(0, _rules.PointsFor(ScoreReason.AllSaboteurs));

            _rules.Takedown(AgentType.Saboteur, 4, 400f);

            Assert.AreEqual(1000, _rules.PointsFor(ScoreReason.AllSaboteurs));
            Assert.AreEqual(ScoreReason.AllSaboteurs, _rules.History.Last().Reason, "The bonus follows the fourth takedown.");
        }

        [Test]
        public void AllSaboteursBonusFiresOnlyOnce()
        {
            for (int i = 1; i <= 4; i++)
                _rules.Takedown(AgentType.Saboteur, i, i * 100f);
            _rules.Takedown(AgentType.Saboteur, 4, 900f);
            _rules.Takedown(AgentType.Saboteur, 1, 1000f);

            Assert.AreEqual(1000, _rules.PointsFor(ScoreReason.AllSaboteurs));
        }

        [Test]
        public void ThreeSaboteursAndAnotherAgentIsNotAllFour()
        {
            for (int i = 1; i <= 3; i++)
                _rules.Takedown(AgentType.Saboteur, i, i * 100f);
            _rules.Takedown(AgentType.Guard, 4, 400f);

            Assert.AreEqual(0, _rules.PointsFor(ScoreReason.AllSaboteurs));
        }

        [Test]
        public void TheSameSaboteurTwiceIsNotTwoSaboteurs()
        {
            for (int i = 0; i < 4; i++)
                _rules.Takedown(AgentType.Saboteur, 1, i * 100f);

            Assert.AreEqual(0, _rules.PointsFor(ScoreReason.AllSaboteurs));
        }

        [Test]
        public void AllSaboteursBonusIsNotMultipliedByTheCombo()
        {
            for (int i = 1; i <= 4; i++)
                _rules.Takedown(AgentType.Saboteur, i, i * 1f);

            Assert.AreEqual(1000, _rules.PointsFor(ScoreReason.AllSaboteurs));
        }

        // ---- The win bonus

        [Test]
        public void WinBonusIsFourLinesThatAddUp()
        {
            IReadOnlyList<ScoreAward> awards = _rules.Win(300f, 80f, 20, 10, 300f);

            Assert.AreEqual(4, awards.Count);
            Assert.AreEqual(2000, awards[0].Points);
            Assert.AreEqual(3000 - 3 * 300, awards[1].Points, "Time bonus.");
            Assert.AreEqual(800, awards[2].Points, "10 a point of HP.");
            Assert.AreEqual(15 * 50, awards[3].Points, "15 a percent of accuracy at 50%.");
            Assert.AreEqual(2000 + 2100 + 800 + 750, _rules.Score);
            Assert.IsTrue(_rules.HasWon);
        }

        [TestCase(0f, 3000)]
        [TestCase(100f, 2700)]
        [TestCase(1000f, 0)]
        [TestCase(5000f, 0)]
        [TestCase(-5f, 3000)]
        public void TimeBonusFallsThreeAPointASecondToZero(float seconds, int expected)
        {
            Assert.AreEqual(expected, ScoreRules.TimeBonus(seconds));
        }

        [TestCase(100f, 1000)]
        [TestCase(37.5f, 375)]
        [TestCase(0f, 0)]
        [TestCase(-3f, 0)]
        public void IntegrityBonusIsTenAPointOfHp(float hp, int expected)
        {
            Assert.AreEqual(expected, ScoreRules.IntegrityBonus(hp));
        }

        [TestCase(10, 10, 1500)]
        [TestCase(10, 5, 750)]
        [TestCase(40, 30, 1125)]
        [TestCase(10, 0, 0)]
        [TestCase(10, 20, 1500)]
        public void AccuracyBonusIsFifteenAPercent(int fired, int hit, int expected)
        {
            Assert.AreEqual(expected, ScoreRules.AccuracyBonus(fired, hit));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(9)]
        public void AccuracyNeedsAtLeastTenShots(int fired)
        {
            Assert.AreEqual(0, ScoreRules.AccuracyBonus(fired, fired), "A lucky single shot is not 100%.");
        }

        [Test]
        public void WinBonusCountsOnlyOnce()
        {
            _rules.Win(100f, 100f, 0, 0, 100f);
            int score = _rules.Score;

            Assert.AreEqual(0, _rules.Win(100f, 100f, 0, 0, 101f).Count);
            Assert.AreEqual(score, _rules.Score);
        }

        [Test]
        public void NaNInputsGiveNoBonusInsteadOfBreakingTheScore()
        {
            Assert.AreEqual(0, ScoreRules.TimeBonus(float.NaN));
            Assert.AreEqual(0, ScoreRules.IntegrityBonus(float.NaN));
        }

        // ---- The grade

        [TestCase(14000, ScoreGrade.S)]
        [TestCase(13999, ScoreGrade.A)]
        [TestCase(11000, ScoreGrade.A)]
        [TestCase(10999, ScoreGrade.B)]
        [TestCase(8000, ScoreGrade.B)]
        [TestCase(7999, ScoreGrade.C)]
        [TestCase(5000, ScoreGrade.C)]
        [TestCase(4999, ScoreGrade.D)]
        [TestCase(0, ScoreGrade.D)]
        [TestCase(50000, ScoreGrade.S)]
        public void GradeChangesAtTheExactThresholds(int score, ScoreGrade expected)
        {
            Assert.AreEqual(expected, ScoreRules.GradeFor(score));
        }

        [Test]
        public void GradeFollowsTheRunningScore()
        {
            Assert.AreEqual(ScoreGrade.D, _rules.Grade);
            for (int i = 0; i < 17; i++)
                _rules.TaskCompleted(i);

            Assert.AreEqual(5100, _rules.Score);
            Assert.AreEqual(ScoreGrade.C, _rules.Grade);
        }

        // ---- A whole run, and starting again

        [Test]
        public void AFullPlayThroughReachesTheTopGrade()
        {
            for (int i = 0; i < 17; i++)
                _rules.TaskCompleted(i * 10f);
            for (int i = 0; i < 3; i++)
                _rules.SwitchRestored(200f + i);
            for (int i = 1; i <= 4; i++)
                _rules.Takedown(AgentType.Saboteur, i, 300f + i * 10f);
            _rules.Takedown(AgentType.Captain, 9, 500f);
            _rules.Win(600f, 90f, 40, 36, 600f);

            Assert.GreaterOrEqual(_rules.Score, 14000);
            Assert.AreEqual(ScoreGrade.S, _rules.Grade);
        }

        [Test]
        public void ResetClearsEverythingForANewRun()
        {
            for (int i = 1; i <= 4; i++)
                _rules.Takedown(AgentType.Saboteur, i, i * 1f);
            _rules.Win(100f, 100f, 10, 10, 100f);

            _rules.Reset();

            Assert.AreEqual(0, _rules.Score);
            Assert.AreEqual(1, _rules.Combo);
            Assert.AreEqual(0, _rules.Takedowns);
            Assert.AreEqual(0, _rules.History.Count);
            Assert.IsFalse(_rules.HasWon);
            Assert.AreEqual(1f, _rules.RepeatFactor(1));
            Assert.AreEqual(0, _rules.PointsFor(ScoreReason.Takedown));

            // The one-off bonuses are available again.
            for (int i = 1; i <= 4; i++)
                _rules.Takedown(AgentType.Saboteur, i, i * 1f);
            Assert.AreEqual(1000, _rules.PointsFor(ScoreReason.AllSaboteurs));
            Assert.AreEqual(4, _rules.Win(10f, 10f, 0, 0, 10f).Count);
        }

        [Test]
        public void UnknownAgentTypeIsRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ScoreRules.TakedownBase((AgentType)99));
        }
    }
}
