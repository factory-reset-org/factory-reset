using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Journey.Chapters
{
    /// <summary>
    /// Builds the three parts of the objective beacon: an open beam, a flat ground ring and a
    /// four-sided arrow pointing down. All three have their pivot on the floor or at the tip,
    /// and UV.y running 0 to 1 along the part (bottom to top, inner to outer edge, tip to
    /// base), which the beacon shader uses for its fades.
    /// </summary>
    public static class BeaconMeshes
    {
        /// <summary>An open cylinder from y = 0 to <paramref name="height"/>, no caps.</summary>
        public static Mesh CreateBeam(float radius, float height, int segments)
        {
            Check(radius > 0f && height > 0f, "The radius and height must be positive.");
            Check(segments >= 3, "A beam needs at least 3 segments.");

            var builder = new Builder();
            for (int i = 0; i <= segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float u = (float)i / segments;
                builder.Vertex(direction * radius, direction, new Vector2(u, 0f));
                builder.Vertex(direction * radius + Vector3.up * height, direction, new Vector2(u, 1f));
            }
            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                builder.Quad(b, b + 1, b + 3, b + 2);
            }
            return builder.Build("BeaconBeam");
        }

        /// <summary>A flat ring on the XZ plane facing up, between two radii.</summary>
        public static Mesh CreateRing(float innerRadius, float outerRadius, int segments)
        {
            Check(innerRadius >= 0f && outerRadius > innerRadius, "The outer radius must be larger than the inner one.");
            Check(segments >= 3, "A ring needs at least 3 segments.");

            var builder = new Builder();
            for (int i = 0; i <= segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float u = (float)i / segments;
                builder.Vertex(direction * innerRadius, Vector3.up, new Vector2(u, 0f));
                builder.Vertex(direction * outerRadius, Vector3.up, new Vector2(u, 1f));
            }
            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                builder.Quad(b, b + 1, b + 3, b + 2);
            }
            return builder.Build("BeaconRing");
        }

        /// <summary>
        /// A four-sided pyramid with its tip at the origin pointing down and its square base at
        /// <paramref name="height"/>, corners <paramref name="radius"/> from the axis.
        /// </summary>
        public static Mesh CreateArrow(float radius, float height)
        {
            Check(radius > 0f && height > 0f, "The radius and height must be positive.");

            var builder = new Builder();
            var corners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float angle = 0.5f * Mathf.PI * i;
                corners[i] = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
            }

            // Sides: flat-shaded, so each face keeps its own vertices and normal.
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % 4];
                Vector3 outward = Vector3.Cross(b - a, Vector3.zero - a).normalized;
                Vector3 edgeMiddle = (a + b) * 0.5f;
                if (Vector3.Dot(outward, new Vector3(edgeMiddle.x, 0f, edgeMiddle.z)) < 0f)
                    outward = -outward;   // face away from the axis
                int first = builder.Vertex(Vector3.zero, outward, new Vector2(0.5f, 0f));
                builder.Vertex(a, outward, new Vector2(0f, 1f));
                builder.Vertex(b, outward, new Vector2(1f, 1f));
                builder.Triangle(first, first + 1, first + 2);
            }

            int cap = builder.Vertex(corners[0], Vector3.up, new Vector2(0f, 1f));
            for (int i = 1; i < 4; i++)
                builder.Vertex(corners[i], Vector3.up, new Vector2(i / 3f, 1f));
            builder.Quad(cap, cap + 1, cap + 2, cap + 3);
            return builder.Build("BeaconArrow");
        }

        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new ArgumentOutOfRangeException(message);
        }

        // Collects vertices and triangles; each triangle is wound so its front face looks
        // along its vertices' normal (Unity treats clockwise as the front).
        sealed class Builder
        {
            readonly List<Vector3> _positions = new List<Vector3>();
            readonly List<Vector3> _normals = new List<Vector3>();
            readonly List<Vector2> _uvs = new List<Vector2>();
            readonly List<int> _triangles = new List<int>();

            public int Vertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                _positions.Add(position);
                _normals.Add(normal);
                _uvs.Add(uv);
                return _positions.Count - 1;
            }

            public void Triangle(int a, int b, int c)
            {
                Vector3 face = Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
                Vector3 normal = _normals[a] + _normals[b] + _normals[c];
                if (Vector3.Dot(face, normal) < 0f)
                    (b, c) = (c, b);
                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
            }

            public void Quad(int a, int b, int c, int d)
            {
                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
