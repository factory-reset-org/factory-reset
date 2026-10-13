using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ToyFactory.Runtime.World;
using static ToyFactory.Editor.World.DressingKit;

namespace ToyFactory.Editor.World
{
    /// <summary>
    /// Cuts window openings through a level wall: the wall's mesh is rebuilt round the openings
    /// (UV-mapped in metres like the rounded box it replaces, so the wall panels stay in
    /// line), while its box collider stays whole. The openings are kept on the wall in a
    /// <see cref="WallOpenings"/> component, per room builder.
    /// </summary>
    public static class WallCutter
    {
        /// <summary>
        /// Replaces the openings <paramref name="owner"/> cuts through <paramref name="wall"/> with
        /// <paramref name="openings"/> (world metres: along the wall's long axis, then bottom and top)
        /// and rebuilds the wall's mesh from every builder's openings.
        /// </summary>
        public static void Cut(GameObject wall, string owner, IEnumerable<(float alongMin, float alongMax, float bottom, float top)> openings)
        {
            var data = wall.GetComponent<WallOpenings>();
            if (data == null)
                data = wall.AddComponent<WallOpenings>();
            data.Openings.RemoveAll(o => o.Owner == owner);
            foreach (var (alongMin, alongMax, bottom, top) in openings)
                data.Openings.Add(new WallOpenings.Opening { Owner = owner, AlongMin = alongMin, AlongMax = alongMax, Bottom = bottom, Top = top });

            Vector3 size = wall.GetComponent<BoxCollider>().size;
            bool alongZ = size.z > size.x;
            Vector3 centre = wall.transform.position;
            var holes = new List<Rect>();
            foreach (WallOpenings.Opening o in data.Openings)
            {
                float along = alongZ ? centre.z : centre.x;
                holes.Add(Rect.MinMaxRect(o.AlongMin - along, o.Bottom - centre.y, o.AlongMax - along, o.Top - centre.y));
            }
            wall.GetComponent<MeshFilter>().sharedMesh = holes.Count == 0
                ? RoundBox(size)
                : Custom($"Wall_{wall.name}_Cut", () => Build($"Wall_{wall.name}_Cut", size, alongZ, holes));
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(wall);
        }

        // The wall as one grid: split along the wall and up it at every hole edge; the front and
        // back faces are the grid's solid cells sharing their corners (one lightmap chart each, so
        // no seams run up the wall), and sides are added only where a solid cell meets a hole or
        // the wall's edge.
        static Mesh Build(string name, Vector3 size, bool alongZ, List<Rect> holes)
        {
            float length = alongZ ? size.z : size.x, height = size.y, thickness = alongZ ? size.x : size.z;
            List<float> xs = Cuts(-length * 0.5f, length * 0.5f, holes, h => h.xMin, h => h.xMax);
            List<float> ys = Cuts(-height * 0.5f, height * 0.5f, holes, h => h.yMin, h => h.yMax);
            int nx = xs.Count - 1, ny = ys.Count - 1;
            var solid = new bool[nx, ny];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    var mid = new Vector2((xs[i] + xs[i + 1]) * 0.5f, (ys[j] + ys[j + 1]) * 0.5f);
                    solid[i, j] = !holes.Exists(hole => hole.Contains(mid));
                }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float h = thickness * 0.5f;
            foreach (float side in new[] { 1f, -1f })
            {
                var shared = new Dictionary<(int, int), int>();
                Vector3 normal = alongZ ? new Vector3(side, 0f, 0f) : new Vector3(0f, 0f, side);
                Vector3 right = Vector3.Cross(Vector3.up, normal), up = Vector3.Cross(normal, right);
                int Corner(int gi, int gj)
                {
                    if (shared.TryGetValue((gi, gj), out int index))
                        return index;
                    Vector3 p = alongZ ? new Vector3(side * h, ys[gj], xs[gi]) : new Vector3(xs[gi], ys[gj], side * h);
                    shared[(gi, gj)] = vertices.Count;
                    vertices.Add(p);
                    normals.Add(normal);
                    uvs.Add(alongZ ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
                    return vertices.Count - 1;
                }
                // Along the wall runs +right or -right on this face; up is always +y.
                bool alongRight = Vector3.Dot(right, alongZ ? Vector3.forward : Vector3.right) > 0f;
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                    {
                        if (!solid[i, j])
                            continue;
                        int lo = alongRight ? i + 1 : i, hi = alongRight ? i : i + 1;   // +right side, -right side
                        int c0 = Corner(lo, j), c1 = Corner(hi, j), c2 = Corner(hi, j + 1), c3 = Corner(lo, j + 1);
                        triangles.AddRange(new[] { c0, c2, c1, c0, c3, c2 });
                    }
            }

            // Sides: the reveals round the holes, and the wall's own ends, top and bottom.
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    if (!solid[i, j])
                        continue;
                    Vector3 min = alongZ ? new Vector3(-h, ys[j], xs[i]) : new Vector3(xs[i], ys[j], -h);
                    Vector3 max = alongZ ? new Vector3(h, ys[j + 1], xs[i + 1]) : new Vector3(xs[i + 1], ys[j + 1], h);
                    Vector3 alongAxis = alongZ ? Vector3.forward : Vector3.right;
                    if (i == 0 || !solid[i - 1, j]) Face(vertices, normals, uvs, triangles, min, max, -alongAxis);
                    if (i == nx - 1 || !solid[i + 1, j]) Face(vertices, normals, uvs, triangles, min, max, alongAxis);
                    if (j == 0 || !solid[i, j - 1]) Face(vertices, normals, uvs, triangles, min, max, Vector3.down);
                    if (j == ny - 1 || !solid[i, j + 1]) Face(vertices, normals, uvs, triangles, min, max, Vector3.up);
                }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // The wall's ends and every hole edge (clamped to the wall), sorted, without repeats.
        static List<float> Cuts(float from, float to, List<Rect> holes, System.Func<Rect, float> low, System.Func<Rect, float> high)
        {
            var cuts = new List<float> { from, to };
            foreach (Rect hole in holes)
            {
                cuts.Add(Mathf.Clamp(low(hole), from, to));
                cuts.Add(Mathf.Clamp(high(hole), from, to));
            }
            cuts.Sort();
            for (int c = cuts.Count - 1; c > 0; c--)
                if (cuts[c] - cuts[c - 1] < 0.001f)
                    cuts.RemoveAt(c);
            return cuts;
        }

        // One face of the box from min to max, facing 'normal' (an axis direction), UV-mapped in metres.
        static void Face(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 min, Vector3 max, Vector3 normal)
        {
            Vector3 centre = (min + max) * 0.5f, half = (max - min) * 0.5f;
            int axis = normal.x != 0f ? 0 : normal.y != 0f ? 1 : 2;
            Vector3 helper = axis == 1 ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(helper, normal), up = Vector3.Cross(normal, right);
            Vector3 face = centre + Vector3.Scale(normal, half);
            float w = Mathf.Abs(Vector3.Dot(right, half)), hh = Mathf.Abs(Vector3.Dot(up, half));
            int b = v.Count;
            Vector3[] corners = { face + right * w - up * hh, face - right * w - up * hh, face - right * w + up * hh, face + right * w + up * hh };
            foreach (Vector3 p in corners)
            {
                v.Add(p);
                n.Add(normal);
                uv.Add(axis == 0 ? new Vector2(p.z, p.y) : axis == 1 ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y));
            }
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
    }
}
