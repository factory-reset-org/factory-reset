using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class PathSmootherTests
    {
        // Builds a real GridGraph from text rows, '.' walkable and '#' blocked. Row 0 is y = 0.
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

        // The world-space path a brain would hand the body: one point per cell centre.
        static List<Vector3> WorldPath(GridGraph grid, Vector2Int start, Vector2Int goal)
        {
            PathResult result = new AStarSearch(grid).FindPath(start, goal, BaseCostModel.Instance);
            Assert.IsTrue(result.Found, $"Test setup: no path from {start} to {goal}");
            var path = new List<Vector3>();
            foreach (Vector2Int cell in result.Cells)
                path.Add(grid.CellToWorld(cell));
            return path;
        }

        static List<Vector3> Smooth(GridGraph grid, List<Vector3> path)
        {
            var result = new List<Vector3>();
            PathSmoother.StringPull(grid, path, result);
            return result;
        }

        static float Length(List<Vector3> path)
        {
            float total = 0f;
            for (int i = 1; i < path.Count; i++)
                total += Vector3.Distance(path[i - 1], path[i]);
            return total;
        }

        [Test]
        public void PathsOfTwoOrFewerPointsAreCopiedUnchanged()
        {
            GridGraph grid = GridFromRows("...");
            var two = new List<Vector3> { grid.CellToWorld(new Vector2Int(0, 0)), grid.CellToWorld(new Vector2Int(2, 0)) };

            CollectionAssert.AreEqual(two, Smooth(grid, two));
            CollectionAssert.AreEqual(two.GetRange(0, 1), Smooth(grid, two.GetRange(0, 1)));
            CollectionAssert.IsEmpty(Smooth(grid, new List<Vector3>()));
        }

        [Test]
        public void StraightCorridorBecomesItsTwoEnds()
        {
            GridGraph grid = GridFromRows("........");
            List<Vector3> path = WorldPath(grid, new Vector2Int(0, 0), new Vector2Int(7, 0));

            List<Vector3> smoothed = Smooth(grid, path);

            CollectionAssert.AreEqual(new[] { path[0], path[path.Count - 1] }, smoothed);
        }

        [Test]
        public void StaircaseOnAnOpenGridBecomesOneStraightLine()
        {
            GridGraph grid = GridFromRows("........", "........", "........", "........");
            List<Vector3> path = WorldPath(grid, new Vector2Int(0, 0), new Vector2Int(7, 3));

            List<Vector3> smoothed = Smooth(grid, path);

            Assert.Greater(path.Count, 2);
            CollectionAssert.AreEqual(new[] { path[0], path[path.Count - 1] }, smoothed);
        }

        [Test]
        public void PathAroundAWallKeepsATurningPoint()
        {
            // A wall between the two ends forces the route round its open end.
            GridGraph grid = GridFromRows(
                "......",
                "......",
                "####..",
                "......");
            List<Vector3> path = WorldPath(grid, new Vector2Int(0, 3), new Vector2Int(0, 0));

            List<Vector3> smoothed = Smooth(grid, path);

            Assert.Greater(smoothed.Count, 2);
            Assert.IsFalse(GridLineCheck.IsWalkable(grid, path[0], path[path.Count - 1]));
        }

        [Test]
        public void SmoothedPathsAreSafeShorterSubsequencesOnRandomGrids()
        {
            var rng = new System.Random(21);
            for (int trial = 0; trial < 100; trial++)
            {
                GridGraph grid = RandomGrid(20, 20, 0.25f, rng);
                Vector2Int start = RandomWalkableCell(grid, rng);
                Vector2Int goal = RandomWalkableCell(grid, rng);
                PathResult found = new AStarSearch(grid).FindPath(start, goal, BaseCostModel.Instance);
                if (!found.Found)
                    continue;

                List<Vector3> path = WorldPath(grid, start, goal);
                List<Vector3> smoothed = Smooth(grid, path);

                // Same ends.
                Assert.AreEqual(path[0], smoothed[0], $"Trial {trial}: start");
                Assert.AreEqual(path[path.Count - 1], smoothed[smoothed.Count - 1], $"Trial {trial}: end");

                // Every smoothed segment is walkable in a straight line.
                for (int i = 1; i < smoothed.Count; i++)
                    Assert.IsTrue(GridLineCheck.IsWalkable(grid, smoothed[i - 1], smoothed[i]),
                        $"Trial {trial}: segment {i} is blocked");

                // Kept points appear in the original path, in order.
                int cursor = 0;
                foreach (Vector3 point in smoothed)
                {
                    while (cursor < path.Count && path[cursor] != point) cursor++;
                    Assert.Less(cursor, path.Count, $"Trial {trial}: {point} is not an original waypoint in order");
                }

                // Never longer than the original.
                Assert.LessOrEqual(Length(smoothed), Length(path) + 1e-4f, $"Trial {trial}: longer after smoothing");
            }
        }

        [Test]
        public void InputPathIsNotModified()
        {
            GridGraph grid = GridFromRows("......", "......");
            List<Vector3> path = WorldPath(grid, new Vector2Int(0, 0), new Vector2Int(5, 1));
            var copy = new List<Vector3>(path);

            Smooth(grid, path);

            CollectionAssert.AreEqual(copy, path);
        }

        [Test]
        public void InvalidArgumentsThrow()
        {
            GridGraph grid = GridFromRows("..");
            var path = new List<Vector3>();
            var result = new List<Vector3>();

            Assert.Throws<ArgumentNullException>(() => PathSmoother.StringPull(null, path, result));
            Assert.Throws<ArgumentNullException>(() => PathSmoother.StringPull(grid, null, result));
            Assert.Throws<ArgumentNullException>(() => PathSmoother.StringPull(grid, path, null));
            Assert.Throws<ArgumentException>(() => PathSmoother.StringPull(grid, path, path));
        }

        [Test]
        public void SmoothingIntoAReusedListAllocatesZeroBytes()
        {
            GridGraph grid = RandomGrid(30, 30, 0.15f, new System.Random(5));
            var rng = new System.Random(6);
            List<Vector3> path = null;
            while (path == null)
            {
                Vector2Int start = RandomWalkableCell(grid, rng);
                Vector2Int goal = RandomWalkableCell(grid, rng);
                if (new AStarSearch(grid).FindPath(start, goal, BaseCostModel.Instance).Found)
                    path = WorldPath(grid, start, goal);
            }
            var result = new List<Vector3>(path.Count);

            // Warm up so one-off costs (JIT) are not counted.
            for (int i = 0; i < 3; i++)
                PathSmoother.StringPull(grid, path, result);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++)
                PathSmoother.StringPull(grid, path, result);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated);
        }
    }
}
