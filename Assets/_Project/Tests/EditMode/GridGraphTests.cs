using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ToyFactory.AI.Core.Grid;
using UnityEngine;

namespace ToyFactory.Tests.EditMode
{
    public class GridGraphTests
    {
        static readonly Vector2Int Centre = new Vector2Int(1, 1);

        [Test]
        public void ConstructionInitialisesEveryNode()
        {
            var origin = new Vector3(-2f, 3f, 4f);
            var graph = new GridGraph(3, 2, origin);
            Assert.AreEqual(3, graph.Width);
            Assert.AreEqual(2, graph.Height);
            Assert.AreEqual(origin, graph.Origin);
            Assert.AreEqual(0.5f, GridGraph.CellSize);
            Assert.AreEqual(0, graph.Version);
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
            {
                var cell = new Vector2Int(x, y);
                GridNode node = graph.GetNode(cell);
                Assert.AreEqual(cell, node.Cell);
                Assert.AreEqual(new Vector3(-1.75f + x * 0.5f, 3f, 4.25f + y * 0.5f), node.WorldPosition);
                Assert.IsTrue(node.Walkable);
                Assert.IsTrue(node.IsTraversable);
                Assert.AreEqual(0, node.BlockerCount);
                Assert.IsFalse(node.IsDoorway);
            }
        }

        [TestCase(0, 2)]
        [TestCase(2, 0)]
        [TestCase(-1, 2)]
        [TestCase(2, -1)]
        [TestCase(int.MaxValue, 2)]
        public void InvalidDimensionsAreRejected(int width, int height)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridGraph(width, height, Vector3.zero));
        }

        [Test]
        public void NonFiniteOriginsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new GridGraph(1, 1, new Vector3(float.NaN, 0, 0)));
            Assert.Throws<ArgumentException>(() => new GridGraph(1, 1, new Vector3(0, float.PositiveInfinity, 0)));
            Assert.Throws<ArgumentException>(() => new GridGraph(1, 1, new Vector3(0, 0, float.NegativeInfinity)));
        }

        [TestCase(0, 0, true)]
        [TestCase(2, 1, true)]
        [TestCase(-1, 0, false)]
        [TestCase(0, -1, false)]
        [TestCase(3, 0, false)]
        [TestCase(0, 2, false)]
        public void BoundsUseExclusiveMaximum(int x, int y, bool expected)
        {
            var graph = new GridGraph(3, 2, Vector3.zero);
            Assert.AreEqual(expected, graph.Contains(new Vector2Int(x, y)));
        }

        [TestCase(0f, 0)]
        [TestCase(0.499f, 0)]
        [TestCase(0.5f, 1)]
        [TestCase(-0.001f, -1)]
        [TestCase(-0.5f, -1)]
        [TestCase(-0.501f, -2)]
        public void WorldToCellUsesFloorRelativeToOrigin(float offset, int expected)
        {
            var graph = new GridGraph(3, 2, new Vector3(-2, 7, 4));
            Assert.AreEqual(new Vector2Int(expected, expected),
                graph.WorldToCell(new Vector3(-2 + offset, -100, 4 + offset)));
        }

        [Test]
        public void TryWorldToCellChecksBothWorldBoundsWithoutClamping()
        {
            var graph = new GridGraph(3, 2, new Vector3(-2, 7, 4));
            Assert.IsTrue(graph.TryWorldToCell(new Vector3(-2, 0, 4), out Vector2Int cell));
            Assert.AreEqual(Vector2Int.zero, cell);
            Assert.IsTrue(graph.TryWorldToCell(new Vector3(-0.501f, 0, 4.999f), out cell));
            Assert.AreEqual(new Vector2Int(2, 1), cell);
            Assert.IsFalse(graph.TryWorldToCell(new Vector3(-0.5f, 0, 4), out cell));
            Assert.AreEqual(new Vector2Int(3, 0), cell);
            Assert.IsFalse(graph.TryWorldToCell(new Vector3(-2, 0, 5), out cell));
            Assert.AreEqual(new Vector2Int(0, 2), cell);
            Assert.IsFalse(graph.TryWorldToCell(new Vector3(-2.01f, 0, 4), out cell));
            Assert.AreEqual(new Vector2Int(-1, 0), cell);
        }

        [Test]
        public void CellCentresRoundTripAtOffsetOrigin()
        {
            var graph = new GridGraph(4, 3, new Vector3(-3, 2, -1));
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
            {
                var cell = new Vector2Int(x, y);
                Vector3 world = graph.CellToWorld(cell);
                Assert.AreEqual(new Vector3(-2.75f + x * 0.5f, 2, -0.75f + y * 0.5f), world);
                Assert.AreEqual(cell, graph.WorldToCell(world));
            }
        }

        [Test]
        public void UnrepresentableWorldCoordinatesAreRejected()
        {
            var graph = new GridGraph(1, 1, Vector3.zero);
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue })
                Assert.Throws<ArgumentOutOfRangeException>(() => graph.WorldToCell(new Vector3(value, 0, 0)));
        }

        [Test]
        public void OpenInteriorHasFourCardinalThenFourDiagonalNeighbours()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            CollectionAssert.AreEqual(new[]
            {
                new Vector2Int(1, 2), new Vector2Int(2, 1),
                new Vector2Int(1, 0), new Vector2Int(0, 1),
                new Vector2Int(2, 2), new Vector2Int(2, 0),
                new Vector2Int(0, 0), new Vector2Int(0, 2)
            }, graph.GetNeighbours(Centre).ToArray());
        }

        [TestCase(0, 0, 3)]
        [TestCase(2, 0, 3)]
        [TestCase(0, 2, 3)]
        [TestCase(2, 2, 3)]
        [TestCase(1, 0, 5)]
        [TestCase(0, 1, 5)]
        [TestCase(1, 2, 5)]
        [TestCase(2, 1, 5)]
        public void BoundaryNeighboursStayInsideGrid(int x, int y, int count)
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            Vector2Int[] neighbours = graph.GetNeighbours(new Vector2Int(x, y)).ToArray();
            Assert.AreEqual(count, neighbours.Length);
            Assert.IsTrue(neighbours.All(graph.Contains));
            Assert.AreEqual(count, neighbours.Distinct().Count());
        }

        [Test]
        public void SingleCellAndThinGridsHaveNoWrappingNeighbours()
        {
            Assert.IsEmpty(new GridGraph(1, 1, Vector3.zero).GetNeighbours(Vector2Int.zero));
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(0, 0), new Vector2Int(0, 2) },
                new GridGraph(1, 3, Vector3.zero).GetNeighbours(new Vector2Int(0, 1)).ToArray());
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(0, 0), new Vector2Int(2, 0) },
                new GridGraph(3, 1, Vector3.zero).GetNeighbours(new Vector2Int(1, 0)).ToArray());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BlockedSourcesAndDestinationsCannotBeTraversed(bool dynamicBlocker)
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            if (dynamicBlocker) graph.AddBlocker(Centre);
            else graph.SetWalkable(Centre, false);
            Assert.IsFalse(graph.IsTraversable(Centre));
            Assert.IsEmpty(graph.GetNeighbours(Centre));
            Assert.IsFalse(graph.GetNeighbours(new Vector2Int(1, 0)).Contains(Centre));
        }

        [TestCase(1, 1)]
        [TestCase(1, -1)]
        [TestCase(-1, 1)]
        [TestCase(-1, -1)]
        public void DiagonalsRequireBothSideCellsForStaticAndDynamicObstacles(int dx, int dy)
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            var destination = Centre + new Vector2Int(dx, dy);
            var sideX = Centre + new Vector2Int(dx, 0);
            var sideY = Centre + new Vector2Int(0, dy);
            graph.SetWalkable(sideX, false);
            Assert.IsFalse(graph.GetNeighbours(Centre).Contains(destination));
            graph.SetWalkable(sideX, true);
            graph.AddBlocker(sideY);
            Assert.IsFalse(graph.GetNeighbours(Centre).Contains(destination));
            graph.SetWalkable(sideX, false);
            Assert.IsFalse(graph.GetNeighbours(Centre).Contains(destination));
            graph.RemoveBlocker(sideY);
            graph.SetWalkable(sideX, true);
            Assert.IsTrue(graph.GetNeighbours(Centre).Contains(destination));
        }

        [Test]
        public void OverlappingBlockersRequireMatchingRemovalsAndPreserveWalkability()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            graph.AddBlocker(Centre);
            graph.AddBlocker(Centre);
            Assert.AreEqual(2, graph.GetNode(Centre).BlockerCount);
            Assert.IsTrue(graph.GetNode(Centre).Walkable);
            graph.RemoveBlocker(Centre);
            Assert.AreEqual(1, graph.GetNode(Centre).BlockerCount);
            Assert.IsFalse(graph.IsTraversable(Centre));
            graph.RemoveBlocker(Centre);
            Assert.IsTrue(graph.IsTraversable(Centre));
            graph.SetWalkable(Centre, false);
            graph.AddBlocker(Centre);
            graph.RemoveBlocker(Centre);
            Assert.IsFalse(graph.IsTraversable(Centre));
        }

        [Test]
        public void EffectiveMutationsIncrementVersionAndPublishCompletedState()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            var events = new List<GridChange>();
            graph.Changed += change =>
            {
                Assert.AreEqual(graph.Version, change.Version);
                events.Add(change);
            };
            graph.AddBlocker(Centre);
            graph.AddBlocker(Centre);
            graph.RemoveBlocker(Centre);
            graph.RemoveBlocker(Centre);
            graph.SetWalkable(Centre, false);
            graph.SetDoorway(Centre, true);
            Assert.AreEqual(6, graph.Version);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, events.Select(e => e.Version).ToArray());
            foreach (GridChange change in events)
            {
                CollectionAssert.AreEqual(new[] { Centre }, change.ChangedCells);
                Assert.AreEqual(9, change.AffectedCells.Count);
                Assert.AreEqual(9, change.AffectedCells.Distinct().Count());
                Assert.IsTrue(change.AffectedCells.All(graph.Contains));
            }
        }

        [Test]
        public void ChangeIncludesDiagonalDependencyEndpointsEvenWhenBlocked()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            GridChange observed = null;
            graph.Changed += change =>
            {
                observed = change;
                Assert.IsFalse(graph.IsTraversable(new Vector2Int(1, 0)));
            };
            graph.AddBlocker(new Vector2Int(1, 0));
            Assert.IsTrue(observed.AffectedCells.Contains(Vector2Int.zero));
            Assert.IsTrue(observed.AffectedCells.Contains(Centre));
            Assert.IsFalse(graph.GetNeighbours(Vector2Int.zero).Contains(Centre));
        }

        [Test]
        public void CornerChangePayloadIsReadOnlyAndRemainsStable()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            var events = new List<GridChange>();
            graph.Changed += events.Add;
            graph.SetDoorway(Vector2Int.zero, true);
            GridChange first = events[0];
            CollectionAssert.AreEquivalent(new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up, Centre }, first.AffectedCells);
            Assert.Throws<NotSupportedException>(() => ((IList<Vector2Int>)first.ChangedCells)[0] = Centre);
            Assert.Throws<NotSupportedException>(() => ((IList<Vector2Int>)first.AffectedCells)[0] = Centre);
            graph.AddBlocker(Centre);
            Assert.AreEqual(1, first.Version);
            Assert.AreEqual(Vector2Int.zero, first.ChangedCells[0]);
            Assert.AreEqual(4, first.AffectedCells.Count);
        }

        [Test]
        public void NoOpsAndRejectedMutationsDoNotChangeVersionOrEmitEvents()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            int notifications = 0;
            graph.Changed += _ => notifications++;
            graph.SetWalkable(Centre, true);
            graph.SetDoorway(Centre, false);
            Assert.Throws<InvalidOperationException>(() => graph.RemoveBlocker(Centre));
            var outside = new Vector2Int(-1, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.GetNode(outside));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.CellToWorld(outside));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.SetWalkable(outside, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.SetDoorway(outside, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.AddBlocker(outside));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.RemoveBlocker(outside));
            Assert.IsFalse(graph.IsTraversable(outside));
            Assert.IsEmpty(graph.GetNeighbours(outside));
            Assert.AreEqual(0, graph.Version);
            Assert.AreEqual(0, notifications);
            Assert.AreEqual(0, graph.GetNode(Centre).BlockerCount);
        }

        [Test]
        public void DoorwayMetadataDoesNotBlockAndNodeSnapshotsRemainStable()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            GridNode before = graph.GetNode(Centre);
            graph.SetDoorway(Centre, true);
            Assert.IsTrue(graph.GetNode(Centre).IsDoorway);
            Assert.IsTrue(graph.IsTraversable(Centre));
            graph.AddBlocker(Centre);
            Assert.IsTrue(graph.GetNode(Centre).IsDoorway);
            Assert.IsFalse(graph.IsTraversable(Centre));
            Assert.IsFalse(before.IsDoorway);
            Assert.IsTrue(before.IsTraversable);
            graph.RemoveBlocker(Centre);
            graph.SetDoorway(Centre, false);
            Assert.IsFalse(graph.GetNode(Centre).IsDoorway);
            Assert.IsTrue(graph.IsTraversable(Centre));
        }
    }
}
