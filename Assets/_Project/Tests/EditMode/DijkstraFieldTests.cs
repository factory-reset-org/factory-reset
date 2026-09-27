using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class DijkstraFieldTests
    {
        const float Tolerance = 1e-3f;

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

        static DijkstraField FieldFrom(GridGraph grid, Vector2Int source)
        {
            var field = new DijkstraField(grid);
            field.Compute(source, BaseCostModel.Instance);
            return field;
        }

        static float PathCost(List<Vector2Int> cells)
        {
            float total = 0f;
            for (int i = 1; i < cells.Count; i++)
                total += BaseCostModel.Instance.StepCost(cells[i - 1], cells[i]);
            return total;
        }

        [Test]
        public void OpenGridCostsEqualOctileDistance()
        {
            var grid = new GridGraph(10, 8, Vector3.zero);
            var source = new Vector2Int(2, 3);

            DijkstraField field = FieldFrom(grid, source);

            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    var cell = new Vector2Int(x, y);
                    Assert.AreEqual(BaseCostModel.OctileDistance(source, cell), field.Cost(cell), Tolerance, $"Cell {cell}");
                }
        }

        [Test]
        public void SourceCostIsZero()
        {
            DijkstraField field = FieldFrom(GridFromRows("...", "...", "..."), new Vector2Int(1, 1));

            Assert.AreEqual(0f, field.Cost(new Vector2Int(1, 1)));
            Assert.IsTrue(field.IsReachable(new Vector2Int(1, 1)));
        }

        [Test]
        public void WallsAreRoutedAroundWithoutCuttingCorners()
        {
            GridGraph grid = GridFromRows(
                ".....",
                ".###.",
                ".....");

            DijkstraField field = FieldFrom(grid, new Vector2Int(0, 1));

            // Straight line is 4, but the wall forces six straight steps around it.
            Assert.AreEqual(6f, field.Cost(new Vector2Int(4, 1)), Tolerance);
        }

        [Test]
        public void DiagonalPastABlockedSideCellIsNotAllowed()
        {
            GridGraph grid = GridFromRows(
                "..",
                "#.");

            DijkstraField field = FieldFrom(grid, new Vector2Int(0, 0));

            // The diagonal (0,0) -> (1,1) would cut the corner of the wall at (0,1).
            Assert.AreEqual(2f, field.Cost(new Vector2Int(1, 1)), Tolerance);
        }

        [Test]
        public void WalledOffCellsAreUnreachable()
        {
            GridGraph grid = GridFromRows(
                ".....",
                ".###.",
                ".#.#.",
                ".###.",
                ".....");

            DijkstraField field = FieldFrom(grid, new Vector2Int(0, 0));
            var enclosed = new Vector2Int(2, 2);

            Assert.IsFalse(field.IsReachable(enclosed));
            Assert.IsTrue(float.IsPositiveInfinity(field.Cost(enclosed)));
            Assert.IsTrue(float.IsPositiveInfinity(field.Cost(new Vector2Int(1, 1))), "Wall cells are unreachable.");
        }

        [Test]
        public void BlockedSourceLeavesEveryCellUnreachable()
        {
            GridGraph grid = GridFromRows("#..");

            DijkstraField field = FieldFrom(grid, new Vector2Int(0, 0));

            for (int x = 0; x < 3; x++)
                Assert.IsFalse(field.IsReachable(new Vector2Int(x, 0)));
            Assert.AreEqual(0, field.NodesExpanded);
        }

        [Test]
        public void ClosedDoorBlocksTheField()
        {
            GridGraph grid = GridFromRows(".....");
            grid.SetDoor(new Vector2Int(2, 0), doorId: 1, isClosed: true);

            DijkstraField field = FieldFrom(grid, new Vector2Int(0, 0));

            Assert.IsTrue(field.IsReachable(new Vector2Int(1, 0)));
            Assert.IsFalse(field.IsReachable(new Vector2Int(4, 0)));
        }

        [Test]
        public void UncomputedOrOffGridCellsReturnInfinity()
        {
            var grid = new GridGraph(3, 3, Vector3.zero);
            var field = new DijkstraField(grid);

            Assert.IsFalse(field.HasBeenComputed);
            Assert.IsTrue(float.IsPositiveInfinity(field.Cost(Vector2Int.zero)));

            field.Compute(Vector2Int.zero, BaseCostModel.Instance);

            Assert.IsTrue(float.IsPositiveInfinity(field.Cost(new Vector2Int(-1, 0))));
            Assert.IsTrue(float.IsPositiveInfinity(field.Cost(new Vector2Int(3, 0))));
        }

        [Test]
        public void NullCostModelIsRejected()
        {
            var field = new DijkstraField(new GridGraph(2, 2, Vector3.zero));

            Assert.Throws<ArgumentNullException>(() => field.Compute(Vector2Int.zero, null));
            Assert.Throws<ArgumentNullException>(() => new DijkstraField(null));
        }

        [Test]
        public void CostsAreSymmetricBetweenTwoCells()
        {
            var rng = new System.Random(2024);
            GridGraph grid = RandomGrid(15, 15, 0.25f, rng);
            Vector2Int a = RandomWalkableCell(grid, rng);
            Vector2Int b = RandomWalkableCell(grid, rng);

            float aToB = FieldFrom(grid, a).Cost(b);
            float bToA = FieldFrom(grid, b).Cost(a);

            Assert.AreEqual(aToB, bToA, Tolerance);
        }

        [Test]
        public void FieldCostMatchesAStarOnFiftyRandomGrids()
        {
            var rng = new System.Random(12345);

            for (int run = 0; run < 50; run++)
            {
                GridGraph grid = RandomGrid(20, 20, 0.25f, rng);
                Vector2Int source = RandomWalkableCell(grid, rng);
                DijkstraField field = FieldFrom(grid, source);
                var aStar = new AStarSearch(grid);

                for (int target = 0; target < 5; target++)
                {
                    Vector2Int cell = RandomWalkableCell(grid, rng);
                    PathResult path = aStar.FindPath(source, cell, BaseCostModel.Instance);

                    Assert.AreEqual(path.Found, field.IsReachable(cell), $"Run {run}, target {cell}: reachability differs.");
                    if (path.Found)
                        Assert.AreEqual(PathCost(path.Cells), field.Cost(cell), Tolerance, $"Run {run}, target {cell}: cost differs.");
                }
            }
        }

        [Test]
        public void FieldBecomesStaleWhenTheGridChangesAndFreshAfterRecompute()
        {
            GridGraph grid = GridFromRows("....", "....");
            DijkstraField field = FieldFrom(grid, Vector2Int.zero);

            Assert.IsFalse(field.IsStale);
            Assert.AreEqual(grid.Version, field.GraphVersion);

            grid.AddBlocker(new Vector2Int(2, 0));
            Assert.IsTrue(field.IsStale);

            field.Compute(Vector2Int.zero, BaseCostModel.Instance);
            Assert.IsFalse(field.IsStale);
        }

        [Test]
        public void RecomputeAfterABlockingChangeMatchesAFreshField()
        {
            var rng = new System.Random(777);
            GridGraph grid = RandomGrid(20, 20, 0.2f, rng);
            Vector2Int source = RandomWalkableCell(grid, rng);
            var reused = new DijkstraField(grid);
            reused.Compute(source, BaseCostModel.Instance);

            // Drop a pushed box's worth of blockers in one batch, away from the source.
            using (GridGraph.Batch batch = grid.BeginBatch())
            {
                for (int i = 0; i < 6; i++)
                {
                    Vector2Int cell = RandomWalkableCell(grid, rng);
                    if (cell != source)
                        batch.AddBlocker(cell);
                }
                batch.Commit();
            }

            reused.Compute(source, BaseCostModel.Instance);
            DijkstraField fresh = FieldFrom(grid, source);

            for (int index = 0; index < grid.CellCount; index++)
            {
                Vector2Int cell = grid.FromIndex(index);
                Assert.AreEqual(fresh.Cost(cell), reused.Cost(cell), Tolerance, $"Cell {cell}");
            }
        }

        [Test]
        public void RecomputingAllocatesZeroBytes()
        {
            var rng = new System.Random(99);
            GridGraph grid = RandomGrid(30, 30, 0.2f, rng);
            Vector2Int source = RandomWalkableCell(grid, rng);
            var field = new DijkstraField(grid);

            // Warm up so one-off costs (JIT, first profiler marker use) are not counted.
            for (int i = 0; i < 3; i++)
                field.Compute(source, BaseCostModel.Instance);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                field.Compute(source, BaseCostModel.Instance);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated);
            Assert.Greater(field.NodesExpanded, 0);
        }
    }
}
