using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    public sealed class GridManagerTests : GridWorldFixture
    {
        [UnityTest]
        public IEnumerator EnablingAndStartingDoNotBuild()
        {
            Bake();
            GridManager manager = CreateManager();
            Assert.That(GridManager.Instance, Is.SameAs(manager));
            yield return null;
            Assert.That(GridManager.Current, Is.Null);
        }

        [Test]
        public void BuildPublishesCompletedGraphBeforeReadyWithExistingCoordinates()
        {
            AddBox("Wall", Origin + new Vector3(2f, 2f, 2f), new Vector3(0.5f, 4f, 8f));
            Bake();
            GridManager manager = CreateManager();
            int calls = 0;
            GridManager.Ready += ready =>
            {
                calls++;
                Assert.That(GridManager.Current, Is.SameAs(ready));
                Assert.That(ready.IsTraversable(new Vector2Int(4, 4)), Is.False);
                Assert.That(ready.IsTraversable(Vector2Int.zero), Is.True);
                Assert.That(ready.Version, Is.EqualTo(1), "All construction changes must share one batch.");
            };
            var grid = manager.BuildGrid();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(grid, Is.SameAs(GridManager.Current));
            Assert.That(grid.Width, Is.EqualTo(8));
            Assert.That(grid.Height, Is.EqualTo(8));
            Assert.That(grid.Origin, Is.EqualTo(Origin));
            Assert.That(grid.CellToWorld(Vector2Int.zero), Is.EqualTo(Origin + new Vector3(0.25f, 0f, 0.25f)));
            Assert.That(grid.WorldToCell(Origin + new Vector3(0.75f, 5f, 0.25f)), Is.EqualTo(new Vector2Int(1, 0)));
        }

        [Test]
        public void RepeatedBuildReturnsSameGraphWithoutReadyOrVersionChange()
        {
            Bake();
            GridManager manager = CreateManager();
            int calls = 0;
            GridManager.Ready += _ => calls++;
            var first = manager.BuildGrid();
            Assert.That(first.Version, Is.Zero, "An entirely walkable grid has no effective initial mutation.");
            SetField(manager, "width", 0); // Repeated calls do not secretly rebuild changed configuration.
            LogAssert.Expect(LogType.Warning, "Grid already built; returning the existing graph.");
            Assert.That(manager.BuildGrid(), Is.SameAs(first));
            Assert.That(first.Version, Is.Zero);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void MissingBakeDoesNotPublishAndCanBeRetried()
        {
            GridManager manager = CreateManager();
            int calls = 0;
            GridManager.Ready += _ => calls++;
            Assert.Throws<InvalidOperationException>(() => manager.BuildGrid());
            Assert.That(GridManager.Current, Is.Null);
            Assert.That(calls, Is.Zero);
            Bake();
            Assert.That(manager.BuildGrid(), Is.Not.Null);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void MissingSurfaceIsRejected()
        {
            GridManager manager = CreateManager();
            SetField(manager, "surface", null);
            Assert.Throws<InvalidOperationException>(() => manager.BuildGrid());
            Assert.That(GridManager.Current, Is.Null);
        }

        [Test]
        public void DisabledSurfaceIsRejected()
        {
            Bake();
            GridManager manager = CreateManager();
            Surface.enabled = false;
            Assert.Throws<InvalidOperationException>(() => manager.BuildGrid());
            Assert.That(GridManager.Current, Is.Null);
        }

        [TestCase(0, 8)]
        [TestCase(8, -1)]
        [TestCase(int.MaxValue, 2)]
        public void InvalidDimensionsFailBeforePublication(int width, int height)
        {
            GridManager manager = CreateManager();
            SetField(manager, "width", width);
            SetField(manager, "height", height);
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.BuildGrid());
            Assert.That(GridManager.Current, Is.Null);
        }

        [Test]
        public void NonFiniteOriginFailsBeforePublication()
        {
            GridManager manager = CreateManager();
            SetField(manager, "origin", new Vector3(0f, float.PositiveInfinity, 0f));
            Assert.Throws<ArgumentException>(() => manager.BuildGrid());
            Assert.That(GridManager.Current, Is.Null);
        }

        [Test]
        public void UnsupportedGridDoesNotPublishPartialGraphOrReady()
        {
            Bake();
            GridManager manager = CreateManager();
            SetField(manager, "origin", Origin + Vector3.right * 100f);
            int calls = 0;
            GridManager.Ready += _ => calls++;
            Assert.Throws<InvalidOperationException>(() => manager.BuildGrid());
            Assert.That(GridManager.Current, Is.Null);
            Assert.That(calls, Is.Zero);
            SetField(manager, "origin", Origin);
            Assert.That(manager.BuildGrid(), Is.Not.Null);
        }

        [Test]
        public void DuplicateCannotReplaceOwnerOrClearItsPublication()
        {
            Bake();
            GridManager owner = CreateManager();
            var first = owner.BuildGrid();
            LogAssert.Expect(LogType.Warning, "Another GridManager is already active; this manager cannot build.");
            GridManager duplicate = CreateManager();
            Assert.Throws<InvalidOperationException>(() => duplicate.BuildGrid());
            UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
            Assert.That(GridManager.Instance, Is.SameAs(owner));
            Assert.That(GridManager.Current, Is.SameAs(first));
        }

        [Test]
        public void DestroyingOwnerClearsPublication()
        {
            Bake();
            GridManager manager = CreateManager();
            manager.BuildGrid();
            UnityEngine.Object.DestroyImmediate(manager.gameObject);
            Assert.That(GridManager.Instance, Is.Null);
            Assert.That(GridManager.Current, Is.Null);
        }

        [Test]
        public void DisabledManagerCannotPublishAndCanBuildAfterReenable()
        {
            Bake();
            GridManager manager = CreateManager();
            manager.BuildGrid();
            manager.enabled = false;
            Assert.That(GridManager.Current, Is.Null);
            Assert.That(GridManager.Instance, Is.Null);
            Assert.Throws<InvalidOperationException>(() => manager.BuildGrid());
            manager.enabled = true;
            Assert.That(manager.BuildGrid(), Is.SameAs(GridManager.Current));
        }

        [Test]
        public void NewSessionClearsListenersAndRebuildsEvenWhenComponentSurvives()
        {
            Bake();
            GridManager manager = CreateManager();
            int oldCalls = 0;
            GridManager.Ready += _ => oldCalls++;
            var old = manager.BuildGrid();
            ResetPublication();
            Assert.That(GridManager.Instance, Is.Null);
            Assert.That(GridManager.Current, Is.Null);
            int newCalls = 0;
            GridManager.Ready += _ => newCalls++;
            var current = manager.BuildGrid();
            Assert.That(current, Is.Not.SameAs(old));
            Assert.That(GridManager.Instance, Is.SameAs(manager));
            Assert.That(oldCalls, Is.EqualTo(1));
            Assert.That(newCalls, Is.EqualTo(1));
        }

        [Test]
        public void ThrowingReadyListenerDoesNotRollBackSuccessfulPublication()
        {
            Bake();
            GridManager manager = CreateManager();
            GridManager.Ready += _ => throw new InvalidOperationException("Listener failed");
            Assert.Throws<InvalidOperationException>(() => manager.BuildGrid());
            var published = GridManager.Current;
            Assert.That(published, Is.Not.Null);
            LogAssert.Expect(LogType.Warning, "Grid already built; returning the existing graph.");
            Assert.That(manager.BuildGrid(), Is.SameAs(published));
        }
    }
}
