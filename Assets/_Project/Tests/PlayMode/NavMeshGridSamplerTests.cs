using System;
using System.Reflection;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    // Temporary navigation data keeps these tests independent of Env and project bake settings.
    public abstract class GridWorldFixture
    {
        protected readonly Vector3 Origin = new Vector3(1000f, 0f, 1000f);
        protected GameObject Root;
        protected NavMeshSurface Surface;

        [SetUp]
        public void SetUpWorld()
        {
            ResetPublication();
            Root = new GameObject("Grid test world");
            Surface = Root.AddComponent<NavMeshSurface>();
            Surface.agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID;
            Surface.collectObjects = CollectObjects.Children;
            Surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            AddBox("Floor", Origin + new Vector3(2f, -0.1f, 2f), new Vector3(8f, 0.2f, 8f));
        }

        [TearDown]
        public void TearDownWorld()
        {
            NavMeshData data = Surface != null ? Surface.navMeshData : null;
            if (Surface != null) Surface.RemoveData();
            if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
            if (data != null) UnityEngine.Object.DestroyImmediate(data);
            ResetPublication();
        }

        protected void AddBox(string name, Vector3 position, Vector3 size)
        {
            var box = new GameObject(name);
            box.transform.SetParent(Root.transform);
            box.transform.position = position;
            box.AddComponent<BoxCollider>().size = size;
        }

        protected void Bake()
        {
            Physics.SyncTransforms();
            Surface.BuildNavMesh();
            Assert.That(Surface.navMeshData, Is.Not.Null);
        }

        protected NavMeshGridSampler Sampler(int mask = NavMesh.AllAreas,
            float horizontal = 0.01f, float vertical = 0.1f) =>
            new NavMeshGridSampler(Surface.agentTypeID, mask, 0.2f, horizontal, vertical);

        protected static void ResetPublication() =>
            typeof(GridManager).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        protected static void SetField(GridManager manager, string name, object value) =>
            typeof(GridManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, value);

        protected GridManager CreateManager()
        {
            var owner = new GameObject("Grid manager");
            owner.transform.SetParent(Root.transform);
            var manager = owner.AddComponent<GridManager>();
            SetField(manager, "surface", Surface);
            SetField(manager, "origin", Origin);
            SetField(manager, "width", 8);
            SetField(manager, "height", 8);
            return manager;
        }
    }

    public sealed class NavMeshGridSamplerTests : GridWorldFixture
    {
        [Test]
        public void SupportedCentreIsWalkableButMissingFloorIsNot()
        {
            Bake();
            var sampler = Sampler();
            Assert.That(sampler.IsWalkable(Origin + new Vector3(2f, 0f, 2f)), Is.True);
            Assert.That(sampler.IsWalkable(Origin + new Vector3(20f, 0f, 2f)), Is.False);
        }

        [Test]
        public void BakedWallAndItsClearanceAreNotWalkable()
        {
            AddBox("Wall", Origin + new Vector3(2f, 2f, 2f), new Vector3(0.5f, 4f, 8f));
            Bake();
            Assert.That(Sampler().IsWalkable(Origin + new Vector3(2f, 0f, 2f)), Is.False);
            Assert.That(Sampler().IsWalkable(Origin + new Vector3(2.3f, 0f, 2f)), Is.False);
            Assert.That(Sampler().IsWalkable(Origin + new Vector3(0f, 0f, 2f)), Is.True);
        }

        [Test]
        public void AreaFilterExcludesOtherwiseSupportedCells()
        {
            Surface.defaultArea = 3;
            Bake();
            Vector3 point = Origin + new Vector3(2f, 0f, 2f);
            Assert.That(Sampler(1 << 3).IsWalkable(point), Is.True);
            Assert.That(Sampler(1).IsWalkable(point), Is.False);
        }

        [Test]
        public void NearbyNavMeshAcrossAnEdgeDoesNotSupportTheRequestedCentre()
        {
            Bake();
            Vector3 inside = Origin + new Vector3(2f, 0f, 2f);
            Assert.That(NavMesh.FindClosestEdge(inside,
                out NavMeshHit edge, NavMesh.AllAreas), Is.True);
            // Derive the outward direction geometrically instead of assuming the
            // orientation of the navigation API's edge normal.
            Vector3 outward = edge.position - inside;
            outward.y = 0f;
            Vector3 outside = edge.position + outward.normalized * 0.05f;
            Assert.That(NavMesh.SamplePosition(outside, out NavMeshHit hit, 0.2f, NavMesh.AllAreas), Is.True);
            Assert.That(Vector3.Distance(hit.position, outside), Is.GreaterThan(0.01f));
            Assert.That(Sampler(horizontal: 0.001f).IsWalkable(outside), Is.False);
        }

        [TestCase(-0.05f)]
        [TestCase(0.05f)]
        public void NearbyDifferentFloorHeightIsRejected(float offset)
        {
            Bake();
            Assert.That(NavMesh.SamplePosition(Origin + new Vector3(2f, 0f, 2f),
                out NavMeshHit hit, 0.2f, NavMesh.AllAreas), Is.True);
            Assert.That(Sampler(vertical: 0.01f).IsWalkable(hit.position + Vector3.up * offset), Is.False);
        }

        [Test]
        public void UnknownAgentAndEmptyAreaMaskAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new NavMeshGridSampler(int.MaxValue, -1, 0.1f, 0.01f, 0.1f));
            Assert.Throws<ArgumentException>(() => Sampler(0));
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(-0.1f, 0f, 0f)]
        [TestCase(0.3f, 0.01f, 0.1f)]
        [TestCase(float.NaN, 0f, 0f)]
        [TestCase(0.1f, -0.01f, 0f)]
        [TestCase(0.1f, 0.2f, 0f)]
        [TestCase(0.1f, 0f, float.PositiveInfinity)]
        [TestCase(0.1f, 0f, 0.2f)]
        public void InvalidSamplingLimitsAreRejected(float distance, float horizontal, float vertical)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NavMeshGridSampler(Surface.agentTypeID, -1, distance, horizontal, vertical));
        }

        [Test]
        public void NonFiniteCentreIsRejected()
        {
            Assert.Throws<ArgumentException>(() => Sampler().IsWalkable(new Vector3(float.NaN, 0f, 0f)));
        }
    }
}
