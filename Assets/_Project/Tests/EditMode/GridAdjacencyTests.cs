using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class GridAdjacencyTests
    {
        static GridGraph RandomGrid(int width, int height, float blockedChance, System.Random rng)
        {
            var grid = new GridGraph(width, height, Vector3.zero);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (rng.NextDouble() < blockedChance)
                        grid.SetWalkable(new Vector2Int(x, y), false);
            return grid;
        }

        // Every cell's table entry must be exactly what the grid itself answers: same
        // neighbours, same order, the base step cost for each.
        static void AssertMatchesGrid(GridGraph grid, string context)
        {
            GridAdjacency table = GridAdjacency.For(grid);
            table.GetArrays(out int[] counts, out int[] next, out float[] steps);
            var buffer = new Vector2Int[8];

            for (int index = 0; index < grid.CellCount; index++)
            {
                Vector2Int cell = grid.FromIndex(index);
                int expected = grid.IsTraversable(cell) ? grid.GetNeighboursNonAlloc(cell, buffer) : 0;
                Assert.AreEqual(expected, counts[index], $"{context}: neighbour count of {cell}");
                for (int k = 0; k < expected; k++)
                {
                    int slot = index * GridAdjacency.Slots + k;
                    Assert.AreEqual(grid.ToIndex(buffer[k]), next[slot], $"{context}: neighbour {k} of {cell}");
                    Assert.AreEqual(BaseCostModel.Instance.StepCost(cell, buffer[k]), steps[slot], 1e-6f,
                        $"{context}: step cost to neighbour {k} of {cell}");
                }
            }
        }

        [Test]
        public void TableMatchesTheGridOnRandomGrids()
        {
            var rng = new System.Random(2024);
            for (int run = 0; run < 20; run++)
                AssertMatchesGrid(RandomGrid(15, 12, 0.3f, rng), $"Run {run}");
        }

        [Test]
        public void BlockedCellHasNoNeighboursAndNoCornerIsCut()
        {
            var grid = new GridGraph(3, 3, Vector3.zero);
            grid.SetWalkable(new Vector2Int(1, 0), false);
            GridAdjacency table = GridAdjacency.For(grid);

            Assert.AreEqual(0, table.Count(grid.ToIndex(new Vector2Int(1, 0))));
            // (0,0) to (1,1) would cut the blocked (1,0) corner: only north is left.
            Assert.AreEqual(1, table.Count(grid.ToIndex(new Vector2Int(0, 0))));
        }

        [Test]
        public void OneTablePerGrid()
        {
            var a = new GridGraph(4, 4, Vector3.zero);
            var b = new GridGraph(4, 4, Vector3.zero);

            Assert.AreSame(GridAdjacency.For(a), GridAdjacency.For(a));
            Assert.AreNotSame(GridAdjacency.For(a), GridAdjacency.For(b));
        }

        [Test]
        public void ChangesRefreshOnlyTheAffectedCellsAndKeepTheTableCorrect()
        {
            var rng = new System.Random(55);
            GridGraph grid = RandomGrid(20, 20, 0.15f, rng);
            GridAdjacency table = GridAdjacency.For(grid);
            Vector2Int doorCell = new Vector2Int(10, 10);
            grid.SetWalkable(doorCell, true);

            for (int step = 0; step < 30; step++)
            {
                int before = table.CellsRefreshed;
                int affected = 0;
                void Count(GridChange change) => affected = change.AffectedCells.Count;
                grid.Changed += Count;

                // A box-sized blocker moving, or the door opening and closing.
                if (step % 5 == 4)
                    grid.SetDoor(doorCell, 1, isClosed: step % 10 == 4);
                else
                    using (GridGraph.Batch batch = grid.BeginBatch())
                    {
                        var corner = new Vector2Int(rng.Next(19), rng.Next(19));
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                                batch.AddBlocker(corner + new Vector2Int(dx, dy));
                        batch.Commit();
                    }

                grid.Changed -= Count;
                AssertMatchesGrid(grid, $"After change {step}");
                Assert.AreEqual(affected, table.CellsRefreshed - before, $"Change {step}: only the affected cells are refreshed.");
            }
        }

        [Test]
        public void ReadingTheTableAllocatesNothing()
        {
            var grid = new GridGraph(10, 10, Vector3.zero);
            GridAdjacency table = GridAdjacency.For(grid);
            table.GetArrays(out _, out _, out _);

            int allocations = GcAllocations.Count(() =>
            {
                for (int i = 0; i < 100; i++)
                    table.GetArrays(out _, out _, out _);
            });

            Assert.AreEqual(0, allocations);
        }
    }
}
