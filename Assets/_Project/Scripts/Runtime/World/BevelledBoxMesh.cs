using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Builds a box with chamfered (bevelled) edges and corners, for the smooth toy-plastic
    /// environment style. The chamfers catch a highlight along every edge, which is what
    /// makes a box read as moulded plastic instead of a hard-edged block.
    /// </summary>
    /// <remarks>
    /// Geometry: 6 inset faces, 12 edge chamfers and 8 corner triangles = 96 vertices and
    /// 44 triangles, centred on the origin. Every facet has its own vertices, so its normal
    /// is flat and the bevel highlight stays crisp. UVs are box-projected in metres (1 UV
    /// unit = 1 m), so a tiled texture keeps the same density on any size of box.
    /// </remarks>
    public static class BevelledBoxMesh
    {
        public const int VertexCount = 96;
        public const int TriangleCount = 44;

        /// <summary>Creates the mesh for a box of <paramref name="size"/> with chamfers of width <paramref name="bevel"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A size is not positive, or the bevel is not
        /// positive and smaller than half of the smallest side.</exception>
        public static Mesh Create(Vector3 size, float bevel)
        {
            if (!(size.x > 0f) || !(size.y > 0f) || !(size.z > 0f))
                throw new ArgumentOutOfRangeException(nameof(size), size, "Every side must be positive.");
            float smallestHalf = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f;
            if (!(bevel > 0f) || bevel >= smallestHalf)
                throw new ArgumentOutOfRangeException(nameof(bevel), bevel,
                    $"The bevel must be positive and smaller than half the smallest side ({smallestHalf}).");

            Vector3 h = size * 0.5f;                                // outer half extents
            Vector3 e = h - new Vector3(bevel, bevel, bevel);       // inner half extents
            var builder = new Builder();

            // Faces: on each axis side, the flat face inset by the bevel.
            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                builder.AddQuad(
                    Point(axis, sign * h[axis], u, -e[u], v, -e[v]),
                    Point(axis, sign * h[axis], u, e[u], v, -e[v]),
                    Point(axis, sign * h[axis], u, e[u], v, e[v]),
                    Point(axis, sign * h[axis], u, -e[u], v, e[v]));
            }

            // Edges: a 45 degree strip between each pair of neighbouring faces.
            for (int a = 0; a < 3; a++)
            for (int b = a + 1; b < 3; b++)
            for (int sa = -1; sa <= 1; sa += 2)
            for (int sb = -1; sb <= 1; sb += 2)
            {
                int c = 3 - a - b;
                builder.AddQuad(
                    Point(a, sa * h[a], b, sb * e[b], c, -e[c]),
                    Point(a, sa * h[a], b, sb * e[b], c, e[c]),
                    Point(a, sa * e[a], b, sb * h[b], c, e[c]),
                    Point(a, sa * e[a], b, sb * h[b], c, -e[c]));
            }

            // Corners: one triangle where three edge strips meet.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                builder.AddTriangle(
                    new Vector3(sx * h.x, sy * e.y, sz * e.z),
                    new Vector3(sx * e.x, sy * h.y, sz * e.z),
                    new Vector3(sx * e.x, sy * e.y, sz * h.z));
            }

            return builder.ToMesh($"BevelledBox {size.x}x{size.y}x{size.z} b{bevel}");
        }

        // A point given as three (axis, value) pairs, so faces and edges can be written once for every axis.
        static Vector3 Point(int axisA, float a, int axisB, float b, int axisC, float c)
        {
            var p = new Vector3();
            p[axisA] = a;
            p[axisB] = b;
            p[axisC] = c;
            return p;
        }

        sealed class Builder
        {
            readonly List<Vector3> _vertices = new List<Vector3>(VertexCount);
            readonly List<Vector3> _normals = new List<Vector3>(VertexCount);
            readonly List<Vector2> _uvs = new List<Vector2>(VertexCount);
            readonly List<int> _triangles = new List<int>(TriangleCount * 3);

            public void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) =>
                AddFacet(new[] { p0, p1, p2, p3 });

            public void AddTriangle(Vector3 p0, Vector3 p1, Vector3 p2) =>
                AddFacet(new[] { p0, p1, p2 });

            void AddFacet(Vector3[] points)
            {
                Vector3 centre = Vector3.zero;
                foreach (Vector3 p in points) centre += p;
                centre /= points.Length;

                // The box is convex and centred on the origin, so the outward normal points
                // away from the origin. Reverse the winding if the points were listed inward.
                Vector3 normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
                if (Vector3.Dot(normal, centre) < 0f)
                {
                    Array.Reverse(points);
                    normal = -normal;
                }

                int first = _vertices.Count;
                foreach (Vector3 p in points)
                {
                    _vertices.Add(p);
                    _normals.Add(normal);
                    _uvs.Add(BoxProject(p, normal));
                }

                // Unity treats clockwise triangles (seen from the front) as front-facing, and
                // Cross(p1 - p0, p2 - p0) points out of a clockwise triangle in a left-handed
                // space, so the fan below faces outward.
                for (int i = 1; i < points.Length - 1; i++)
                {
                    _triangles.Add(first);
                    _triangles.Add(first + i);
                    _triangles.Add(first + i + 1);
                }
            }

            // Projects onto the plane of the normal's largest axis, in metres.
            static Vector2 BoxProject(Vector3 p, Vector3 normal)
            {
                Vector3 n = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
                if (n.x >= n.y && n.x >= n.z) return new Vector2(p.z, p.y);
                if (n.y >= n.z) return new Vector2(p.x, p.z);
                return new Vector2(p.x, p.y);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
