using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    public sealed class GearMeshTests
    {
        readonly List<Mesh> _meshes = new List<Mesh>();

        [TearDown]
        public void TearDown()
        {
            foreach (Mesh mesh in _meshes)
                if (mesh != null)
                    UnityEngine.Object.DestroyImmediate(mesh);
            _meshes.Clear();
        }

        Mesh Create(int teeth = 12)
        {
            Mesh mesh = GearMesh.Create(0.8f, 0.64f, 0.22f, teeth, 0.14f);
            _meshes.Add(mesh);
            return mesh;
        }

        [Test]
        public void HasTheDocumentedTriangleCount()
        {
            Assert.That(Create(12).triangles.Length / 3, Is.EqualTo(GearMesh.TriangleCount(12)));
            Assert.That(Create(9).triangles.Length / 3, Is.EqualTo(GearMesh.TriangleCount(9)));
        }

        [Test]
        public void BoundsMatchTheTipRadiusAndThicknessCentredOnTheOrigin()
        {
            Bounds bounds = Create().bounds;
            Assert.That(bounds.center.magnitude, Is.LessThan(0.05f));
            Assert.That(bounds.extents.x, Is.EqualTo(0.8f).Within(0.01f));
            Assert.That(bounds.extents.y, Is.EqualTo(0.8f).Within(0.02f));
            Assert.That(bounds.extents.z, Is.EqualTo(0.07f).Within(1e-4f));
        }

        [Test]
        public void FacesPointAlongTheAxleAndWallsPointAwayFromOrInToTheAxle()
        {
            Mesh mesh = Create();
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            int[] triangles = mesh.triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 n = normals[triangles[t]];
                Vector3 centre = (vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]]) / 3f;
                if (Mathf.Abs(n.z) > 0.99f)
                {
                    Assert.That(Mathf.Sign(n.z), Is.EqualTo(Mathf.Sign(centre.z)), $"Face {t / 3} faces inward.");
                    continue;
                }
                var radial = new Vector2(centre.x, centre.y);
                bool holeWall = radial.magnitude < 0.3f;
                float dot = Vector2.Dot(new Vector2(n.x, n.y), radial.normalized);
                if (holeWall)
                    Assert.That(dot, Is.LessThan(0f), $"Hole wall {t / 3} faces away from the axle.");
                else
                    Assert.That(dot, Is.GreaterThan(0f), $"Outer wall {t / 3} faces the axle.");
            }
        }

        [Test]
        public void WindingMatchesTheStoredNormals()
        {
            Mesh mesh = Create();
            Vector3[] stored = mesh.normals;
            Mesh check = UnityEngine.Object.Instantiate(mesh);
            _meshes.Add(check);
            check.RecalculateNormals();
            Vector3[] fromWinding = check.normals;
            for (int i = 0; i < stored.Length; i++)
                Assert.That(Vector3.Dot(stored[i], fromWinding[i]), Is.GreaterThan(0.999f), $"Vertex {i}.");
        }

        [TestCase(0.8f, 0.64f, 0.7f)]   // hole wider than the root
        [TestCase(0.6f, 0.64f, 0.2f)]   // tip inside the root
        [TestCase(0.8f, 0.64f, 0f)]     // no hole
        public void BadRadiiThrow(float tip, float root, float hole) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => GearMesh.Create(tip, root, hole, 12, 0.1f));

        [Test]
        public void TooFewTeethOrNoThicknessThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GearMesh.Create(0.8f, 0.6f, 0.2f, 2, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => GearMesh.Create(0.8f, 0.6f, 0.2f, 12, 0f));
        }
    }
}
