using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class AStarSearchTests
    {
        const float Tolerance = 1e-3f;
        static readonly float Diagonal = BaseCostModel.DiagonalCost;

        static PathResult Search(IGridGraph grid, Vector2Int start, Vector2Int goal) =>
            new AStarSearch(grid).FindPath(start, goal, BaseCostModel.Instance);

        static float PathCost(List<Vector2Int> cells)
        {
            float total = 0f;
            for (int i = 1; i < cells.Count; i++)
                total += BaseCostModel.Instance.StepCost(cells[i - 1], cells[i]);
            return total;
        }

        [Test]
        public void StraightLineOnOpenGridIsOptimal()
        {
            var grid = TestGrid.FromRows(".....");

            PathResult result = Search(grid, new Vector2Int(0, 0), new Vector2Int(4, 0));

            Assert.IsTrue(result.Found);
            Assert.AreEqual(5, result.Cells.Count);
            Assert.AreEqual(4f, PathCost(result.Cells), Tolerance);
        }

        [Test]
        public void DiagonalOnOpenGridIsOptimal()
        {
            var grid = TestGrid.FromRows("....", "....", "....", "....");

            PathResult result = Search(grid, new Vector2Int(0, 0), new Vector2Int(3, 3));

            Assert.IsTrue(result.Found);
            Assert.AreEqual(4, result.Cells.Count);
            Assert.AreEqual(3f * Diagonal, PathCost(result.Cells), Tolerance);
        }

        [Test]
        public void RoutesAroundAWallWithoutCuttingCorners()
        {
            var grid = TestGrid.FromRows(
                ".....",
                ".###.",
                ".....");

            PathResult result = Search(grid, new Vector2Int(0, 1), new Vector2Int(4, 1));

            // Both diagonals past the wall's ends would cut a corner, so the best route is
            // six straight steps around the top or bottom.
            Assert.IsTrue(result.Found);
            Assert.AreEqual(6f, PathCost(result.Cells), Tolerance);
        }

        [Test]
        public void DiagonalBetweenTwoBlockedCellsIsNotAllowed()
        {
            var grid = TestGrid.FromRows(
                ".#",
                "#.");

            PathResult result = Search(grid, new Vector2Int(0, 0), new Vector2Int(1, 1));

            Assert.IsFalse(result.Found);
            Assert.IsNull(result.Cells);
        }

        [Test]
        public void WalledOffGoalIsNotFound()
        {
            var grid = TestGrid.FromRows(
                ".....",
                ".###.",
                ".#.#.",
                ".###.",
                ".....");

            PathResult result = Search(grid, new Vector2Int(0, 0), new Vector2Int(2, 2));

            Assert.IsFalse(result.Found);
            Assert.Greater(result.NodesExpanded, 0);
        }

        [Test]
        public void StartEqualToGoalReturnsSingleCellPath()
        {
            var grid = TestGrid.FromRows("...", "...", "...");
            var cell = new Vector2Int(1, 1);

            PathResult result = Search(grid, cell, cell);

            Assert.IsTrue(result.Found);
            CollectionAssert.AreEqual(new[] { cell }, result.Cells);
        }

        [Test]
        public void BlockedStartOrGoalIsNotFound()
        {
            var grid = TestGrid.FromRows("#.#");

            Assert.IsFalse(Search(grid, new Vector2Int(0, 0), new Vector2Int(1, 0)).Found);
            Assert.IsFalse(Search(grid, new Vector2Int(1, 0), new Vector2Int(2, 0)).Found);
        }

        [Test]
        public void ResultReportsTheGraphVersion()
        {
            var grid = TestGrid.FromRows("....");
            grid.SetWalkable(new Vector2Int(3, 0), false);
            grid.SetWalkable(new Vector2Int(3, 0), true);

            PathResult result = Search(grid, new Vector2Int(0, 0), new Vector2Int(3, 0));

            Assert.AreEqual(2, result.GraphVersion);
        }

        [Test]
        public void ReusedSearchReplansAroundANewlyBlockedCell()
        {
            var grid = TestGrid.FromRows(".....", ".....", ".....");
            var search = new AStarSearch(grid);
            var start = new Vector2Int(0, 1);
            var goal = new Vector2Int(4, 1);
            var blocked = new Vector2Int(2, 1);

            PathResult before = search.FindPath(start, goal, BaseCostModel.Instance);
            grid.SetWalkable(blocked, false);
            PathResult after = search.FindPath(start, goal, BaseCostModel.Instance);

            CollectionAssert.Contains(before.Cells, blocked);
            CollectionAssert.DoesNotContain(after.Cells, blocked);
            Assert.AreEqual(ReferenceDijkstraCost(grid, start, goal), PathCost(after.Cells), Tolerance);
        }

        [Test]
        public void CostMatchesReferenceDijkstraOnFiftyRandomGrids()
        {
            var rng = new System.Random(12345);
            var neighbours = new List<Vector2Int>();

            for (int run = 0; run < 50; run++)
            {
                TestGrid grid = TestGrid.Random(20, 20, 0.25f, rng);
                Vector2Int start = RandomWalkableCell(grid, rng);
                Vector2Int goal = RandomWalkableCell(grid, rng);

                PathResult result = Search(grid, start, goal);
                float expected = ReferenceDijkstraCost(grid, start, goal);

                Assert.AreEqual(!float.IsPositiveInfinity(expected), result.Found, $"Run {run}: reachability differs.");
                if (!result.Found)
                    continue;

                Assert.AreEqual(expected, PathCost(result.Cells), Tolerance, $"Run {run}: path is not optimal.");
                Assert.AreEqual(start, result.Cells[0], $"Run {run}: path does not start at the start.");
                Assert.AreEqual(goal, result.Cells[result.Cells.Count - 1], $"Run {run}: path does not end at the goal.");
                for (int i = 1; i < result.Cells.Count; i++)
                {
                    grid.GetNeighbours(result.Cells[i - 1], neighbours);
                    CollectionAssert.Contains(neighbours, result.Cells[i], $"Run {run}: step {i} is not a legal move.");
                }
            }
        }

        static Vector2Int RandomWalkableCell(TestGrid grid, System.Random rng)
        {
            while (true)
            {
                var cell = new Vector2Int(rng.Next(grid.Width), rng.Next(grid.Height));
                if (grid.IsTraversable(cell))
                    return cell;
            }
        }

        // Deliberately simple uniform-cost search (no heuristic, no heap) used only as a
        // known-correct answer to check A* against.
        static float ReferenceDijkstraCost(IGridGraph grid, Vector2Int start, Vector2Int goal)
        {
            var dist = new Dictionary<Vector2Int, float> { [start] = 0f };
            var done = new HashSet<Vector2Int>();
            var neighbours = new List<Vector2Int>();

            while (true)
            {
                bool found = false;
                Vector2Int best = default;
                float bestDist = float.PositiveInfinity;
                foreach (KeyValuePair<Vector2Int, float> entry in dist)
                {
                    if (!done.Contains(entry.Key) && entry.Value < bestDist)
                    {
                        best = entry.Key;
                        bestDist = entry.Value;
                        found = true;
                    }
                }

                if (!found)
                    return float.PositiveInfinity;
                if (best == goal)
                    return bestDist;

                done.Add(best);
                grid.GetNeighbours(best, neighbours);
                foreach (Vector2Int next in neighbours)
                {
                    float nextDist = bestDist + BaseCostModel.Instance.StepCost(best, next);
                    if (!dist.TryGetValue(next, out float current) || nextDist < current)
                        dist[next] = nextDist;
                }
            }
        }
    }
}
