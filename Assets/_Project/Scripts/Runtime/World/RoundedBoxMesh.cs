using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Builds a box with truly rounded edges and corners and smooth shading, for the moulded
    /// toy-plastic look. It replaces the single 45 degree chamfer of <see cref="BevelledBoxMesh"/>,
    /// which reads as a hard edge from more than a couple of metres away.
    /// </summary>
    /// <remarks>
    /// <para>Each face is a grid whose rows and columns are dense near the edges and one wide
    /// strip across the flat middle. Every grid point is pushed out from the box's core (the box
    /// shrunk by the radius) to the radius: the classic rounded-cube construction. That push
    /// direction is also the normal, so the rounding shades smoothly.</para>
    /// <para>Each face covers half of an edge's quarter circle, so a quarter circle gets
    /// 2 x <c>segments</c> steps, spaced so each step turns the same angle. Faces share positions
    /// and normals along their seams, so the seams do not show.</para>
    /// <para>UVs are box-projected in metres per face, like <see cref="BevelledBoxMesh"/>, so
    /// tiled textures keep their density. Centred on the origin.</para>
    /// </remarks>
    public static class RoundedBoxMesh
    {
        /// <summary>Largest radius the level uses (obstacles 1 m and thicker).</summary>
        public const float MaxRadius = 0.25f;

        /// <summary>Radius cap for boxes 5 m or longer (walls). Walls butt into other walls, and a
        /// big radius would cut a deep groove at every room corner.</summary>
        public const float WallRadius = 0.08f;

        /// <summary>Triangles for a box with <paramref name="segments"/> per half quarter circle.</summary>
        public static int TriangleCount(int segments) => 6 * (2 * segments + 1) * (2 * segments + 1) * 2;

        /// <summary>
        /// The level's rounding rule: a quarter of the smallest side, at most
        /// <see cref="MaxRadius"/>, at most <see cref="WallRadius"/> for walls, and always under
        /// half the smallest side. A 1 m crate gets 0.25 m, a 0.5 m wall 0.08 m, a 0.3 m toy 0.075 m.
        /// </summary>
        public static float RecommendedRadius(Vector3 size)
        {
            float smallest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float radius = Mathf.Min(smallest * 0.25f, MaxRadius);
            if (largest >= 5f)
                radius = Mathf.Min(radius, WallRadius);
            return Mathf.Min(radius, smallest * 0.45f);
        }

        /// <summary>
        /// Segments for a box: 1 for pieces under 1 m (toys, beacons), which are seen small and
        /// come in the hundreds; otherwise 3 for big curves (0.1 m and up), 2 for medium ones
        /// and 1 for thin trims.
        /// </summary>
        public static int RecommendedSegments(Vector3 size, float radius)
        {
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (largest < 1f)
                return 1;
            if (radius >= 0.1f)
                return 3;
            return radius >= 0.03f ? 2 : 1;
        }

        /// <summary>Creates the mesh.</summary>
        /// <param name="size">Outer size of the box (metres).</param>
        /// <param name="radius">Edge radius; positive and under half the smallest side.</param>
        /// <param name="segments">Steps per half quarter circle, at least 1.</param>
        /// <exception cref="ArgumentOutOfRangeException">A side is not positive, the radius is out of
        /// range, or there are no segments.</exception>
        public static Mesh Create(Vector3 size, float radius, int segments)
        {
            if (!(size.x > 0f) || !(size.y > 0f) || !(size.z > 0f))
                throw new ArgumentOutOfRangeException(nameof(size), size, "Every side must be positive.");
            float smallestHalf = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f;
            if (!(radius > 0f) || radius >= smallestHalf)
                throw new ArgumentOutOfRangeException(nameof(radius), radius,
                    $"The radius must be positive and smaller than half the smallest side ({smallestHalf}).");
            if (segments < 1)
                throw new ArgumentOutOfRangeException(nameof(segments), segments, "At least one segment.");

            Vector3 half = size * 0.5f;
            Vector3 core = half - new Vector3(radius, radius, radius);

            int lines = 2 * segments + 2;     // grid lines per face axis
            int quads = lines - 1;
            int vertexCount = 6 * lines * lines;
            var vertices = new List<Vector3>(vertexCount);
            var normals = new List<Vector3>(vertexCount);
            var uvs = new List<Vector2>(vertexCount);
            var triangles = new List<int>(TriangleCount(segments) * 3);

            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                int first = vertices.Count;
                for (int j = 0; j < lines; j++)
                for (int i = 0; i < lines; i++)
                {
                    var onFace = new Vector3();
                    onFace[axis] = sign * half[axis];
                    onFace[u] = GridCoordinate(i, segments, half[u], radius);
                    onFace[v] = GridCoordinate(j, segments, half[v], radius);

                    var inner = new Vector3(
                        Mathf.Clamp(onFace.x, -core.x, core.x),
                        Mathf.Clamp(onFace.y, -core.y, core.y),
                        Mathf.Clamp(onFace.z, -core.z, core.z));
                    Vector3 normal = (onFace - inner).normalized;
                    Vector3 position = inner + normal * radius;

                    vertices.Add(position);
                    normals.Add(normal);
                    uvs.Add(axis == 0 ? new Vector2(position.z, position.y)
                          : axis == 1 ? new Vector2(position.x, position.z)
                          : new Vector2(position.x, position.y));
                }

                var faceNormal = new Vector3();
                faceNormal[axis] = sign;
                for (int j = 0; j < quads; j++)
                for (int i = 0; i < quads; i++)
                {
                    int a = first + j * lines + i, b = a + 1, c = a + lines + 1, d = a + lines;
                    // Unity's front faces are clockwise; Cross(p1 - p0, p2 - p0) points out of a
                    // clockwise triangle, so flip the quad when it would face into the box.
                    Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    if (Vector3.Dot(cross, faceNormal) < 0f)
                    {
                        int swap = b;
                        b = d;
                        d = swap;
                    }
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(a); triangles.Add(c); triangles.Add(d);
                }
            }

            var mesh = new Mesh { name = $"RoundedBox {size.x}x{size.y}x{size.z} r{radius}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // Grid line k of 2s+2 along one face axis: s+1 lines through the near rounding zone
        // (from the edge at -half to the core at -half + radius), then s+1 through the far one
        // (core to edge). A line at the edge turns 45 degrees from the face, one at the core 0;
        // the tan spacing makes each step turn the same angle.
        static float GridCoordinate(int k, int segments, float half, float radius)
        {
            const float QuarterTurn = Mathf.PI * 0.25f;
            if (k <= segments)
                return -half + radius - radius * Mathf.Tan(QuarterTurn * (segments - k) / segments);
            int step = k - segments - 1;
            return half - radius + radius * Mathf.Tan(QuarterTurn * step / segments);
        }
    }
}
