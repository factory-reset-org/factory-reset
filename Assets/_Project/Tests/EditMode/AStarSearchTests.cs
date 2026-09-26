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
        static readonly Vector2Int[] Buffer = new Vector2Int[8];

        // Builds a real GridGraph from text rows, '.' walkable and '#' blocked, so tests run
        // against the same grid implementation the game actually uses, not a parallel fake.
        static GridGraph GridFromRows(params string[] rows)
        {
            var grid = new GridGraph(rows[0].Length, rows.Length, Vector3.zero);
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] == '#')
                        grid.SetWalkable(new Vector2Int(x, y), false);
            return grid;
        }

        static GridGraph RandomGrid(int width, int height, float blockedChance, System.Random rng)
        {
            var grid = new GridGraph(width, height, Vector3.zero);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (rng.NextDouble() < blockedChance)
                        grid.SetWalkable(new Vector2Int(x, y), false);
            return grid;
        }

        static PathResult Search(GridGraph grid, Vector2Int start, Vector2Int goal) =>
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
            GridGraph grid = GridFromRows(".....");

            PathResult result = Search(grid, new Vector2Int(0, 0), new Vector2Int(4, 0));

            Assert.IsTrue(result.Found);
            Assert.AreEqual(5, result.Cells.Count);
            Assert.AreEqual(4f, PathCost(result.Cells), Tolerance);
        }

        [Test]
        public void DiagonalOnOpenGridIsOptimal()
        {
            GridGraph grid = GridFromRows("....", "....", "....", "....");

            PathResult result = Search(grid, new Vector2Int(0, 0), new Vector2Int(3, 3));

            Assert.IsTrue(result.Found);
            Assert.AreEqual(4, result.Cells.Count);
            Assert.AreEqual(3f * Diagonal, PathCost(result.Cells), Tolerance);
        }

        [Test]
        public void RoutesAroundAWallWithoutCuttingCorners()
        {
            GridGraph grid = GridFromRows(
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
            GridGraph grid = GridFromRows(
                ".#",
                "#.");

            PathResult result = Search(grid, new Vector2Int(0, 0), new Vector2Int(1, 1));

            Assert.IsFalse(result.Found);
            Assert.IsNull(result.Cells);
        }

        [Test]
        public void WalledOffGoalIsNotFound()
        {
            GridGraph grid = GridFromRows(
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
            GridGraph grid = GridFromRows("...", "...", "...");
            var cell = new Vector2Int(1, 1);

            PathResult result = Search(grid, cell, cell);

            Assert.IsTrue(result.Found);
            CollectionAssert.AreEqual(new[] { cell }, result.Cells);
        }

        [Test]
        public void BlockedStartOrGoalIsNotFound()
        {
            GridGraph grid = GridFromRows("#.#");

            Assert.IsFalse(Search(grid, new Vector2Int(0, 0), new Vector2Int(1, 0)).Found);
            Assert.IsFalse(Search(grid, new Vector2Int(1, 0), new Vector2Int(2, 0)).Found);
        }

        [Test]
        public void ResultReportsTheGraphVersion()
        {
            GridGraph grid = GridFromRows("....");
            var cell = new Vector2Int(3, 0);
            grid.SetWalkable(cell, false);
            grid.SetWalkable(cell, true);

            PathResult result = Search(grid, new Vector2Int(0, 0), cell);

            Assert.AreEqual(2, result.GraphVersion);
        }

        [Test]
        public void ClosedDoorBlocksMovementEvenThoughSoundStillPasses()
        {
            GridGraph grid = GridFromRows(".....");
            var door = new Vector2Int(2, 0);
            grid.SetDoor(door, doorId: 1, isClosed: true);

            PathResult result = Search(grid, new Vector2Int(0, 0), new Vector2Int(4, 0));

            Assert.IsFalse(result.Found);
        }

        [Test]
        public void ReusedSearchReplansAroundANewlyBlockedCell()
        {
            GridGraph grid = GridFromRows(".....", ".....", ".....");
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

            for (int run = 0; run < 50; run++)
            {
                GridGraph grid = RandomGrid(20, 20, 0.25f, rng);
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
                    int count = grid.GetNeighboursNonAlloc(result.Cells[i - 1], Buffer);
                    bool isNeighbour = false;
                    for (int n = 0; n < count; n++)
                        isNeighbour |= Buffer[n] == result.Cells[i];
                    Assert.IsTrue(isNeighbour, $"Run {run}: step {i} is not a legal move.");
                }
            }
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

        // Deliberately simple uniform-cost search (no heuristic, no heap) used only as a
        // known-correct answer to check A* against.
        static float ReferenceDijkstraCost(GridGraph grid, Vector2Int start, Vector2Int goal)
        {
            var dist = new Dictionary<Vector2Int, float> { [start] = 0f };
            var done = new HashSet<Vector2Int>();
            var buffer = new Vector2Int[8];

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
                int count = grid.GetNeighboursNonAlloc(best, buffer);
                for (int i = 0; i < count; i++)
                {
                    Vector2Int next = buffer[i];
                    float nextDist = bestDist + BaseCostModel.Instance.StepCost(best, next);
                    if (!dist.TryGetValue(next, out float current) || nextDist < current)
                        dist[next] = nextDist;
                }
            }
        }
    }
}
