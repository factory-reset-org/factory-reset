using System;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class GridRegionsTests
    {
        // A 9 x 5 room cut in two by a wall down column 4.
        static GridGraph SplitRoom()
        {
            var grid = new GridGraph(9, 5, Vector3.zero);
            for (int y = 0; y < 5; y++)
                grid.SetWalkable(new Vector2Int(4, y), false);
            return grid;
        }

        [Test]
        public void AnOpenGridIsOneRegion()
        {
            var grid = new GridGraph(6, 6, Vector3.zero);
            GridRegions regions = GridRegions.For(grid);

            Assert.AreEqual(1, regions.Count);
            Assert.IsTrue(regions.Connected(new Vector2Int(0, 0), new Vector2Int(5, 5)));
        }

        [Test]
        public void AWallAcrossTheRoomMakesTwoRegions()
        {
            GridRegions regions = GridRegions.For(SplitRoom());

            Assert.AreEqual(2, regions.Count);
            Assert.IsTrue(regions.Connected(new Vector2Int(0, 0), new Vector2Int(3, 4)));
            Assert.IsTrue(regions.Connected(new Vector2Int(5, 0), new Vector2Int(8, 4)));
            Assert.IsFalse(regions.Connected(new Vector2Int(3, 2), new Vector2Int(5, 2)));
        }

        [Test]
        public void ItAgreesWithASearchOnEveryPairOfASmallGrid()
        {
            GridGraph grid = SplitRoom();
            grid.SetWalkable(new Vector2Int(6, 2), false);
            GridRegions regions = GridRegions.For(grid);
            var search = new AStarSearch(grid);

            for (int a = 0; a < grid.CellCount; a++)
            for (int b = 0; b < grid.CellCount; b++)
            {
                Vector2Int from = grid.FromIndex(a), to = grid.FromIndex(b);
                bool found = search.FindPath(from, to, BaseCostModel.Instance).Found;
                Assert.AreEqual(found, regions.Connected(from, to), $"{from} to {to}");
            }
        }

        [Test]
        public void ACellThatIsNotTraversableIsConnectedToNothing()
        {
            GridRegions regions = GridRegions.For(SplitRoom());
            var wall = new Vector2Int(4, 2);

            Assert.IsFalse(regions.Connected(wall, new Vector2Int(3, 2)));
            Assert.IsFalse(regions.Connected(wall, wall));
            Assert.IsFalse(regions.Connected(new Vector2Int(-1, 0), new Vector2Int(0, 0)));
        }

        [Test]
        public void OpeningAGapJoinsTheRegionsAndRebuildsOnlyThen()
        {
            GridGraph grid = SplitRoom();
            GridRegions regions = GridRegions.For(grid);
            var west = new Vector2Int(3, 2);
            var east = new Vector2Int(5, 2);

            Assert.IsFalse(regions.Connected(west, east));
            Assert.IsFalse(regions.Connected(west, east));
            int rebuilds = regions.Rebuilds;
            Assert.AreEqual(1, rebuilds, "Asked twice, worked out once.");

            grid.SetWalkable(new Vector2Int(4, 2), true);

            Assert.IsTrue(regions.Connected(west, east));
            Assert.AreEqual(1, regions.Count);
            Assert.AreEqual(rebuilds + 1, regions.Rebuilds);
        }

        [Test]
        public void TheSameGridSharesOneInstance()
        {
            var grid = new GridGraph(4, 4, Vector3.zero);

            Assert.AreSame(GridRegions.For(grid), GridRegions.For(grid));
            Assert.Throws<ArgumentNullException>(() => GridRegions.For(null));
        }

        // ---- OneToOneCost's effort limit ----------------------------------------------

        [Test]
        public void ACostSearchGivesUpWhenItRunsOutOfEffort()
        {
            var grid = new GridGraph(40, 40, Vector3.zero);
            var query = new OneToOneCost(grid);
            var from = new Vector2Int(0, 0);
            var to = new Vector2Int(39, 39);

            float full = query.Compute(from, to, BaseCostModel.Instance);
            Assert.IsFalse(float.IsPositiveInfinity(full));

            float limited = query.Compute(from, to, BaseCostModel.Instance, float.PositiveInfinity, 5);
            Assert.IsTrue(float.IsPositiveInfinity(limited));
            Assert.LessOrEqual(query.NodesExpanded, 6);

            // A limit it does not reach changes nothing.
            Assert.AreEqual(full, query.Compute(from, to, BaseCostModel.Instance, float.PositiveInfinity, 100000), 1e-4f);
        }
    }
}
