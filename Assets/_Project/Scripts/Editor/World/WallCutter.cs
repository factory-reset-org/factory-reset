using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ToyFactory.Runtime.World;
using static ToyFactory.Editor.World.DressingKit;

namespace ToyFactory.Editor.World
{
    /// <summary>
    /// Cuts window openings through a level wall: the wall's mesh is rebuilt as boxes around the
    /// openings (UV-mapped in metres like the rounded box it replaces, so the wall panels stay in
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

        // The wall as boxes around the holes: split along the wall at every hole edge, then each
        // column into the heights no hole covers. Each box spans the wall's full thickness.
        static Mesh Build(string name, Vector3 size, bool alongZ, List<Rect> holes)
        {
            float length = alongZ ? size.z : size.x, height = size.y, thickness = alongZ ? size.x : size.z;
            var cuts = new List<float> { -length * 0.5f, length * 0.5f };
            foreach (Rect hole in holes)
            {
                cuts.Add(Mathf.Clamp(hole.xMin, -length * 0.5f, length * 0.5f));
                cuts.Add(Mathf.Clamp(hole.xMax, -length * 0.5f, length * 0.5f));
            }
            cuts.Sort();

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int c = 0; c < cuts.Count - 1; c++)
            {
                float a0 = cuts[c], a1 = cuts[c + 1];
                if (a1 - a0 < 0.001f)
                    continue;
                float mid = (a0 + a1) * 0.5f;
                var blocked = new List<Vector2>();
                foreach (Rect hole in holes)
                    if (hole.xMin <= mid && mid <= hole.xMax)
                        blocked.Add(new Vector2(Mathf.Max(hole.yMin, -height * 0.5f), Mathf.Min(hole.yMax, height * 0.5f)));
                blocked.Sort((p, q) => p.x.CompareTo(q.x));
                float y = -height * 0.5f;
                foreach (Vector2 b in blocked)
                {
                    if (b.x > y + 0.001f)
                        AddBox(vertices, normals, uvs, triangles, alongZ, a0, a1, y, b.x, thickness);
                    y = Mathf.Max(y, b.y);
                }
                if (height * 0.5f > y + 0.001f)
                    AddBox(vertices, normals, uvs, triangles, alongZ, a0, a1, y, height * 0.5f, thickness);
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

        // A box from a0 to a1 along the wall, y0 to y1 in height, the full thickness across.
        static void AddBox(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, bool alongZ,
            float a0, float a1, float y0, float y1, float thickness)
        {
            float h = thickness * 0.5f;
            Vector3 min = alongZ ? new Vector3(-h, y0, a0) : new Vector3(a0, y0, -h);
            Vector3 max = alongZ ? new Vector3(h, y1, a1) : new Vector3(a1, y1, h);
            Vector3 centre = (min + max) * 0.5f, half = (max - min) * 0.5f;
            for (int axis = 0; axis < 3; axis++)
                foreach (float sign in new[] { -1f, 1f })
                {
                    var normal = new Vector3();
                    normal[axis] = sign;
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
}
