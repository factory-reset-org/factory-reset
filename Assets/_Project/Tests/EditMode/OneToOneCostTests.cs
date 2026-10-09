using System;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class OneToOneCostTests
    {
        const float Tolerance = 1e-3f;

        static GridGraph RandomGrid(int width, int height, float blockedChance, System.Random rng)
        {
            var grid = new GridGraph(width, height, Vector3.zero);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (rng.NextDouble() < blockedChance)
                        grid.SetWalkable(new Vector2Int(x, y), false);
            return grid;
        }

        static Vector2Int RandomWalkableCell(GridGraph grid, System.Random rng)
        {
            while (true)
            {
                var cell = new Vector2Int(rng.Next(grid.Width), rng.Next(grid.Height));
                if (grid.IsTraversable(cell))
                    return cell;
            }
        }

        // A tactical-style model: base cost plus a penalty for stepping into the middle column.
        sealed class MiddleColumnPenalty : ICostModel
        {
            readonly int _column;
            public MiddleColumnPenalty(int column) { _column = column; }

            public float StepCost(Vector2Int from, Vector2Int to) =>
                BaseCostModel.Instance.StepCost(from, to) + (to.x == _column ? 3f : 0f);
        }

        [Test]
        public void CostMatchesTheDijkstraFieldOnFiftyRandomGrids()
        {
            var rng = new System.Random(4242);
            for (int run = 0; run < 50; run++)
            {
                GridGraph grid = RandomGrid(20, 20, 0.3f, rng);
                Vector2Int from = RandomWalkableCell(grid, rng);
                var field = new DijkstraField(grid);
                field.Compute(from, BaseCostModel.Instance);
                var query = new OneToOneCost(grid);

                for (int target = 0; target < 5; target++)
                {
                    Vector2Int to = RandomWalkableCell(grid, rng);
                    // Unreachable targets included: both answer infinity.
                    Assert.AreEqual(field.Cost(to), query.Compute(from, to, BaseCostModel.Instance), Tolerance,
                        $"Run {run}, {from} to {to}");
                }
            }
        }

        [Test]
        public void CostMatchesTheFieldUnderAnAddedPenaltyModel()
        {
            var rng = new System.Random(8);
            GridGraph grid = RandomGrid(16, 16, 0.2f, rng);
            var model = new MiddleColumnPenalty(8);
            var query = new OneToOneCost(grid);

            for (int run = 0; run < 20; run++)
            {
                Vector2Int from = RandomWalkableCell(grid, rng);
                Vector2Int to = RandomWalkableCell(grid, rng);
                var field = new DijkstraField(grid);
                field.Compute(from, model);

                Assert.AreEqual(field.Cost(to), query.Compute(from, to, model), Tolerance, $"{from} to {to}");
            }
        }

        [Test]
        public void SameCellIsFreeAndBlockedOrOffGridCellsAreUnreachable()
        {
            var grid = new GridGraph(5, 5, Vector3.zero);
            grid.SetWalkable(new Vector2Int(2, 2), false);
            var query = new OneToOneCost(grid);

            Assert.AreEqual(0f, query.Compute(new Vector2Int(1, 1), new Vector2Int(1, 1), BaseCostModel.Instance));
            Assert.IsTrue(float.IsPositiveInfinity(query.Compute(new Vector2Int(0, 0), new Vector2Int(2, 2), BaseCostModel.Instance)));
            Assert.IsTrue(float.IsPositiveInfinity(query.Compute(new Vector2Int(2, 2), new Vector2Int(0, 0), BaseCostModel.Instance)));
            Assert.IsTrue(float.IsPositiveInfinity(query.Compute(new Vector2Int(0, 0), new Vector2Int(9, 0), BaseCostModel.Instance)));
        }

        [Test]
        public void TheBoundRulesOutLongerRoutesOnly()
        {
            // A wall with one gap at the top: from (0,0) to (4,0) is 4 straight, but the wall
            // forces a walk up to the gap and back down.
            var grid = new GridGraph(5, 6, Vector3.zero);
            for (int y = 0; y < 5; y++)
                grid.SetWalkable(new Vector2Int(2, y), false);
            var query = new OneToOneCost(grid);
            var from = new Vector2Int(0, 0);
            var to = new Vector2Int(4, 0);

            float exact = query.Compute(from, to, BaseCostModel.Instance);
            Assert.Greater(exact, 4f);

            Assert.AreEqual(exact, query.Compute(from, to, BaseCostModel.Instance, exact + Tolerance), Tolerance, "A bound at the cost still finds it.");
            Assert.IsTrue(float.IsPositiveInfinity(query.Compute(from, to, BaseCostModel.Instance, exact - 0.5f)), "Every route is over the bound.");
            Assert.IsTrue(float.IsPositiveInfinity(query.Compute(from, to, BaseCostModel.Instance, 3f)), "Even the straight line is over the bound.");
            Assert.AreEqual(0, query.NodesExpanded, "Ruled out by the octile distance alone, with no search.");
        }

        [Test]
        public void TheBoundKeepsAnUnreachableTargetCheap()
        {
            var grid = new GridGraph(60, 60, Vector3.zero);
            // Wall the target in.
            var target = new Vector2Int(55, 55);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dy != 0)
                        grid.SetWalkable(target + new Vector2Int(dx, dy), false);
            var query = new OneToOneCost(grid);
            var from = new Vector2Int(45, 45);

            query.Compute(from, target, BaseCostModel.Instance);
            int unbounded = query.NodesExpanded;
            query.Compute(from, target, BaseCostModel.Instance, 20f);
            int bounded = query.NodesExpanded;

            Assert.Greater(unbounded, 3000, "Unbounded, it searches everything reachable before giving up.");
            Assert.Less(bounded, unbounded / 5);
        }

        [Test]
        public void ANearbyTargetCostsFarLessThanAWholeField()
        {
            var grid = new GridGraph(80, 80, Vector3.zero);
            var query = new OneToOneCost(grid);
            var field = new DijkstraField(grid);
            var from = new Vector2Int(40, 40);
            var to = new Vector2Int(60, 48);

            float cost = query.Compute(from, to, BaseCostModel.Instance);
            field.Compute(from, BaseCostModel.Instance);

            Assert.AreEqual(field.Cost(to), cost, Tolerance);
            Assert.Less(query.NodesExpanded, field.NodesExpanded / 10);
        }

        [Test]
        public void AQueryAllocatesNothing()
        {
            var rng = new System.Random(5);
            GridGraph grid = RandomGrid(30, 30, 0.2f, rng);
            var query = new OneToOneCost(grid);
            Vector2Int from = RandomWalkableCell(grid, rng);
            Vector2Int to = RandomWalkableCell(grid, rng);
            query.Compute(from, to, BaseCostModel.Instance); // warm up

            int allocations = GcAllocations.Count(() =>
            {
                for (int i = 0; i < 20; i++)
                    query.Compute(from, to, BaseCostModel.Instance);
            });

            Assert.AreEqual(0, allocations);
        }

        [Test]
        public void BadArgumentsAreRejected()
        {
            var query = new OneToOneCost(new GridGraph(3, 3, Vector3.zero));
            Assert.Throws<ArgumentNullException>(() => query.Compute(Vector2Int.zero, Vector2Int.one, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => query.Compute(Vector2Int.zero, Vector2Int.one, BaseCostModel.Instance, -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => query.Compute(Vector2Int.zero, Vector2Int.one, BaseCostModel.Instance, float.NaN));
            Assert.Throws<ArgumentNullException>(() => new OneToOneCost(null));
        }
    }
}
