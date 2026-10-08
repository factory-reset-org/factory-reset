using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Builds a flat toy gear: a toothed disc with a hole in the middle, extruded along its
    /// local Z axis (the axle), for the turning wall gears in the level dressing.
    /// </summary>
    /// <remarks>
    /// Each tooth is four points of the outline (root, tip, tip, root), like the prototype's
    /// gears. The outline and the hole share the same angles, so each face is a ring of quads.
    /// Faces and side walls have their own vertices, so every facet stays flat and catches a
    /// clean highlight. 32 triangles per tooth: 8 front, 8 back, 8 outer wall, 8 hole wall.
    /// Centred on the origin; no lightmap UVs, because a turning gear is lit by light probes.
    /// </remarks>
    public static class GearMesh
    {
        /// <summary>Triangles for a gear with <paramref name="teeth"/> teeth.</summary>
        public static int TriangleCount(int teeth) => teeth * 32;

        /// <summary>Creates the gear mesh.</summary>
        /// <param name="tipRadius">Radius at the tooth tips (metres).</param>
        /// <param name="rootRadius">Radius between the teeth.</param>
        /// <param name="holeRadius">Radius of the hole for the axle.</param>
        /// <param name="teeth">Number of teeth, at least 3.</param>
        /// <param name="thickness">Depth along the axle (local Z).</param>
        /// <exception cref="ArgumentOutOfRangeException">The radii are not
        /// 0 &lt; hole &lt; root &lt; tip, the thickness is not positive, or there are fewer than 3 teeth.</exception>
        public static Mesh Create(float tipRadius, float rootRadius, float holeRadius, int teeth, float thickness)
        {
            if (!(holeRadius > 0f) || !(rootRadius > holeRadius) || !(tipRadius > rootRadius))
                throw new ArgumentOutOfRangeException(nameof(rootRadius), "The radii must satisfy 0 < hole < root < tip.");
            if (teeth < 3)
                throw new ArgumentOutOfRangeException(nameof(teeth), teeth, "A gear needs at least 3 teeth.");
            if (!(thickness > 0f))
                throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "The thickness must be positive.");

            // Outline: per tooth, root -> rising flank -> tip -> falling flank, as fractions of the tooth step.
            float[] fractions = { 0f, 0.18f, 0.5f, 0.68f };
            int count = teeth * fractions.Length;
            var outline = new Vector2[count];
            var hole = new Vector2[count];
            float step = Mathf.PI * 2f / teeth;
            for (int t = 0; t < teeth; t++)
            for (int f = 0; f < fractions.Length; f++)
            {
                float angle = (t + fractions[f]) * step;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                bool tip = f == 1 || f == 2;
                outline[t * fractions.Length + f] = dir * (tip ? tipRadius : rootRadius);
                hole[t * fractions.Length + f] = dir * holeRadius;
            }

            float front = thickness * 0.5f, back = -front;
            var vertices = new List<Vector3>(count * 16);
            var normals = new List<Vector3>(count * 16);
            var uvs = new List<Vector2>(count * 16);
            var triangles = new List<int>(count * 24);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                for (int i = 0; i < 4; i++)
                    normals.Add(normal);
                uvs.Add(new Vector2(a.x, a.y)); uvs.Add(new Vector2(b.x, b.y));
                uvs.Add(new Vector2(c.x, c.y)); uvs.Add(new Vector2(d.x, d.y));
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            }

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector2 o0 = outline[i], o1 = outline[j], h0 = hole[i], h1 = hole[j];
                // Front (+Z) and back (-Z) rings. Unity is left-handed with clockwise front faces.
                Quad(new Vector3(h0.x, h0.y, front), new Vector3(o0.x, o0.y, front),
                     new Vector3(o1.x, o1.y, front), new Vector3(h1.x, h1.y, front));
                Quad(new Vector3(h1.x, h1.y, back), new Vector3(o1.x, o1.y, back),
                     new Vector3(o0.x, o0.y, back), new Vector3(h0.x, h0.y, back));
                // Outer wall, facing away from the axle.
                Quad(new Vector3(o0.x, o0.y, front), new Vector3(o0.x, o0.y, back),
                     new Vector3(o1.x, o1.y, back), new Vector3(o1.x, o1.y, front));
                // Hole wall, facing the axle.
                Quad(new Vector3(h1.x, h1.y, front), new Vector3(h1.x, h1.y, back),
                     new Vector3(h0.x, h0.y, back), new Vector3(h0.x, h0.y, front));
            }

            var mesh = new Mesh { name = "Gear_" + teeth };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
