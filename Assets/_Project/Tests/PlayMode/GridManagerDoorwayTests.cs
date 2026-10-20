using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    public sealed class GridManagerDoorwayTests : GridWorldFixture
    {
        [Test]
        public void BuildMapsDoorCellsInRowMajorOrderAndAppliesInitialState()
        {
            Bake();
            CreateDoorway(12, true, Origin + new Vector3(0.5f, 0f, 0.5f),
                new Vector3(1f, 2f, 1f));
            GridGraph grid = CreateManager().BuildGrid();

            var expected = new[]
            {
                new Vector2Int(0, 0), new Vector2Int(1, 0),
                new Vector2Int(0, 1), new Vector2Int(1, 1)
            };
            foreach (Vector2Int cell in expected)
            {
                GridNode node = grid.GetNode(cell);
                Assert.That(node.IsDoorway, Is.True);
                Assert.That(node.DoorId, Is.EqualTo(12));
                Assert.That(node.IsDoorClosed, Is.True);
                Assert.That(node.IsTraversable, Is.False);
                Assert.That(node.IsSoundTraversable, Is.True);
            }
            Assert.That(grid.GetNode(new Vector2Int(2, 0)).IsDoorway, Is.False);
            Assert.That(grid.Version, Is.EqualTo(1), "Initial doorway metadata belongs to the construction batch.");
        }

        [Test]
        public void OpenInitialDoorIsTraversableAndStillTagged()
        {
            Bake();
            CreateDoorway(4, false, Origin + new Vector3(0.25f, 0f, 0.25f),
                new Vector3(0.49f, 2f, 0.49f));
            GridNode node = CreateManager().BuildGrid().GetNode(Vector2Int.zero);
            Assert.That(node.IsDoorway, Is.True);
            Assert.That(node.DoorId, Is.EqualTo(4));
            Assert.That(node.IsDoorClosed, Is.False);
            Assert.That(node.IsTraversable, Is.True);
        }

        [Test]
        public void SetDoorClosedUpdatesEveryCellInOneBatchAndDeterministicOrder()
        {
            Bake();
            CreateDoorway(7, false, Origin + new Vector3(0.5f, 0f, 0.25f),
                new Vector3(1f, 2f, 0.49f));
            GridGraph grid = CreateManager().BuildGrid();
            GridChange observed = null;
            int changes = 0;
            grid.Changed += change => { changes++; observed = change; };
            int before = grid.Version;

            GridManager.SetDoorClosed(7, true);

            Assert.That(grid.Version, Is.EqualTo(before + 1));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(observed.ChangedCells, Is.EqualTo(new[]
            {
                new Vector2Int(0, 0), new Vector2Int(1, 0)
            }));
            Assert.That(observed.MovementChanged, Is.True);
            Assert.That(observed.SoundChanged, Is.True);
            Assert.That(grid.GetNode(new Vector2Int(0, 0)).IsDoorClosed, Is.True);
            Assert.That(grid.GetNode(new Vector2Int(1, 0)).IsDoorClosed, Is.True);
        }

        [Test]
        public void RepeatingDoorStateIsNoOp()
        {
            Bake();
            CreateDoorway(3, true, Origin + new Vector3(0.25f, 0f, 0.25f),
                new Vector3(0.49f, 2f, 0.49f));
            GridGraph grid = CreateManager().BuildGrid();
            int before = grid.Version;
            int changes = 0;
            grid.Changed += _ => changes++;
            GridManager.SetDoorClosed(3, true);
            Assert.That(grid.Version, Is.EqualTo(before));
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void ClosedDoorBlocksMovementButAllowsSoundNeighbours()
        {
            Bake();
            CreateDoorway(5, true, Origin + new Vector3(0.75f, 0f, 0.25f),
                new Vector3(0.49f, 2f, 0.49f));
            GridGraph grid = CreateManager().BuildGrid();
            var buffer = new Vector2Int[8];
            int movementCount = grid.GetNeighboursNonAlloc(Vector2Int.zero, buffer);
            Assert.That(Array.IndexOf(buffer, new Vector2Int(1, 0), 0, movementCount), Is.EqualTo(-1));
            int soundCount = grid.GetNeighboursNonAlloc(Vector2Int.zero, buffer, allowClosedDoors: true);
            Assert.That(Array.IndexOf(buffer, new Vector2Int(1, 0), 0, soundCount), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void PreBuildUpdateWarnsAndDoesNothing()
        {
            LogAssert.Expect(LogType.Warning, "Cannot update a door before the grid has been built.");
            GridManager.SetDoorClosed(9, true);
            Assert.That(GridManager.Current, Is.Null);
        }

        [Test]
        public void UnknownDoorIdWarnsAndDoesNotMutateGraph()
        {
            Bake();
            CreateDoorway(2, false, Origin + new Vector3(0.25f, 0f, 0.25f),
                new Vector3(0.49f, 2f, 0.49f));
            GridGraph grid = CreateManager().BuildGrid();
            int before = grid.Version;
            LogAssert.Expect(LogType.Warning, "Door ID 99 is not registered with the grid.");
            GridManager.SetDoorClosed(99, true);
            Assert.That(grid.Version, Is.EqualTo(before));
        }

        [Test]
        public void DuplicateDoorIdsRejectBuildWithoutPublication()
        {
            Bake();
            CreateDoorway(1, false, Origin + new Vector3(0.25f, 0f, 0.25f), Vector3.one);
            CreateDoorway(1, true, Origin + new Vector3(1.25f, 0f, 0.25f), Vector3.one);
            Assert.Throws<InvalidOperationException>(() => CreateManager().BuildGrid());
            Assert.That(GridManager.Current, Is.Null);
        }

        [Test]
        public void OverlappingDoorOwnershipRejectsBuildWithoutPublication()
        {
            Bake();
            CreateDoorway(1, false, Origin + new Vector3(0.25f, 0f, 0.25f), Vector3.one);
            CreateDoorway(2, true, Origin + new Vector3(0.25f, 0f, 0.25f), Vector3.one);
            Assert.Throws<InvalidOperationException>(() => CreateManager().BuildGrid());
            Assert.That(GridManager.Current, Is.Null);
        }

        [Test]
        public void MarkerContainingNoCellCentreRejectsBuild()
        {
            Bake();
            CreateDoorway(1, false, Origin + new Vector3(20f, 0f, 20f), Vector3.one);
            Assert.Throws<InvalidOperationException>(() => CreateManager().BuildGrid());
            Assert.That(GridManager.Current, Is.Null);
        }

        [TestCase(InvalidMarker.NegativeId)]
        [TestCase(InvalidMarker.NotTrigger)]
        [TestCase(InvalidMarker.DisabledCollider)]
        [TestCase(InvalidMarker.ZeroVolume)]
        public void InvalidMarkerRejectsBuild(InvalidMarker invalid)
        {
            Bake();
            DoorwayMarker marker = CreateDoorway(1, false,
                Origin + new Vector3(0.25f, 0f, 0.25f), Vector3.one);
            switch (invalid)
            {
                case InvalidMarker.NegativeId:
                    SetMarkerField(marker, "doorId", -1);
                    break;
                case InvalidMarker.NotTrigger:
                    marker.GetComponent<BoxCollider>().isTrigger = false;
                    break;
                case InvalidMarker.DisabledCollider:
                    marker.GetComponent<BoxCollider>().enabled = false;
                    break;
                case InvalidMarker.ZeroVolume:
                    marker.GetComponent<BoxCollider>().size = Vector3.zero;
                    break;
            }
            Assert.Throws<InvalidOperationException>(() => CreateManager().BuildGrid());
            Assert.That(GridManager.Current, Is.Null);
        }

        [Test]
        public void RotatedMarkerUsesItsOrientedVolume()
        {
            Bake();
            DoorwayMarker marker = CreateDoorway(8, false,
                Origin + new Vector3(1.75f, 0f, 1.75f), new Vector3(1.6f, 2f, 0.4f));
            marker.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            GridGraph grid = CreateManager().BuildGrid();
            Assert.That(grid.GetNode(new Vector2Int(3, 3)).DoorId, Is.EqualTo(8));
            Assert.That(grid.GetNode(new Vector2Int(4, 2)).DoorId, Is.EqualTo(8));
            Assert.That(grid.GetNode(new Vector2Int(2, 4)).DoorId, Is.EqualTo(8));
            Assert.That(grid.GetNode(new Vector2Int(4, 3)).DoorId, Is.Null);
            Assert.That(grid.GetNode(new Vector2Int(3, 4)).DoorId, Is.Null);
        }

        DoorwayMarker CreateDoorway(int id, bool closed, Vector3 position, Vector3 size)
        {
            var gameObject = new GameObject($"Doorway {id}");
            gameObject.transform.SetParent(Root.transform);
            gameObject.transform.position = position;
            BoxCollider volume = gameObject.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = size;
            DoorwayMarker marker = gameObject.AddComponent<DoorwayMarker>();
            SetMarkerField(marker, "doorId", id);
            SetMarkerField(marker, "initiallyClosed", closed);
            return marker;
        }

        static void SetMarkerField(DoorwayMarker marker, string name, object value) =>
            typeof(DoorwayMarker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(marker, value);

        public enum InvalidMarker
        {
            NegativeId,
            NotTrigger,
            DisabledCollider,
            ZeroVolume
        }
    }
}
