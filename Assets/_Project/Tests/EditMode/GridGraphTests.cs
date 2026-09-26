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
        public void IndexHelpersUseRowMajorHeapIdsAndRejectInvalidInputs()
        {
            var graph = new GridGraph(4, 3, Vector3.zero);
            Assert.AreEqual(12, graph.CellCount);
            for (int i = 0; i < graph.CellCount; i++)
            {
                Assert.AreEqual(new Vector2Int(i % 4, i / 4), graph.FromIndex(i));
                Assert.AreEqual(i, graph.ToIndex(graph.FromIndex(i)));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.FromIndex(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.FromIndex(12));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.ToIndex(new Vector2Int(4, 0)));
        }

        [Test]
        public void ClosedDoorBlocksMovementButPreservesSoundConnectivityAndIdentity()
        {
            var graph = new GridGraph(3, 1, Vector3.zero);
            var door = new Vector2Int(1, 0);
            var buffer = new Vector2Int[8];
            graph.SetDoor(door, 42, true);
            GridNode node = graph.GetNode(door);
            Assert.AreEqual(42, node.DoorId);
            Assert.IsTrue(node.IsDoorClosed);
            Assert.IsTrue(node.IsDoorway);
            Assert.IsTrue(node.IsChokepoint);
            Assert.AreEqual(0, node.BlockerCount);
            Assert.IsFalse(node.IsTraversable);
            Assert.IsTrue(node.IsSoundTraversable);
            Assert.AreEqual(0, graph.GetNeighboursNonAlloc(Vector2Int.zero, buffer));
            Assert.AreEqual(1, graph.GetNeighboursNonAlloc(Vector2Int.zero, buffer, true));
            Assert.AreEqual(door, buffer[0]);
            Assert.AreEqual(2, graph.GetNeighboursNonAlloc(door, buffer, true));
            graph.SetDoor(door, 42, false);
            Assert.IsTrue(graph.IsTraversable(door));
            Assert.AreEqual(1, graph.GetNeighboursNonAlloc(Vector2Int.zero, buffer));
            graph.SetDoor(door, null, false);
            Assert.IsNull(graph.GetNode(door).DoorId);
            Assert.IsFalse(graph.GetNode(door).IsDoorClosed);
            Assert.IsTrue(graph.GetNode(door).IsDoorway);
            Assert.Throws<ArgumentException>(() => graph.SetDoor(door, null, true));
            graph.SetDoor(door, 7, true);
            graph.SetDoorway(door, false);
            Assert.IsNull(graph.GetNode(door).DoorId);
            Assert.IsFalse(graph.GetNode(door).IsDoorClosed);
            Assert.IsTrue(graph.IsTraversable(door));
        }

        [Test]
        public void DoorStateDoesNotEraseOrdinaryBlockersOrWalls()
        {
            var graph = new GridGraph(3, 1, Vector3.zero);
            var door = new Vector2Int(1, 0);
            var buffer = new Vector2Int[8];
            graph.SetDoor(door, 1, true);
            graph.AddBlocker(door);
            graph.SetDoor(door, 1, false);
            Assert.AreEqual(1, graph.GetNode(door).BlockerCount);
            Assert.IsFalse(graph.GetNode(door).IsSoundTraversable);
            Assert.AreEqual(0, graph.GetNeighboursNonAlloc(Vector2Int.zero, buffer, true));
            graph.RemoveBlocker(door);
            graph.SetWalkable(door, false);
            Assert.AreEqual(0, graph.GetNeighboursNonAlloc(Vector2Int.zero, buffer, true));
        }

        [Test]
        public void BatchPublishesOneAtomicChangeWithCompleteDistinctSets()
        {
            var graph = new GridGraph(4, 3, Vector3.zero);
            var events = new List<GridChange>();
            var other = new Vector2Int(2, 1);
            graph.Changed += change =>
            {
                Assert.AreEqual(1, graph.GetNode(Centre).BlockerCount);
                Assert.AreEqual(2, graph.GetNode(other).BlockerCount);
                Assert.AreEqual(1, graph.Version);
                events.Add(change);
            };
            using (GridGraph.Batch batch = graph.BeginBatch())
            {
                batch.AddBlocker(other);
                batch.AddBlocker(Centre);
                batch.AddBlocker(other);
                Assert.AreEqual(0, graph.GetNode(other).BlockerCount);
                Assert.AreEqual(0, graph.Version);
                Assert.IsEmpty(events);
                batch.Commit();
            }
            Assert.AreEqual(1, events.Count);
            CollectionAssert.AreEqual(new[] { Centre, other }, events[0].ChangedCells);
            CollectionAssert.AreEqual(Enumerable.Range(0, 12).Select(graph.FromIndex).ToArray(), events[0].AffectedCells);
        }

        [Test]
        public void BatchMovesOverlappingFootprintAndOmitsNetUnchangedCells()
        {
            var graph = new GridGraph(4, 1, Vector3.zero);
            var a = new Vector2Int(0, 0);
            var b = new Vector2Int(1, 0);
            var c = new Vector2Int(2, 0);
            using (GridGraph.Batch initial = graph.BeginBatch())
            {
                initial.AddBlocker(a); initial.AddBlocker(b); initial.Commit();
            }
            GridChange observed = null;
            graph.Changed += change => observed = change;
            using (GridGraph.Batch move = graph.BeginBatch())
            {
                move.RemoveBlocker(a); move.RemoveBlocker(b);
                move.AddBlocker(b); move.AddBlocker(c); move.Commit();
            }
            Assert.AreEqual(2, graph.Version);
            CollectionAssert.AreEqual(new[] { a, c }, observed.ChangedCells);
            Assert.AreEqual(1, graph.GetNode(b).BlockerCount);
        }

        [Test]
        public void MultiCellDoorClosesInOneVersionWithoutBecomingOrdinaryBlockers()
        {
            var graph = new GridGraph(3, 2, Vector3.zero);
            int events = 0;
            graph.Changed += _ => events++;
            using (GridGraph.Batch batch = graph.BeginBatch())
            {
                batch.SetDoor(new Vector2Int(1, 0), 9, true);
                batch.SetDoor(new Vector2Int(1, 1), 9, true);
                batch.Commit();
            }
            Assert.AreEqual(1, graph.Version);
            Assert.AreEqual(1, events);
            for (int y = 0; y < 2; y++)
            {
                GridNode door = graph.GetNode(new Vector2Int(1, y));
                Assert.AreEqual(9, door.DoorId);
                Assert.IsTrue(door.IsDoorClosed);
                Assert.IsTrue(door.IsSoundTraversable);
                Assert.AreEqual(0, door.BlockerCount);
            }
        }

        [Test]
        public void EmptyNoOpCancelledAndAbandonedBatchesEmitNothing()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            int events = 0;
            graph.Changed += _ => events++;
            using (GridGraph.Batch batch = graph.BeginBatch()) batch.Commit();
            using (GridGraph.Batch batch = graph.BeginBatch())
            {
                batch.SetWalkable(Centre, false); batch.SetWalkable(Centre, true);
                batch.AddBlocker(Centre); batch.RemoveBlocker(Centre);
                batch.SetDoor(Centre, 2, true); batch.SetDoorway(Centre, false);
                batch.Commit();
            }
            using (GridGraph.Batch batch = graph.BeginBatch()) batch.AddBlocker(Centre);
            Assert.AreEqual(0, graph.Version);
            Assert.AreEqual(0, events);
            Assert.IsTrue(graph.IsTraversable(Centre));
        }

        [Test]
        public void InvalidBatchScopeRollsBackAndStaleBatchesCannotOverwriteChanges()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                using (GridGraph.Batch batch = graph.BeginBatch())
                {
                    batch.AddBlocker(Centre);
                    batch.AddBlocker(new Vector2Int(-1, 0));
                    batch.Commit();
                }
            });
            Assert.AreEqual(0, graph.Version);
            Assert.AreEqual(0, graph.GetNode(Centre).BlockerCount);
            using (GridGraph.Batch stale = graph.BeginBatch())
            {
                stale.AddBlocker(Centre);
                graph.SetDoorway(Centre, true);
                Assert.Throws<InvalidOperationException>(() => stale.Commit());
            }
            Assert.AreEqual(0, graph.GetNode(Centre).BlockerCount);
            Assert.IsTrue(graph.GetNode(Centre).IsDoorway);
            var finished = graph.BeginBatch();
            finished.Commit();
            Assert.Throws<ObjectDisposedException>(() => finished.Commit());
            Assert.Throws<ObjectDisposedException>(() => finished.AddBlocker(Centre));
        }

        [Test]
        public void NonAllocNeighboursMatchConvenienceOrderAcrossEveryCell()
        {
            var graph = new GridGraph(4, 3, Vector3.zero);
            graph.SetWalkable(Centre, false);
            graph.AddBlocker(new Vector2Int(2, 1));
            graph.SetDoor(new Vector2Int(3, 1), 3, true);
            var buffer = new Vector2Int[8];
            for (int i = 0; i < graph.CellCount; i++)
            {
                Vector2Int cell = graph.FromIndex(i);
                int count = graph.GetNeighboursNonAlloc(cell, buffer);
                CollectionAssert.AreEqual(graph.GetNeighbours(cell).ToArray(), buffer.Take(count).ToArray());
            }
            Assert.AreEqual(0, graph.GetNeighboursNonAlloc(new Vector2Int(-1, 0), buffer));
            Assert.Throws<ArgumentNullException>(() => graph.GetNeighboursNonAlloc(Centre, null));
            Assert.Throws<ArgumentException>(() => graph.GetNeighboursNonAlloc(Centre, new Vector2Int[7]));
        }

        [TestCase(1, 1)]
        [TestCase(1, -1)]
        [TestCase(-1, 1)]
        [TestCase(-1, -1)]
        public void SoundNeighboursRespectWallsAndBlockersOnBothDiagonalSides(int dx, int dy)
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            var buffer = new Vector2Int[8];
            var sideX = Centre + new Vector2Int(dx, 0);
            var sideY = Centre + new Vector2Int(0, dy);
            var target = Centre + new Vector2Int(dx, dy);
            graph.SetDoor(sideX, 1, true);
            int count = graph.GetNeighboursNonAlloc(Centre, buffer, true);
            Assert.IsTrue(buffer.Take(count).Contains(target));
            count = graph.GetNeighboursNonAlloc(Centre, buffer);
            Assert.IsFalse(buffer.Take(count).Contains(target));
            graph.AddBlocker(sideY);
            count = graph.GetNeighboursNonAlloc(Centre, buffer, true);
            Assert.IsFalse(buffer.Take(count).Contains(target));
            graph.RemoveBlocker(sideY);
            graph.SetWalkable(sideX, false);
            count = graph.GetNeighboursNonAlloc(Centre, buffer, true);
            Assert.IsFalse(buffer.Take(count).Contains(target));
        }

        [Test]
        public void ReusedNeighbourBufferAndIndexHelpersAllocateZeroBytes()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            var buffer = new Vector2Int[8];
            graph.SetDoor(new Vector2Int(2, 1), 5, true);
            int checksum = 0;
            for (int i = 0; i < 100; i++)
            {
                checksum += graph.GetNeighboursNonAlloc(graph.FromIndex(graph.ToIndex(Centre)), buffer);
                checksum += graph.GetNeighboursNonAlloc(Centre, buffer, true);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                checksum += graph.GetNeighboursNonAlloc(graph.FromIndex(graph.ToIndex(Centre)), buffer);
                checksum += graph.GetNeighboursNonAlloc(Centre, buffer, true);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Assert.Greater(checksum, 0);
        }

        [Test]
        public void ChokepointsUseStaticLegalWalkableNeighboursAndDoorwayTags()
        {
            var graph = new GridGraph(3, 3, Vector3.zero);
            Assert.IsTrue(graph.GetNode(Vector2Int.zero).IsChokepoint);
            Assert.IsFalse(graph.GetNode(new Vector2Int(1, 0)).IsChokepoint);
            Assert.IsFalse(graph.GetNode(Centre).IsChokepoint);
            graph.AddBlocker(new Vector2Int(1, 0));
            graph.AddBlocker(new Vector2Int(1, 2));
            Assert.IsFalse(graph.GetNode(Centre).IsChokepoint);
            GridNode snapshot = graph.GetNode(Centre);
            using (GridGraph.Batch batch = graph.BeginBatch())
            {
                batch.SetWalkable(new Vector2Int(1, 0), false);
                batch.SetWalkable(new Vector2Int(1, 2), false);
                batch.Commit();
            }
            Assert.IsTrue(graph.GetNode(Centre).IsChokepoint);
            Assert.IsFalse(snapshot.IsChokepoint);
            Assert.IsFalse(graph.GetNode(new Vector2Int(1, 0)).IsChokepoint);
            graph.SetWalkable(new Vector2Int(1, 0), true);
            graph.SetWalkable(new Vector2Int(1, 2), true);
            Assert.IsFalse(graph.GetNode(Centre).IsChokepoint);
            graph.SetDoor(Centre, 2, true);
            Assert.IsTrue(graph.GetNode(Centre).IsChokepoint);
            graph.SetDoor(Centre, 2, false);
            Assert.IsTrue(graph.GetNode(Centre).IsChokepoint);
            graph.SetDoorway(Centre, false);
            Assert.IsFalse(graph.GetNode(Centre).IsChokepoint);
        }

        [Test]
        public void DoorIdentityStateAndNoOpsParticipateInVersioning()
        {
            var graph = new GridGraph(1, 1, Vector3.zero);
            int events = 0;
            graph.Changed += _ => events++;
            graph.SetDoor(Vector2Int.zero, 0, false);
            graph.SetDoor(Vector2Int.zero, 0, false);
            graph.SetDoor(Vector2Int.zero, 0, true);
            graph.SetDoor(Vector2Int.zero, 1, true);
            Assert.AreEqual(3, graph.Version);
            Assert.AreEqual(3, events);
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
