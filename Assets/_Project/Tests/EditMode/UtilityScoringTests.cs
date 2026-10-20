using System;
using NUnit.Framework;
using ToyFactory.AI.Agents.Saboteur;

namespace ToyFactory.Tests.EditMode
{
    public class UtilityScoringTests
    {
        const float Tolerance = 1e-4f;

        static Consideration Direct(string name) => Consideration.Direct(name);

        [Test]
        public void FourConsiderationsOfPointNineGiveHandCalculatedScore()
        {
            var action = new UtilityAction(SaboteurActionKind.CloseDoor,
                Direct("a"), Direct("b"), Direct("c"), Direct("d"));

            ActionScore score = action.Evaluate(new[] { 0.9f, 0.9f, 0.9f, 0.9f });

            Assert.AreEqual(0.6561f, score.Raw, Tolerance);
            Assert.AreEqual(0.8253f, score.BaseScore, Tolerance);
        }

        [Test]
        public void SingleConsiderationIsNotCompensated()
        {
            Assert.AreEqual(0.4f, UtilityAction.Compensate(0.4f, 1), Tolerance);
        }

        [TestCase(0f)]
        [TestCase(1f)]
        public void CompensationKeepsTheEndpoints(float raw)
        {
            Assert.AreEqual(raw, UtilityAction.Compensate(raw, 4), Tolerance);
        }

        [Test]
        public void CompensationNeverLowersTheScoreOrExceedsOne()
        {
            for (int n = 1; n <= 6; n++)
            {
                for (float raw = 0f; raw <= 1f; raw += 0.05f)
                {
                    float result = UtilityAction.Compensate(raw, n);
                    Assert.GreaterOrEqual(result, raw - Tolerance);
                    Assert.LessOrEqual(result, 1f + Tolerance);
                }
            }
        }

        [Test]
        public void CompensationRejectsZeroConsiderations()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => UtilityAction.Compensate(0.5f, 0));
        }

        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        [TestCase(float.NaN)]
        public void CompensationRejectsOutOfRangeRawScores(float raw)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => UtilityAction.Compensate(raw, 2));
        }

        [Test]
        public void ZeroConsiderationVetoesTheAction()
        {
            var action = new UtilityAction(SaboteurActionKind.StealBattery, Direct("a"), Direct("claimed"));

            ActionScore score = action.Evaluate(new[] { 0.9f, 0f });

            Assert.IsTrue(score.Vetoed);
            Assert.AreEqual(0f, score.BaseScore);
        }

        [Test]
        public void IdleIsAConstantWithNoConsiderations()
        {
            var idle = UtilityAction.Constant(SaboteurActionKind.Idle, 0.1f);

            ActionScore score = idle.Evaluate(Array.Empty<float>());

            Assert.AreEqual(0.1f, score.BaseScore, Tolerance);
            Assert.AreEqual(0, idle.ConsiderationCount);
        }

        [Test]
        public void ActionWithoutConsiderationsIsRejectedUnlessConstant()
        {
            Assert.Throws<ArgumentException>(() => new UtilityAction(SaboteurActionKind.CloseDoor));
        }

        [Test]
        public void InputCountMustMatchConsiderationCount()
        {
            var action = new UtilityAction(SaboteurActionKind.AttackPlayer, Direct("a"), Direct("b"));

            Assert.Throws<ArgumentException>(() => action.Evaluate(new[] { 0.5f }));
        }

        [Test]
        public void ConsiderationScoresAreReportedForTheDebugView()
        {
            var action = new UtilityAction(SaboteurActionKind.AttackPlayer, Direct("a"), Direct("b"));
            var scores = new float[2];

            action.Evaluate(new[] { 0.25f, 0.75f }, scores);

            Assert.AreEqual(0.25f, scores[0], Tolerance);
            Assert.AreEqual(0.75f, scores[1], Tolerance);
        }

        [Test]
        public void NonFiniteInputIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Direct("a").Evaluate(float.NaN));
        }

        [Test]
        public void CurveResultOutsideZeroToOneIsRejected()
        {
            var broken = new Consideration("broken", _ => 1.5f);

            Assert.Throws<InvalidOperationException>(() => broken.Evaluate(0.5f));
        }

        [Test]
        public void ConsiderationUsesItsCurve()
        {
            var quadratic = new Consideration("range", x => ResponseCurve.Power(x, 2f));

            Assert.AreEqual(0.25f, quadratic.Evaluate(0.5f), Tolerance);
        }

        [Test]
        public void ConstantRejectsAZeroScore()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => UtilityAction.Constant(SaboteurActionKind.Idle, 0f));
        }
    }
}
