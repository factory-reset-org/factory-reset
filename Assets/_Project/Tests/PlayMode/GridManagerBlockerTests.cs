using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    public sealed class GridManagerBlockerTests : GridWorldFixture
    {
        // A 0.4 m box at grid-local (2, 2): grown by 0.55 m it spans 1.25..2.75 on X and Z,
        // which holds the centres of cells 2..5 on each axis (centres at 0.25 + 0.5k).
        Bounds SmallBox(float localX = 2f, float localZ = 2f) =>
            new Bounds(Origin + new Vector3(localX, 0.5f, localZ), new Vector3(0.4f, 1f, 0.4f));

        static List<Vector2Int> Square(int x0, int y0, int x1, int y1)
        {
            var cells = new List<Vector2Int>();
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    cells.Add(new Vector2Int(x, y));
            return cells;
        }

        GridGraph Build()
        {
            Bake();
            return CreateManager().BuildGrid();
        }

        [Test]
        public void CellsUnderGrowsTheBoundsByTheAgentClearance()
        {
            var grid = new GridGraph(8, 8, Origin);

            Vector2Int[] cells = GridManager.CellsUnder(grid, SmallBox());

            CollectionAssert.AreEqual(Square(2, 2, 5, 5), cells);
        }

        [Test]
        public void CellsUnderIsClampedToTheGrid()
        {
            var grid = new GridGraph(8, 8, Origin);

            Vector2Int[] cells = GridManager.CellsUnder(grid, SmallBox(0.1f, 0.1f));

            foreach (Vector2Int cell in cells)
                Assert.That(grid.Contains(cell), cell.ToString());
            Assert.That(cells, Is.Not.Empty);
            Assert.That(GridManager.CellsUnder(grid, SmallBox(50f, 50f)), Is.Empty);
        }

        [Test]
        public void SetBlockerBlocksExactlyThoseCellsInOneChange()
        {
            GridGraph grid = Build();
            int changes = 0;
            grid.Changed += _ => changes++;

            GridManager.SetBlocker(1, SmallBox());

            Assert.That(changes, Is.EqualTo(1));
            var blocked = Square(2, 2, 5, 5);
            for (int i = 0; i < grid.CellCount; i++)
            {
                Vector2Int cell = grid.FromIndex(i);
                bool expected = blocked.Contains(cell);
                Assert.That(grid.GetNode(cell).BlockerCount, Is.EqualTo(expected ? 1 : 0), cell.ToString());
                if (expected)
                    Assert.That(grid.IsTraversable(cell), Is.False, cell.ToString());
            }
            CollectionAssert.AreEqual(blocked, GridManager.BlockedCellsOf(1));
        }

        [Test]
        public void MovingABlockerFreesTheOldCellsInOneChange()
        {
            GridGraph grid = Build();
            GridManager.SetBlocker(1, SmallBox(1f, 1f));
            int changes = 0;
            grid.Changed += _ => changes++;

            // Cells 0..3 move to cells 1..4: they overlap on 1..3.
            GridManager.SetBlocker(1, SmallBox(1.5f, 1.5f));

            Assert.That(changes, Is.EqualTo(1));
            Assert.That(grid.GetNode(new Vector2Int(0, 0)).BlockerCount, Is.EqualTo(0), "Old cell freed.");
            Assert.That(grid.GetNode(new Vector2Int(4, 4)).BlockerCount, Is.EqualTo(1), "New cell blocked.");
            Assert.That(grid.GetNode(new Vector2Int(3, 3)).BlockerCount, Is.EqualTo(1), "Overlap counted once, not twice.");
        }

        [Test]
        public void OverlappingBlockersKeepASharedCellBlockedUntilBothClear()
        {
            GridGraph grid = Build();
            var shared = new Vector2Int(4, 4);
            GridManager.SetBlocker(1, SmallBox(2f, 2f));
            GridManager.SetBlocker(2, SmallBox(2.5f, 2.5f));
            Assert.That(grid.GetNode(shared).BlockerCount, Is.EqualTo(2));

            GridManager.ClearBlocker(1);
            Assert.That(grid.GetNode(shared).BlockerCount, Is.EqualTo(1));
            Assert.That(grid.IsTraversable(shared), Is.False);

            GridManager.ClearBlocker(2);
            Assert.That(grid.GetNode(shared).BlockerCount, Is.EqualTo(0));
            Assert.That(grid.IsTraversable(shared), Is.True);
        }

        [Test]
        public void SettingTheSameBoundsAgainChangesNothing()
        {
            GridGraph grid = Build();
            GridManager.SetBlocker(1, SmallBox());
            int version = grid.Version;

            GridManager.SetBlocker(1, SmallBox());

            Assert.That(grid.Version, Is.EqualTo(version));
        }

        [Test]
        public void ClearingAnUnknownOwnerIsANoOp()
        {
            GridGraph grid = Build();
            int version = grid.Version;

            Assert.DoesNotThrow(() => GridManager.ClearBlocker(99));
            Assert.That(grid.Version, Is.EqualTo(version));
        }

        [Test]
        public void BlockersRequestedBeforeTheBuildJoinTheConstructionBatch()
        {
            GridManager.SetBlocker(1, SmallBox());

            GridGraph grid = Build();

            Assert.That(grid.Version, Is.EqualTo(1), "One construction batch, blockers included.");
            foreach (Vector2Int cell in Square(2, 2, 5, 5))
                Assert.That(grid.GetNode(cell).BlockerCount, Is.EqualTo(1), cell.ToString());
        }

        [Test]
        public void BlockingAndClearingBeforeTheBuildLeavesNothingBehind()
        {
            GridManager.SetBlocker(1, SmallBox());
            GridManager.ClearBlocker(1);

            GridGraph grid = Build();

            for (int i = 0; i < grid.CellCount; i++)
                Assert.That(grid.GetNode(grid.FromIndex(i)).BlockerCount, Is.EqualTo(0));
        }

        [Test]
        public void AGridFootprintIsBlockedAfterTheBuildAndReleasedWhenDisabled()
        {
            Bake();
            var prop = new GameObject("Terminal");
            prop.transform.SetParent(Root.transform);
            prop.transform.position = Origin + new Vector3(2f, 0.5f, 2f);
            prop.AddComponent<BoxCollider>().size = new Vector3(0.4f, 1f, 0.4f);
            Physics.SyncTransforms();
            prop.AddComponent<GridFootprint>();

            GridGraph grid = CreateManager().BuildGrid();
            Assert.That(grid.GetNode(new Vector2Int(3, 3)).BlockerCount, Is.EqualTo(1));

            prop.SetActive(false);
            Assert.That(grid.GetNode(new Vector2Int(3, 3)).BlockerCount, Is.EqualTo(0));
        }

        [Test]
        public void AGridFootprintEnabledAfterTheBuildBlocksStraightAway()
        {
            GridGraph grid = Build();
            var prop = new GameObject("Relay");
            prop.transform.SetParent(Root.transform);
            prop.transform.position = Origin + new Vector3(2f, 0.5f, 2f);
            prop.AddComponent<BoxCollider>().size = new Vector3(0.4f, 1f, 0.4f);
            Physics.SyncTransforms();

            prop.AddComponent<GridFootprint>();

            Assert.That(grid.GetNode(new Vector2Int(3, 3)).BlockerCount, Is.EqualTo(1));
        }
    }
}
