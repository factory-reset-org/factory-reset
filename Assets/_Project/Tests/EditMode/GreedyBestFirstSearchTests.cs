using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Tracker;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class GreedyBestFirstSearchTests
    {
        static readonly Vector2Int[] Buffer = new Vector2Int[8];

        // '.' walkable, '#' blocked; row 0 is y = 0.
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

        static Vector2Int RandomWalkableCell(GridGraph grid, System.Random rng)
        {
            while (true)
            {
                var cell = new Vector2Int(rng.Next(grid.Width), rng.Next(grid.Height));
                if (grid.IsTraversable(cell))
                    return cell;
            }
        }

        static PathResult Search(GridGraph grid, Vector2Int start, Vector2Int goal) =>
            new GreedyBestFirstSearch(grid).FindPath(start, goal, BaseCostModel.Instance);

        // A path is valid if it runs start -> goal and every step is a legal grid move
        // (adjacent, traversable, no corner cutting), as reported by the grid itself.
        static void AssertValidPath(GridGraph grid, PathResult result, Vector2Int start, Vector2Int goal)
        {
            Assert.IsTrue(result.Found);
            List<Vector2Int> cells = result.Cells;
            Assert.AreEqual(start, cells[0]);
            Assert.AreEqual(goal, cells[cells.Count - 1]);
            for (int i = 1; i < cells.Count; i++)
            {
                int count = grid.GetNeighboursNonAlloc(cells[i - 1], Buffer);
                bool legal = false;
                for (int k = 0; k < count; k++)
                    legal |= Buffer[k] == cells[i];
                Assert.IsTrue(legal, $"Step {cells[i - 1]} -> {cells[i]} is not a legal move.");
            }
        }

        static float PathCost(List<Vector2Int> cells)
        {
            float total = 0f;
            for (int i = 1; i < cells.Count; i++)
                total += BaseCostModel.Instance.StepCost(cells[i - 1], cells[i]);
            return total;
        }

        static int ReachableCount(GridGraph grid, Vector2Int start)
        {
            var seen = new HashSet<Vector2Int> { start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int count = grid.GetNeighboursNonAlloc(queue.Dequeue(), Buffer);
                for (int k = 0; k < count; k++)
                    if (seen.Add(Buffer[k]))
                        queue.Enqueue(Buffer[k]);
            }
            return seen.Count;
        }

        [Test]
        public void FindsAStraightPathOnAnOpenGrid()
        {
            GridGraph grid = GridFromRows(".......", ".......", ".......");

            PathResult result = Search(grid, new Vector2Int(0, 1), new Vector2Int(6, 1));

            AssertValidPath(grid, result, new Vector2Int(0, 1), new Vector2Int(6, 1));
            Assert.AreEqual(7, result.Cells.Count);
        }

        [Test]
        public void RoutesAroundAWallWithoutCuttingCorners()
        {
            GridGraph grid = GridFromRows(
                ".......",
                "...#...",
                "...#...",
                "...#...",
                ".......");

            PathResult result = Search(grid, new Vector2Int(0, 2), new Vector2Int(6, 2));

            AssertValidPath(grid, result, new Vector2Int(0, 2), new Vector2Int(6, 2));
        }

        [Test]
        public void StartEqualToGoalReturnsTheSingleCell()
        {
            GridGraph grid = GridFromRows("...");

            PathResult result = Search(grid, new Vector2Int(1, 0), new Vector2Int(1, 0));

            Assert.IsTrue(result.Found);
            CollectionAssert.AreEqual(new[] { new Vector2Int(1, 0) }, result.Cells);
            Assert.AreEqual(1, result.NodesExpanded);
        }

        [Test]
        public void WalledOffGoalIsNotFound()
        {
            // The goal (6, 1) is walkable but sealed in by walls on every side.
            GridGraph grid = GridFromRows(
                ".....###",
                ".....#.#",
                ".....###");

            PathResult result = Search(grid, new Vector2Int(0, 1), new Vector2Int(6, 1));

            Assert.IsFalse(result.Found);
            Assert.IsNull(result.Cells);
        }

        [Test]
        public void ExpandsEachReachableCellExactlyOnceWhenTheGoalIsUnreachable()
        {
            // With a correct closed set, a failed search expands every reachable cell once
            // and nothing more, so the count equals the size of the reachable region.
            GridGraph grid = GridFromRows(
                "........#...",
                "........#...",
                "........#...");
            var start = new Vector2Int(0, 0);

            PathResult result = Search(grid, start, new Vector2Int(10, 1));

            Assert.IsFalse(result.Found);
            Assert.AreEqual(ReachableCount(grid, start), result.NodesExpanded);
        }

        [Test]
        public void BlockedStartOrGoalIsNotFoundWithoutExpanding()
        {
            GridGraph grid = GridFromRows("#...#");

            PathResult fromWall = Search(grid, new Vector2Int(0, 0), new Vector2Int(2, 0));
            PathResult intoWall = Search(grid, new Vector2Int(2, 0), new Vector2Int(4, 0));

            Assert.IsFalse(fromWall.Found);
            Assert.IsFalse(intoWall.Found);
            Assert.AreEqual(0, fromWall.NodesExpanded);
            Assert.AreEqual(0, intoWall.NodesExpanded);
        }

        [Test]
        public void ClosedDoorBlocksMovement()
        {
            GridGraph grid = GridFromRows(".....");
            grid.SetDoor(new Vector2Int(2, 0), doorId: 1, isClosed: true);

            Assert.IsFalse(Search(grid, new Vector2Int(0, 0), new Vector2Int(4, 0)).Found);
        }

        [Test]
        public void ThroughClosedDoorsTheRouteCrossesTheDoor()
        {
            GridGraph grid = GridFromRows(".....");
            var door = new Vector2Int(2, 0);
            grid.SetDoor(door, doorId: 1, isClosed: true);

            PathResult result = new GreedyBestFirstSearch(grid).FindPath(new Vector2Int(0, 0), new Vector2Int(4, 0),
                BaseCostModel.Instance, throughClosedDoors: true);

            Assert.IsTrue(result.Found);
            CollectionAssert.Contains(result.Cells, door);
        }

        [Test]
        public void ThroughClosedDoorsWallsStillBlock()
        {
            GridGraph grid = GridFromRows("..#..");
            var search = new GreedyBestFirstSearch(grid);

            Assert.IsFalse(search.FindPath(new Vector2Int(0, 0), new Vector2Int(4, 0), BaseCostModel.Instance, throughClosedDoors: true).Found);
        }

        [Test]
        public void ThroughClosedDoorsAGoalOnTheDoorIsAllowed()
        {
            GridGraph grid = GridFromRows(".....");
            var door = new Vector2Int(2, 0);
            grid.SetDoor(door, doorId: 1, isClosed: true);
            var search = new GreedyBestFirstSearch(grid);

            Assert.IsFalse(search.FindPath(new Vector2Int(0, 0), door, BaseCostModel.Instance).Found);
            Assert.IsTrue(search.FindPath(new Vector2Int(0, 0), door, BaseCostModel.Instance, throughClosedDoors: true).Found);
        }

        [Test]
        public void FindsAPathWheneverAStarDoesOnFiftyRandomGrids()
        {
            // Completeness: GBFS may take a longer route, but never misses one that exists.
            var rng = new System.Random(4242);
            for (int run = 0; run < 50; run++)
            {
                GridGraph grid = RandomGrid(20, 20, 0.25f, rng);
                Vector2Int start = RandomWalkableCell(grid, rng);
                Vector2Int goal = RandomWalkableCell(grid, rng);

                PathResult greedy = Search(grid, start, goal);
                PathResult optimal = new AStarSearch(grid).FindPath(start, goal, BaseCostModel.Instance);

                Assert.AreEqual(optimal.Found, greedy.Found, $"Run {run}: reachability differs.");
                if (!greedy.Found)
                    continue;
                AssertValidPath(grid, greedy, start, goal);
                Assert.GreaterOrEqual(PathCost(greedy.Cells) + 1e-3f, PathCost(optimal.Cells),
                    $"Run {run}: a path cannot be cheaper than A*'s optimum.");
            }
        }

        [Test]
        public void ExpandsFewerNodesThanAStarOnOpenGrids()
        {
            var rng = new System.Random(7);
            GridGraph grid = RandomGrid(60, 60, 0.05f, rng);
            var greedy = new GreedyBestFirstSearch(grid);
            var astar = new AStarSearch(grid);
            int greedyTotal = 0, astarTotal = 0, pairs = 0;

            while (pairs < 20)
            {
                Vector2Int start = RandomWalkableCell(grid, rng);
                Vector2Int goal = RandomWalkableCell(grid, rng);
                PathResult a = astar.FindPath(start, goal, BaseCostModel.Instance);
                if (!a.Found)
                    continue;
                PathResult g = greedy.FindPath(start, goal, BaseCostModel.Instance);
                greedyTotal += g.NodesExpanded;
                astarTotal += a.NodesExpanded;
                pairs++;
            }

            Assert.Less(greedyTotal, astarTotal);
        }

        [Test]
        public void ResultReportsTheGridVersion()
        {
            GridGraph grid = GridFromRows(".....");
            grid.SetWalkable(new Vector2Int(4, 0), false);
            grid.SetWalkable(new Vector2Int(4, 0), true);

            PathResult result = Search(grid, new Vector2Int(0, 0), new Vector2Int(3, 0));

            Assert.AreEqual(grid.Version, result.GraphVersion);
        }

        [Test]
        public void ReusedSearchAllocatesNothing()
        {
            // An unreachable goal makes the search expand the whole region and build no path
            // list, so any allocation would come from the search loop itself.
            GridGraph grid = GridFromRows(
                "..........#...",
                "..........#...",
                "..........#...");
            var search = new GreedyBestFirstSearch(grid);
            var start = new Vector2Int(0, 0);
            var goal = new Vector2Int(12, 1);
            for (int i = 0; i < 10; i++)
                search.FindPath(start, goal, BaseCostModel.Instance);

            int allocated = GcAllocations.Count(() =>
            {
                for (int i = 0; i < 100; i++)
                    search.FindPath(start, goal, BaseCostModel.Instance);
            });

            Assert.AreEqual(0, allocated);
        }

        [Test]
        public void AFoundPathCostsOnlyItsListAndArray()
        {
            GridGraph grid = GridFromRows(
                "..............",
                "..........#...",
                "..........#...");
            var search = new GreedyBestFirstSearch(grid);
            var start = new Vector2Int(0, 2);
            var goal = new Vector2Int(12, 2);
            for (int i = 0; i < 10; i++)
                search.FindPath(start, goal, BaseCostModel.Instance);

            int allocated = GcAllocations.Count(() =>
            {
                for (int i = 0; i < 100; i++)
                    search.FindPath(start, goal, BaseCostModel.Instance);
            });

            Assert.AreEqual(100 * 2, allocated, "The returned path's list and array, sized exactly; nothing in the search loop.");
        }

        [Test]
        public void NullCostModelThrows()
        {
            GridGraph grid = GridFromRows("...");
            Assert.Throws<ArgumentNullException>(() =>
                new GreedyBestFirstSearch(grid).FindPath(Vector2Int.zero, new Vector2Int(2, 0), null));
        }
    }
}
