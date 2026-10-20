using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ToyFactory.Runtime.Visuals
{
    /// <summary>
    /// Turns a triangle mesh into a mesh of line segments, one per edge, so a model can be drawn as a
    /// wireframe in any render pipeline (the Scene view's wireframe is not available in the Game view).
    /// </summary>
    /// <remarks>
    /// Vertices are matched by position, not by index, because a mesh splits vertices wherever normals,
    /// UVs or material slots change; without that every face would draw its own outline. By default an
    /// edge between two triangles lying in one plane is left out, so the two triangles of a flat quad
    /// show as one quad, the way the modelling tool's wireframe does.
    /// </remarks>
    public static class WireMesh
    {
        // Two triangles whose normals agree to about 1.8 degrees count as one flat face.
        const float CoplanarDot = 0.9995f;

        // Positions closer than this are one vertex (1/10 mm).
        const float Weld = 1e-4f;

        /// <summary>Builds the line mesh of <paramref name="source"/>. Null when there is nothing to draw.</summary>
        /// <param name="source">A triangle mesh. Every submesh is used.</param>
        /// <param name="hideCoplanarEdges">Leave out the edge between two triangles in one plane.</param>
        public static Mesh Build(Mesh source, bool hideCoplanarEdges = true)
        {
            if (source == null || source.vertexCount == 0)
                return null;

            Vector3[] vertices = source.vertices;
            var welded = new Dictionary<Vector3Int, int>();
            var remap = new int[vertices.Length];
            var positions = new List<Vector3>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(vertices[i].x / Weld), Mathf.RoundToInt(vertices[i].y / Weld), Mathf.RoundToInt(vertices[i].z / Weld));
                if (!welded.TryGetValue(key, out int id))
                {
                    id = positions.Count;
                    welded.Add(key, id);
                    positions.Add(vertices[i]);
                }

                remap[i] = id;
            }

            // Each edge keeps the normal of the first triangle that uses it and how many triangles use it.
            var edges = new Dictionary<long, (Vector3 normal, int count, bool crease)>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                if (source.GetTopology(sub) != MeshTopology.Triangles)
                    continue;

                int[] indices = source.GetIndices(sub);
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    int a = remap[indices[t]], b = remap[indices[t + 1]], c = remap[indices[t + 2]];
                    if (a == b || b == c || a == c)
                        continue;

                    Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).normalized;
                    AddEdge(edges, a, b, normal);
                    AddEdge(edges, b, c, normal);
                    AddEdge(edges, c, a, normal);
                }
            }

            var lines = new List<int>(edges.Count * 2);
            foreach (KeyValuePair<long, (Vector3 normal, int count, bool crease)> edge in edges)
            {
                bool flat = edge.Value.count == 2 && !edge.Value.crease;
                if (flat && hideCoplanarEdges)
                    continue;

                lines.Add((int)(edge.Key >> 32));
                lines.Add((int)(edge.Key & 0xFFFFFFFF));
            }

            if (lines.Count == 0)
                return null;

            var mesh = new Mesh { name = source.name + "_Wire", indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(positions);
            mesh.SetIndices(lines, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>How many line segments a mesh built by <see cref="Build"/> holds.</summary>
        public static int EdgeCount(Mesh wire) => wire == null ? 0 : (int)(wire.GetIndexCount(0) / 2);

        static void AddEdge(Dictionary<long, (Vector3 normal, int count, bool crease)> edges, int from, int to, Vector3 normal)
        {
            long key = from < to ? ((long)from << 32) | (uint)to : ((long)to << 32) | (uint)from;
            if (!edges.TryGetValue(key, out var entry))
            {
                edges.Add(key, (normal, 1, false));
                return;
            }

            bool crease = entry.crease || Vector3.Dot(entry.normal, normal) < CoplanarDot;
            edges[key] = (entry.normal, entry.count + 1, crease);
        }
    }
}
