using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// <see cref="DijkstraField.Refresh"/>: a stale field repaired in place must equal a field
    /// computed from scratch on the changed grid, for every cell, whatever the change.
    /// </summary>
    public class DijkstraFieldRepairTests
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

        static void AssertSameAsFresh(GridGraph grid, DijkstraField repaired, string context)
        {
            var fresh = new DijkstraField(grid);
            fresh.Compute(repaired.Source, BaseCostModel.Instance, repaired.MaxCost);
            for (int index = 0; index < grid.CellCount; index++)
            {
                Vector2Int cell = grid.FromIndex(index);
                float expected = fresh.Cost(cell);
                float actual = repaired.Cost(cell);
                if (float.IsPositiveInfinity(expected))
                    Assert.IsTrue(float.IsPositiveInfinity(actual), $"{context}: {cell} should be unreachable, got {actual}");
                else
                    Assert.AreEqual(expected, actual, Tolerance, $"{context}: cost of {cell}");
            }
            Assert.IsFalse(repaired.IsStale, context);
        }

        // One random change of the kinds the game makes: a box placed, a box moved (its old
        // cells released, new ones blocked, in one batch), a cell opened, a door shut or opened.
        static void RandomChange(GridGraph grid, System.Random rng, List<Vector2Int> boxCells, Vector2Int door)
        {
            int kind = rng.Next(4);
            using (GridGraph.Batch batch = grid.BeginBatch())
            {
                if (kind == 0 || (kind == 1 && boxCells.Count == 0))
                {
                    var corner = new Vector2Int(rng.Next(grid.Width - 1), rng.Next(grid.Height - 1));
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            Vector2Int cell = corner + new Vector2Int(dx, dy);
                            batch.AddBlocker(cell);
                            boxCells.Add(cell);
                        }
                }
                else if (kind == 1)
                {
                    // Move the oldest box one cell sideways: release its four cells, block four more.
                    var moved = new List<Vector2Int>();
                    var offset = rng.Next(2) == 0 ? Vector2Int.right : Vector2Int.up;
                    for (int i = 0; i < 4; i++)
                    {
                        batch.RemoveBlocker(boxCells[i]);
                        Vector2Int next = boxCells[i] + offset;
                        if (grid.Contains(next))
                        {
                            batch.AddBlocker(next);
                            moved.Add(next);
                        }
                    }
                    boxCells.RemoveRange(0, 4);
                    boxCells.AddRange(moved);
                    // A box pushed off the edge loses cells: stop tracking some so the list
                    // stays in fours (an untracked blocker simply stays where it is).
                    while (boxCells.Count % 4 != 0)
                        boxCells.RemoveAt(boxCells.Count - 1);
                }
                else if (kind == 2)
                {
                    batch.SetWalkable(new Vector2Int(rng.Next(grid.Width), rng.Next(grid.Height)), true);
                }
                else
                {
                    batch.SetDoor(door, 7, rng.Next(2) == 0);
                }
                batch.Commit();
            }
        }

        [Test]
        public void RefreshEqualsAFreshFieldAfterRandomChanges()
        {
            var rng = new System.Random(20261009);
            int repairs = 0;
            for (int run = 0; run < 60; run++)
            {
                GridGraph grid = RandomGrid(24, 24, 0.18f, rng);
                var door = new Vector2Int(12, 12);
                grid.SetWalkable(door, true);
                Vector2Int source = RandomWalkableCell(grid, rng);
                var field = new DijkstraField(grid);
                field.Compute(source, BaseCostModel.Instance);
                var boxes = new List<Vector2Int>();

                for (int round = 0; round < 8; round++)
                {
                    // Sometimes several changes pile up before the field is refreshed.
                    int changes = 1 + rng.Next(3);
                    for (int c = 0; c < changes; c++)
                        RandomChange(grid, rng, boxes, door);

                    field.Refresh();
                    if (field.LastRefreshWasRepair)
                        repairs++;
                    AssertSameAsFresh(grid, field, $"Run {run}, round {round}");
                }
            }
            Assert.Greater(repairs, 240, "Most refreshes were repairs, not recomputes.");
        }

        [Test]
        public void OpeningADoorConnectsTheRoomBehindIt()
        {
            // Two rooms joined by a single door cell.
            var grid = new GridGraph(21, 9, Vector3.zero);
            for (int y = 0; y < 9; y++)
                grid.SetWalkable(new Vector2Int(10, y), false);
            var door = new Vector2Int(10, 4);
            grid.SetWalkable(door, true);
            grid.SetDoor(door, 1, isClosed: true);
            var field = new DijkstraField(grid);
            field.Compute(new Vector2Int(2, 4), BaseCostModel.Instance);
            Assert.IsFalse(field.IsReachable(new Vector2Int(18, 4)));

            grid.SetDoor(door, 1, isClosed: false);
            field.Refresh();
            Assert.IsTrue(field.LastRefreshWasRepair);
            AssertSameAsFresh(grid, field, "Door opened");
            Assert.AreEqual(16f, field.Cost(new Vector2Int(18, 4)), Tolerance);

            grid.SetDoor(door, 1, isClosed: true);
            field.Refresh();
            AssertSameAsFresh(grid, field, "Door shut again");
            Assert.IsFalse(field.IsReachable(new Vector2Int(18, 4)));
        }

        [Test]
        public void AMovedBoxIsRepairedWithFarFewerCellsThanAFullCompute()
        {
            var grid = new GridGraph(80, 80, Vector3.zero);
            var field = new DijkstraField(grid);
            field.Compute(new Vector2Int(10, 10), BaseCostModel.Instance);
            int full = field.NodesExpanded;

            grid.AddBlocker(new Vector2Int(40, 40));
            field.Refresh();
            Assert.IsTrue(field.LastRefreshWasRepair);
            AssertSameAsFresh(grid, field, "Box placed");

            using (GridGraph.Batch batch = grid.BeginBatch())
            {
                batch.RemoveBlocker(new Vector2Int(40, 40));
                batch.AddBlocker(new Vector2Int(41, 40));
                batch.Commit();
            }
            field.Refresh();
            Assert.IsTrue(field.LastRefreshWasRepair);
            AssertSameAsFresh(grid, field, "Box moved");
            Assert.Less(field.NodesExpanded, full / 4, $"Repair expanded {field.NodesExpanded} of {full} cells.");
        }

        [Test]
        public void BlockingTheSourceFallsBackToACompute()
        {
            var grid = new GridGraph(10, 10, Vector3.zero);
            var field = new DijkstraField(grid);
            field.Compute(new Vector2Int(5, 5), BaseCostModel.Instance);

            grid.AddBlocker(new Vector2Int(5, 5));
            field.Refresh();

            Assert.IsFalse(field.LastRefreshWasRepair);
            Assert.IsFalse(field.IsReachable(new Vector2Int(0, 0)), "A blocked source reaches nothing.");
        }

        [Test]
        public void BoundedFieldsAndOtherCostModelsAreComputedAgain()
        {
            var grid = new GridGraph(12, 12, Vector3.zero);
            var bounded = new DijkstraField(grid);
            bounded.Compute(new Vector2Int(2, 2), BaseCostModel.Instance, 6f);
            var penalised = new DijkstraField(grid);
            var model = new PenaltyModel();
            penalised.Compute(new Vector2Int(2, 2), model);

            grid.AddBlocker(new Vector2Int(4, 4));
            bounded.Refresh();
            penalised.Refresh();

            Assert.IsFalse(bounded.LastRefreshWasRepair);
            Assert.IsFalse(penalised.LastRefreshWasRepair);
            AssertSameAsFresh(grid, bounded, "Bounded");
            var fresh = new DijkstraField(grid);
            fresh.Compute(new Vector2Int(2, 2), model);
            Assert.AreEqual(fresh.Cost(new Vector2Int(9, 9)), penalised.Cost(new Vector2Int(9, 9)), Tolerance);
        }

        sealed class PenaltyModel : ICostModel
        {
            public float StepCost(Vector2Int from, Vector2Int to) =>
                BaseCostModel.Instance.StepCost(from, to) + (to.y == 6 ? 1f : 0f);
        }

        [Test]
        public void MoreChangesThanTheLogHoldsFallBackToACompute()
        {
            var grid = new GridGraph(30, 30, Vector3.zero);
            var field = new DijkstraField(grid);
            field.Compute(new Vector2Int(0, 0), BaseCostModel.Instance);

            for (int i = 0; i <= GridAdjacency.ChangeLogSize; i++)
                grid.AddBlocker(new Vector2Int(5 + i % 20, 5 + i / 20));
            field.Refresh();

            Assert.IsFalse(field.LastRefreshWasRepair);
            AssertSameAsFresh(grid, field, "After the log overflowed");
        }

        [Test]
        public void RefreshingAFreshFieldDoesNothingAndAnUncomputedOneThrows()
        {
            var grid = new GridGraph(5, 5, Vector3.zero);
            var field = new DijkstraField(grid);
            Assert.Throws<InvalidOperationException>(() => field.Refresh());

            field.Compute(Vector2Int.zero, BaseCostModel.Instance);
            int version = field.GraphVersion;
            field.Refresh();
            Assert.AreEqual(version, field.GraphVersion);
        }

        [Test]
        public void RepairingAllocatesNothing()
        {
            var grid = new GridGraph(40, 40, Vector3.zero);
            var field = new DijkstraField(grid);
            field.Compute(new Vector2Int(2, 2), BaseCostModel.Instance);
            var box = new Vector2Int(20, 20);

            // Warm up: the first repairs may grow the reusable lists.
            for (int i = 0; i < 4; i++)
            {
                grid.AddBlocker(box);
                field.Refresh();
                grid.RemoveBlocker(box);
                field.Refresh();
            }

            grid.AddBlocker(box);
            int allocations = GcAllocations.Count(() => field.Refresh());

            Assert.IsTrue(field.LastRefreshWasRepair);
            Assert.AreEqual(0, allocations);
        }
    }
}
