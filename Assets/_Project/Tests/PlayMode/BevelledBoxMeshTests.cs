using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    public sealed class BevelledBoxMeshTests
    {
        static readonly Vector3 WallSize = new Vector3(41.5f, 5f, 0.5f);
        const float WallBevel = 0.06f;

        readonly List<Mesh> _meshes = new List<Mesh>();

        [TearDown]
        public void TearDown()
        {
            foreach (Mesh mesh in _meshes)
                UnityEngine.Object.DestroyImmediate(mesh);
            _meshes.Clear();
        }

        Mesh Create(Vector3 size, float bevel)
        {
            Mesh mesh = BevelledBoxMesh.Create(size, bevel);
            _meshes.Add(mesh);
            return mesh;
        }

        [Test]
        public void HasTheDocumentedVertexAndTriangleCounts()
        {
            Mesh mesh = Create(WallSize, WallBevel);

            Assert.That(mesh.vertexCount, Is.EqualTo(BevelledBoxMesh.VertexCount));
            Assert.That(mesh.triangles.Length / 3, Is.EqualTo(BevelledBoxMesh.TriangleCount));
        }

        [Test]
        public void BoundsMatchTheRequestedSizeCentredOnTheOrigin()
        {
            Mesh mesh = Create(WallSize, WallBevel);

            Assert.That(Vector3.Distance(mesh.bounds.size, WallSize), Is.LessThan(1e-4f));
            Assert.That(mesh.bounds.center.magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void EveryFacetFacesOutward()
        {
            Mesh mesh = Create(new Vector3(2f, 2.8f, 2f), 0.08f);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 centre = (vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]]) / 3f;
                Assert.That(Vector3.Dot(normals[triangles[t]], centre), Is.GreaterThan(0f),
                    $"Triangle {t / 3} points inward.");
            }
        }

        [Test]
        public void WindingMatchesTheStoredNormals()
        {
            // RecalculateNormals derives normals from the triangle winding, so a facet wound
            // the wrong way (invisible from outside) would disagree with the stored normal.
            Mesh mesh = Create(new Vector3(9f, 3.2f, 1f), 0.08f);
            Vector3[] stored = mesh.normals;
            Mesh check = UnityEngine.Object.Instantiate(mesh);
            _meshes.Add(check);
            check.RecalculateNormals();
            Vector3[] fromWinding = check.normals;

            for (int i = 0; i < stored.Length; i++)
                Assert.That(Vector3.Dot(stored[i], fromWinding[i]), Is.GreaterThan(0.999f), $"Vertex {i}.");
        }

        [Test]
        public void SurfaceIsClosedWithNoHolesOrCracks()
        {
            Mesh mesh = Create(new Vector3(1.5f, 3f, 1.5f), 0.08f);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var edgeUses = new Dictionary<(Vector3Int, Vector3Int), int>();

            for (int t = 0; t < triangles.Length; t += 3)
            for (int k = 0; k < 3; k++)
            {
                Vector3Int a = Key(vertices[triangles[t + k]]);
                Vector3Int b = Key(vertices[triangles[t + (k + 1) % 3]]);
                var edge = IsBefore(a, b) ? (a, b) : (b, a);   // same key for both directions
                edgeUses.TryGetValue(edge, out int uses);
                edgeUses[edge] = uses + 1;
            }

            // Every edge of a closed surface (welded by position) is shared by exactly two triangles.
            foreach (var pair in edgeUses)
                Assert.That(pair.Value, Is.EqualTo(2), $"Edge {pair.Key} is used {pair.Value} times.");
        }

        [TestCase(0f)]
        [TestCase(-0.1f)]
        [TestCase(0.25f)]   // half of the 0.5 m side: the chamfers would meet
        public void InvalidBevelThrows(float bevel)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BevelledBoxMesh.Create(WallSize, bevel));
        }

        [Test]
        public void NonPositiveSizeThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BevelledBoxMesh.Create(new Vector3(1f, 0f, 1f), 0.05f));
        }

        static bool IsBefore(Vector3Int a, Vector3Int b) =>
            a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z <= b.z;

        // Weld positions on a 0.1 mm lattice so shared corners compare equal despite float noise.
        static Vector3Int Key(Vector3 p) =>
            new Vector3Int(Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));
    }
}
