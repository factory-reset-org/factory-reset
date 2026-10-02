using System;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;

namespace ToyFactory.Tests.EditMode
{
    public class TacticalCostModelTests
    {
        const float Tolerance = 1e-4f;
        static readonly Vector2Int Origin = new Vector2Int(0, 0);
        static readonly Vector2Int Orthogonal = new Vector2Int(1, 0);
        static readonly Vector2Int Diagonal = new Vector2Int(1, 1);

        [Test]
        public void HiddenStepCostsTheSameAsTheBaseModel()
        {
            var model = new TacticalCostModel(_ => false);

            Assert.AreEqual(1f, model.StepCost(Origin, Orthogonal), Tolerance);
            Assert.AreEqual(1.41421356f, model.StepCost(Origin, Diagonal), Tolerance);
        }

        [Test]
        public void ExposedStepCostsBaseTimesOnePlusLambda()
        {
            var model = new TacticalCostModel(_ => true, lambda: 3f);

            Assert.AreEqual(1f * 4f, model.StepCost(Origin, Orthogonal), Tolerance);
            Assert.AreEqual(1.41421356f * 4f, model.StepCost(Origin, Diagonal), Tolerance);
        }

        [Test]
        public void DefaultLambdaIsThree()
        {
            var model = new TacticalCostModel(_ => true);

            Assert.AreEqual(4f, model.StepCost(Origin, Orthogonal), Tolerance);
        }

        [Test]
        public void CustomLambdaIsRespected()
        {
            var model = new TacticalCostModel(_ => true, lambda: 1f);

            Assert.AreEqual(2f, model.StepCost(Origin, Orthogonal), Tolerance);
        }

        [Test]
        public void ExposureIsQueriedWithTheDestinationCell()
        {
            Vector2Int? seen = null;
            var model = new TacticalCostModel(cell =>
            {
                seen = cell;
                return false;
            });

            model.StepCost(Origin, Orthogonal);

            Assert.AreEqual(Orthogonal, seen);
        }

        [Test]
        public void NullExposureDelegateThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new TacticalCostModel(null));
        }
    }
}
