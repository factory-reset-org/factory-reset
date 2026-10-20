using System;
using NUnit.Framework;
using ToyFactory.AI.Agents.Saboteur;

namespace ToyFactory.Tests.EditMode
{
    public class UtilityCurveTests
    {
        const float Tolerance = 1e-5f;

        [TestCase(0f, 0f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(1f, 1f)]
        public void IdentityLinearMatchesNormalisedInput(float input, float expected)
        {
            Assert.AreEqual(expected, ResponseCurve.Linear(input, 1f, 0f), Tolerance);
        }

        [TestCase(0f, 0f)]
        [TestCase(0.5f, 0.25f)]
        [TestCase(1f, 1f)]
        public void QuadraticPowerMatchesExpectedFalloff(float input, float expected)
        {
            Assert.AreEqual(expected, ResponseCurve.Power(input, 2f), Tolerance);
        }

        [TestCase(0f, 0.006692851f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(1f, 0.993307149f)]
        public void LogisticMatchesExpectedValues(float input, float expected)
        {
            Assert.AreEqual(expected, ResponseCurve.Logistic(input, 10f, 0.5f), Tolerance);
        }

        [TestCase(0f, 1f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(1f, 0f)]
        public void InverseReversesNormalisedInput(float input, float expected)
        {
            Assert.AreEqual(expected, ResponseCurve.Inverse(input), Tolerance);
        }

        [Test]
        public void LowerRemainingAmmoHasGreaterBatteryDesirability()
        {
            float lowAmmo = ResponseCurve.Logistic(1f - 0.1f, 10f, 0.7f);
            float highAmmo = ResponseCurve.Logistic(1f - 0.9f, 10f, 0.7f);

            Assert.Greater(lowAmmo, highAmmo);
        }

        [Test]
        public void FiniteInputsAndOutputsAreClamped()
        {
            Assert.AreEqual(0f, ResponseCurve.Linear(-2f, 1f, 0f));
            Assert.AreEqual(1f, ResponseCurve.Linear(2f, 1f, 0f));
            Assert.AreEqual(1f, ResponseCurve.Linear(0.5f, 4f, 0f));
            Assert.AreEqual(0f, ResponseCurve.Linear(0.5f, 1f, -2f));
            Assert.AreEqual(0f, ResponseCurve.Power(-2f, 2f));
            Assert.AreEqual(1f, ResponseCurve.Inverse(-2f));
        }

        [Test]
        public void InvalidInputsAndParametersAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ResponseCurve.Inverse(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => ResponseCurve.Linear(0.5f, float.PositiveInfinity, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => ResponseCurve.Power(0.5f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => ResponseCurve.Logistic(0.5f, -1f, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => ResponseCurve.Logistic(0.5f, 10f, 2f));
        }
    }
}
