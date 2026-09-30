using System;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class GridLineCheckTests
    {
        static readonly Vector2Int[] EightDirections =
        {
            new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1)
        };

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

        static Vector3 Centre(GridGraph grid, int x, int y) => grid.CellToWorld(new Vector2Int(x, y));

        [Test]
        public void OpenGridLineIsWalkable()
        {
            GridGraph grid = GridFromRows(".....", ".....", ".....", ".....");

            Assert.IsTrue(GridLineCheck.IsWalkable(grid, Centre(grid, 0, 0), Centre(grid, 4, 3)));
        }

        [Test]
        public void BlockedCellOnTheLineBlocksIt()
        {
            GridGraph grid = GridFromRows(".....", "..#..", ".....");

            Assert.IsFalse(GridLineCheck.IsWalkable(grid, Centre(grid, 0, 1), Centre(grid, 4, 1)));
        }

        [Test]
        public void BlockedCellBesideTheLineDoesNotBlockIt()
        {
            GridGraph grid = GridFromRows(".....", "..#..", ".....");

            Assert.IsTrue(GridLineCheck.IsWalkable(grid, Centre(grid, 0, 2), Centre(grid, 4, 2)));
        }

        [Test]
        public void SteepLinePassingNextToABlockedCellIsWalkable()
        {
            // From (0,0) to (1,2) the line crosses x = 1 at y = 1.5, so it never enters (1,0).
            GridGraph grid = GridFromRows(".#", "..", "..");

            Assert.IsTrue(GridLineCheck.IsWalkable(grid, Centre(grid, 0, 0), Centre(grid, 1, 2)));
        }

        [Test]
        public void DiagonalThroughACornerNeedsBothSideCells()
        {
            // The 45-degree line from (0,0) to (1,1) passes exactly through the shared corner.
            GridGraph open = GridFromRows("..", "..");
            GridGraph oneSideBlocked = GridFromRows(".#", "..");

            Assert.IsTrue(GridLineCheck.IsWalkable(open, Centre(open, 0, 0), Centre(open, 1, 1)));
            Assert.IsFalse(GridLineCheck.IsWalkable(oneSideBlocked, Centre(oneSideBlocked, 0, 0), Centre(oneSideBlocked, 1, 1)));
        }

        [Test]
        public void ClosedDoorBlocksTheLineAndOpenDoorDoesNot()
        {
            GridGraph grid = GridFromRows(".....");
            var doorCell = new Vector2Int(2, 0);

            grid.SetDoor(doorCell, 1, isClosed: true);
            Assert.IsFalse(GridLineCheck.IsWalkable(grid, Centre(grid, 0, 0), Centre(grid, 4, 0)));

            grid.SetDoor(doorCell, 1, isClosed: false);
            Assert.IsTrue(GridLineCheck.IsWalkable(grid, Centre(grid, 0, 0), Centre(grid, 4, 0)));
        }

        [Test]
        public void EndsOutsideTheGridAreRejected()
        {
            GridGraph grid = GridFromRows("...", "...");
            Vector3 outside = new Vector3(-1f, 0f, 0.25f);

            Assert.IsFalse(GridLineCheck.IsWalkable(grid, Centre(grid, 1, 0), outside));
            Assert.IsFalse(GridLineCheck.IsWalkable(grid, outside, Centre(grid, 1, 0)));
        }

        [Test]
        public void ZeroLengthLineDependsOnlyOnItsCell()
        {
            GridGraph grid = GridFromRows(".#");

            Assert.IsTrue(GridLineCheck.IsWalkable(grid, Centre(grid, 0, 0), Centre(grid, 0, 0)));
            Assert.IsFalse(GridLineCheck.IsWalkable(grid, Centre(grid, 1, 0), Centre(grid, 1, 0)));
        }

        [Test]
        public void WorldHeightIsIgnored()
        {
            GridGraph grid = GridFromRows("....");
            Vector3 low = Centre(grid, 0, 0);
            Vector3 high = Centre(grid, 3, 0) + Vector3.up * 5f;

            Assert.IsTrue(GridLineCheck.IsWalkable(grid, low, high));
        }

        [Test]
        public void EveryOneCellStepAgreesWithTheGridOnRandomGrids()
        {
            // A line between neighbouring cell centres must be walkable exactly when the grid
            // allows that step, so smoothing never permits a move A* would forbid.
            var rng = new System.Random(7);
            for (int trial = 0; trial < 20; trial++)
            {
                GridGraph grid = RandomGrid(12, 12, 0.3f, rng);
                for (int y = 0; y < grid.Height; y++)
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var cell = new Vector2Int(x, y);
                        if (!grid.IsTraversable(cell))
                            continue;

                        foreach (Vector2Int offset in EightDirections)
                        {
                            Vector2Int next = cell + offset;
                            if (!grid.Contains(next))
                                continue;

                            bool gridAllows = false;
                            foreach (Vector2Int neighbour in grid.GetNeighbours(cell))
                                if (neighbour == next) { gridAllows = true; break; }

                            bool lineAllows = GridLineCheck.IsWalkable(grid, grid.CellToWorld(cell), grid.CellToWorld(next));
                            Assert.AreEqual(gridAllows, lineAllows, $"Step {cell} -> {next} on trial {trial}");
                        }
                    }
            }
        }

        [Test]
        public void LinesAreSymmetricOnRandomGrids()
        {
            var rng = new System.Random(11);
            for (int trial = 0; trial < 200; trial++)
            {
                GridGraph grid = RandomGrid(15, 15, 0.25f, rng);
                Vector3 a = Centre(grid, rng.Next(15), rng.Next(15));
                Vector3 b = Centre(grid, rng.Next(15), rng.Next(15));

                Assert.AreEqual(GridLineCheck.IsWalkable(grid, a, b), GridLineCheck.IsWalkable(grid, b, a),
                    $"Trial {trial}: {a} <-> {b}");
            }
        }

        [Test]
        public void CheckingALineAllocatesZeroBytes()
        {
            GridGraph grid = RandomGrid(30, 30, 0.1f, new System.Random(3));
            Vector3 from = Centre(grid, 0, 0);
            Vector3 to = Centre(grid, 29, 17);

            // Warm up so one-off costs (JIT) are not counted.
            for (int i = 0; i < 3; i++)
                GridLineCheck.IsWalkable(grid, from, to);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                GridLineCheck.IsWalkable(grid, from, to);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated);
        }
    }
}
