using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    public sealed class RoundedBoxMeshTests
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

        Mesh Create(Vector3 size, float radius, int segments)
        {
            Mesh mesh = RoundedBoxMesh.Create(size, radius, segments);
            _meshes.Add(mesh);
            return mesh;
        }

        static Vector3 NearestCorePoint(Vector3 p, Vector3 core) => new Vector3(
            Mathf.Clamp(p.x, -core.x, core.x), Mathf.Clamp(p.y, -core.y, core.y), Mathf.Clamp(p.z, -core.z, core.z));

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void HasTheDocumentedTriangleCount(int segments) =>
            Assert.That(Create(new Vector3(2f, 2.8f, 2f), 0.25f, segments).triangles.Length / 3,
                Is.EqualTo(RoundedBoxMesh.TriangleCount(segments)));

        [Test]
        public void BoundsMatchTheRequestedSizeCentredOnTheOrigin()
        {
            var size = new Vector3(9f, 3.2f, 1f);
            Bounds bounds = Create(size, 0.25f, 3).bounds;
            Assert.That(Vector3.Distance(bounds.center, Vector3.zero), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(bounds.size, size), Is.LessThan(1e-4f));
        }

        [Test]
        public void EveryVertexSitsOnTheRoundedSurface()
        {
            // A rounded box is every point at the radius from its core (the box shrunk by the radius).
            var size = new Vector3(2f, 1.2f, 1f);
            const float radius = 0.25f;
            Vector3 core = size * 0.5f - Vector3.one * radius;
            foreach (Vector3 p in Create(size, radius, 3).vertices)
                Assert.That(Vector3.Distance(p, NearestCorePoint(p, core)), Is.EqualTo(radius).Within(1e-4f), $"Vertex {p}.");
        }

        [Test]
        public void NormalsAreUnitLengthAndPointAwayFromTheCore()
        {
            var size = new Vector3(2f, 1.2f, 1f);
            const float radius = 0.25f;
            Mesh mesh = Create(size, radius, 2);
            Vector3 core = size * 0.5f - Vector3.one * radius;
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 outward = (vertices[i] - NearestCorePoint(vertices[i], core)).normalized;
                Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(1e-4f));
                Assert.That(Vector3.Dot(normals[i], outward), Is.GreaterThan(0.9999f), $"Vertex {i}.");
            }
        }

        [Test]
        public void EveryTriangleFacesOutward()
        {
            // Unity treats clockwise triangles as front faces, and their Cross(b - a, c - a)
            // points out of them, so it must agree with the smooth normals.
            Mesh mesh = Create(new Vector3(0.4f, 0.4f, 0.4f), 0.1f, 3);
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            int[] triangles = mesh.triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Vector3 facing = Vector3.Cross(b - a, c - a);
                if (facing.sqrMagnitude < 1e-12f)
                    continue;
                Vector3 smooth = normals[triangles[t]] + normals[triangles[t + 1]] + normals[triangles[t + 2]];
                Assert.That(Vector3.Dot(facing, smooth), Is.GreaterThan(0f), $"Triangle {t / 3} faces inward.");
            }
        }

        [Test]
        public void SeamsBetweenFacesShareAPositionAndANormal()
        {
            // Smooth shading across an edge needs the two faces' seam vertices to agree.
            Mesh mesh = Create(new Vector3(1f, 1f, 1f), 0.25f, 2);
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            int seams = 0;
            for (int i = 0; i < vertices.Length; i++)
            for (int j = i + 1; j < vertices.Length; j++)
            {
                if ((vertices[i] - vertices[j]).sqrMagnitude > 1e-10f)
                    continue;
                seams++;
                Assert.That(Vector3.Dot(normals[i], normals[j]), Is.GreaterThan(0.9999f), $"Vertices {i} and {j}.");
            }
            Assert.That(seams, Is.GreaterThan(0));
        }

        [Test]
        public void TheRoundingRuleFitsEachKindOfLevelPiece()
        {
            Assert.That(RoundedBoxMesh.RecommendedRadius(new Vector3(2f, 2.8f, 2f)), Is.EqualTo(0.25f));                    // press
            Assert.That(RoundedBoxMesh.RecommendedRadius(new Vector3(0.5f, 6f, 40.5f)), Is.EqualTo(0.08f));                 // wall
            Assert.That(RoundedBoxMesh.RecommendedRadius(new Vector3(0.5f, 2f, 3f)), Is.EqualTo(0.125f));                   // lintel
            Assert.That(RoundedBoxMesh.RecommendedRadius(new Vector3(0.3f, 0.3f, 0.3f)), Is.EqualTo(0.075f).Within(1e-6f)); // toy
            float strip = RoundedBoxMesh.RecommendedRadius(new Vector3(2.9f, 0.012f, 0.15f));                            // floor outline
            Assert.That(strip, Is.GreaterThan(0f).And.LessThan(0.006f));
        }

        [Test]
        public void BigCurvesGetMoreSegmentsThanToys()
        {
            Assert.That(RoundedBoxMesh.RecommendedSegments(new Vector3(2f, 2.8f, 2f), 0.25f), Is.EqualTo(3));   // press
            Assert.That(RoundedBoxMesh.RecommendedSegments(new Vector3(0.5f, 6f, 20f), 0.08f), Is.EqualTo(2));  // wall
            Assert.That(RoundedBoxMesh.RecommendedSegments(new Vector3(0.3f, 0.3f, 0.3f), 0.075f), Is.EqualTo(1)); // toy
            Assert.That(RoundedBoxMesh.RecommendedSegments(new Vector3(0.4f, 0.4f, 0.4f), 0.1f), Is.EqualTo(1));   // bigger toy
            Assert.That(RoundedBoxMesh.RecommendedSegments(new Vector3(2.9f, 0.012f, 0.15f), 0.003f), Is.EqualTo(1));
        }

        [TestCase(0f)]
        [TestCase(0.5f)]     // half the smallest side
        [TestCase(-0.1f)]
        public void InvalidRadiusThrows(float radius) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => RoundedBoxMesh.Create(Vector3.one, radius, 2));

        [Test]
        public void NoSegmentsOrNonPositiveSizeThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RoundedBoxMesh.Create(Vector3.one, 0.1f, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoundedBoxMesh.Create(new Vector3(1f, 0f, 1f), 0.1f, 2));
        }
    }
}
