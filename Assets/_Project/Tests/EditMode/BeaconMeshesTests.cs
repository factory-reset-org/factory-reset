using System;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.Journey.Chapters;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests.EditMode
{
    public class BeaconMeshesTests
    {
        const float Tolerance = 1e-4f;

        Mesh _mesh;

        [TearDown]
        public void TearDown()
        {
            if (_mesh != null)
                Object.DestroyImmediate(_mesh);
        }

        // Every triangle's winding (Unity: clockwise is the front) agrees with its vertices' normals.
        static void AssertFrontFacesMatchNormals(Mesh mesh)
        {
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                Assert.Greater(Vector3.Dot(face, normals[a] + normals[b] + normals[c]), 0f, $"Triangle {i / 3} faces the wrong way.");
            }
        }

        [Test]
        public void TheBeamIsAnOpenCylinderStandingOnItsPivot()
        {
            _mesh = BeaconMeshes.CreateBeam(0.5f, 5f, 16);

            Assert.AreEqual(17 * 2, _mesh.vertexCount, "One column of two vertices per segment edge, seam included.");
            Assert.AreEqual(16 * 2, _mesh.triangles.Length / 3, "Two triangles per segment and no caps.");
            Assert.AreEqual(0f, _mesh.bounds.min.y, Tolerance);
            Assert.AreEqual(5f, _mesh.bounds.max.y, Tolerance);
            Assert.AreEqual(0.5f, _mesh.bounds.extents.x, Tolerance);
            foreach (Vector3 normal in _mesh.normals)
                Assert.AreEqual(0f, normal.y, Tolerance, "Side normals are horizontal.");
            AssertFrontFacesMatchNormals(_mesh);
        }

        [Test]
        public void TheBeamsUvRunsBottomToTop()
        {
            _mesh = BeaconMeshes.CreateBeam(0.5f, 5f, 8);
            Vector3[] positions = _mesh.vertices;
            Vector2[] uvs = _mesh.uv;

            for (int i = 0; i < positions.Length; i++)
                Assert.AreEqual(positions[i].y / 5f, uvs[i].y, Tolerance);
        }

        [Test]
        public void TheRingLiesFlatAndFacesUp()
        {
            _mesh = BeaconMeshes.CreateRing(0.75f, 0.95f, 28);

            Assert.AreEqual(29 * 2, _mesh.vertexCount);
            Assert.AreEqual(28 * 2, _mesh.triangles.Length / 3);
            Assert.AreEqual(0f, _mesh.bounds.size.y, Tolerance);
            Assert.AreEqual(0.95f, _mesh.bounds.extents.x, Tolerance);
            foreach (Vector3 position in _mesh.vertices)
            {
                float radius = new Vector2(position.x, position.z).magnitude;
                Assert.That(radius, Is.EqualTo(0.75f).Within(Tolerance).Or.EqualTo(0.95f).Within(Tolerance));
            }
            AssertFrontFacesMatchNormals(_mesh);
            Assert.AreEqual(Vector3.up, _mesh.normals[0]);
        }

        [Test]
        public void TheArrowPointsDownFromItsBase()
        {
            _mesh = BeaconMeshes.CreateArrow(0.28f, 0.55f);

            Assert.AreEqual(4 * 3 + 4, _mesh.vertexCount, "Four flat sides and a square base.");
            Assert.AreEqual(6, _mesh.triangles.Length / 3);
            Assert.AreEqual(0f, _mesh.bounds.min.y, Tolerance, "The tip is the pivot.");
            Assert.AreEqual(0.55f, _mesh.bounds.max.y, Tolerance);
            AssertFrontFacesMatchNormals(_mesh);

            Vector3[] normals = _mesh.normals;
            for (int i = 0; i < 12; i++)
                Assert.Less(normals[i].y, 0f, "The sides face outwards and down.");
        }

        [Test]
        public void BadSizesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BeaconMeshes.CreateBeam(0f, 5f, 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => BeaconMeshes.CreateBeam(0.5f, 5f, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => BeaconMeshes.CreateRing(1f, 0.5f, 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => BeaconMeshes.CreateArrow(0.3f, -1f));
        }
    }
}
