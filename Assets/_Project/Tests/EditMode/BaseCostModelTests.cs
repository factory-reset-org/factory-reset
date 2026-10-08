using System;
using NUnit.Framework;
using ToyFactory.AI.Core.Search;
using UnityEngine;

namespace ToyFactory.Tests.EditMode
{
    public class BaseCostModelTests
    {
        const float Tolerance = 1e-5f;

        [TestCase(1, 0, false)]
        [TestCase(-1, 0, false)]
        [TestCase(0, 1, false)]
        [TestCase(0, -1, false)]
        [TestCase(1, 1, true)]
        [TestCase(1, -1, true)]
        [TestCase(-1, 1, true)]
        [TestCase(-1, -1, true)]
        public void StepCostsMatchEveryAdjacentDirection(int x, int y, bool diagonal)
        {
            var from = new Vector2Int(-3, 2);
            var to = from + new Vector2Int(x, y);
            float expected = diagonal ? (float)Math.Sqrt(2) : 1f;
            Assert.AreEqual(1f, BaseCostModel.StraightCost);
            Assert.AreEqual(expected, BaseCostModel.Instance.StepCost(from, to), Tolerance);
            Assert.AreEqual(expected, BaseCostModel.Instance.StepCost(to, from), Tolerance);
        }

        [TestCase(0, 0, 0, 0)]
        [TestCase(4, 0, 4, 0)]
        [TestCase(0, -4, 4, 0)]
        [TestCase(3, 3, 0, 3)]
        [TestCase(-3, -3, 0, 3)]
        [TestCase(5, -2, 3, 2)]
        [TestCase(-2, 5, 3, 2)]
        public void OctileDistanceMatchesOpenGridSteps(int x, int y, int straight, int diagonal)
        {
            var from = new Vector2Int(-7, 3);
            var to = from + new Vector2Int(x, y);
            float expected = straight + diagonal * (float)Math.Sqrt(2);
            Assert.AreEqual(expected, BaseCostModel.OctileDistance(from, to), Tolerance);
            Assert.AreEqual(expected, BaseCostModel.OctileDistance(to, from), Tolerance);
        }

        [Test]
        public void OctileDistanceIsConsistentAcrossEveryAdjacentStep()
        {
            var goal = new Vector2Int(7, -2);
            for (int y = -3; y <= 3; y++)
            for (int x = -3; x <= 3; x++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var from = new Vector2Int(x, y);
                var to = from + new Vector2Int(dx, dy);
                Assert.LessOrEqual(BaseCostModel.OctileDistance(from, goal),
                    BaseCostModel.Instance.StepCost(from, to) + BaseCostModel.OctileDistance(to, goal) + Tolerance);
            }
        }

        [Test]
        public void SharedMovementMathAllocatesZeroBytes()
        {
            var from = new Vector2Int(1, 2);
            var to = new Vector2Int(2, 3);
            float checksum = 0;
            for (int i = 0; i < 100; i++)
                checksum += BaseCostModel.Instance.StepCost(from, to) + BaseCostModel.OctileDistance(from, to);
            int allocated = GcAllocations.Count(() =>
            {
                for (int i = 0; i < 1000; i++)
                    checksum += BaseCostModel.Instance.StepCost(from, to) + BaseCostModel.OctileDistance(from, to);
            });
            Assert.AreEqual(0, allocated);
            Assert.Greater(checksum, 0);
        }
    }
}
