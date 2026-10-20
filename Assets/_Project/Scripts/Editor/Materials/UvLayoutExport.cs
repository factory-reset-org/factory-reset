using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ToyFactory.Editor
{
    /// <summary>
    /// Writes the UV layout of every S3 model prefab as a 1,024 x 1,024 picture, from
    /// <b>Factory Reset / Models / Export UV Layouts</b>, and a summary of how much of the 0 to 1 square each
    /// layout covers. It reads the meshes Unity imported, so the picture is what the game uses, whichever
    /// program the model came from. Each mesh of a model gets its own colour; a triangle drawn over another shows
    /// as a darker cell in the coverage count, not in the picture.
    /// </summary>
    public static class UvLayoutExport
    {
        /// <summary>Where the pictures and <c>UV_SUMMARY.md</c> go, relative to the project folder.</summary>
        public const string OutputFolder = "Docs/Evidence/Models/UV";

        const int Size = 1024;
        const string CharacterFolder = "Assets/_Project/Prefabs/Characters";
        const string PropFolder = "Assets/_Project/Prefabs/Props";

        static readonly string[] Characters = { "TrackerToy", "SaboteurBot", "GuardBot", "CaptainBot", "Unit047" };

        static readonly Color32[] Palette =
        {
            new Color32(255, 201, 51, 255), new Color32(61, 219, 176, 255), new Color32(255, 107, 107, 255),
            new Color32(120, 170, 255, 255), new Color32(255, 159, 28, 255), new Color32(200, 140, 255, 255),
            new Color32(255, 255, 255, 255), new Color32(255, 140, 200, 255)
        };

        /// <summary>One model's layout figures.</summary>
        public readonly struct Layout
        {
            /// <summary>Prefab name without the extension.</summary>
            public string Name { get; }

            /// <summary>Distinct meshes drawn.</summary>
            public int Meshes { get; }

            /// <summary>Triangles drawn.</summary>
            public int Triangles { get; }

            /// <summary>Share of the 0 to 1 square covered by at least one triangle, as a percentage.</summary>
            public float Coverage { get; }

            /// <summary>Share of the 0 to 1 square covered by two or more triangles, as a percentage.</summary>
            public float Overlap { get; }

            /// <summary>UV corners that lie outside the 0 to 1 square.</summary>
            public int OutsideCorners { get; }

            /// <summary>Creates the figures.</summary>
            public Layout(string name, int meshes, int triangles, float coverage, float overlap, int outsideCorners)
            {
                Name = name;
                Meshes = meshes;
                Triangles = triangles;
                Coverage = coverage;
                Overlap = overlap;
                OutsideCorners = outsideCorners;
            }
        }

        /// <summary>The menu entry: writes into <see cref="OutputFolder"/> next to the project.</summary>
        [MenuItem("Factory Reset/Models/Export UV Layouts")]
        public static void Export() => ExportTo(Path.Combine(Path.GetDirectoryName(Application.dataPath), OutputFolder));

        /// <summary>Batch entry point for <c>-executeMethod</c>.</summary>
        public static void ExportBatch() => Export();

        /// <summary>The prefabs whose layouts are exported: the five characters and every prop root.</summary>
        public static string[] PrefabPaths() =>
            Characters.Select(c => CharacterFolder + "/" + c + ".prefab")
                .Concat(Directory.GetFiles(PropFolder, "*_Root.prefab").Select(f => f.Replace('\\', '/')).OrderBy(f => f))
                .ToArray();

        /// <summary>Writes one picture per prefab and the summary into <paramref name="folder"/>.</summary>
        public static List<Layout> ExportTo(string folder)
        {
            Directory.CreateDirectory(folder);
            var layouts = new List<Layout>();
            foreach (string path in PrefabPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                string name = Path.GetFileNameWithoutExtension(path);
                Layout layout = Draw(prefab, name, out Color32[] pixels);
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                File.WriteAllBytes(Path.Combine(folder, name + "_uv.png"), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                layouts.Add(layout);
            }

            var text = new StringBuilder();
            text.AppendLine("# UV layouts of the S3 models");
            text.AppendLine();
            text.AppendLine("Written by Factory Reset > Models > Export UV Layouts (`UvLayoutExport`) from the meshes Unity imported. One picture per model prefab, 1,024 x 1,024, the 0 to 1 square outlined in grey, each mesh in its own colour.");
            text.AppendLine("Coverage is the share of the square under at least one triangle and overlap the share under two or more, both counted on a 1,024 x 1,024 grid.");
            text.AppendLine();
            text.AppendLine("| Model | Meshes | Triangles | Coverage % | Overlap % | UV corners outside 0 to 1 | Picture |");
            text.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (Layout l in layouts)
                text.AppendLine($"| {l.Name} | {l.Meshes} | {l.Triangles} | {l.Coverage:0.0} | {l.Overlap:0.0} | {l.OutsideCorners} | [{l.Name}_uv.png]({l.Name}_uv.png) |");
            File.WriteAllText(Path.Combine(folder, "UV_SUMMARY.md"), text.ToString().Replace("\r\n", "\n"));
            Debug.Log($"UV layouts written: {layouts.Count} models to {folder}");
            return layouts;
        }

        static Layout Draw(GameObject prefab, string name, out Color32[] pixels)
        {
            pixels = new Color32[Size * Size];
            var background = new Color32(24, 20, 36, 255);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = background;

            // Distinct meshes only: a model that repeats one mesh (four wheels) has one layout.
            Mesh[] meshes = prefab.GetComponentsInChildren<MeshFilter>(true)
                .Select(f => f.sharedMesh).Where(m => m != null).Distinct().ToArray();

            var counts = new byte[Size * Size];
            int triangles = 0;
            int outside = 0;
            var uv = new List<Vector2>();
            for (int m = 0; m < meshes.Length; m++)
            {
                Mesh mesh = meshes[m];
                uv.Clear();
                mesh.GetUVs(0, uv);
                if (uv.Count == 0)
                    continue;

                Color32 colour = Palette[m % Palette.Length];
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    int[] indices = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        Vector2 a = uv[indices[i]], b = uv[indices[i + 1]], c = uv[indices[i + 2]];
                        triangles++;
                        outside += Outside(a) + Outside(b) + Outside(c);
                        Fill(counts, a, b, c);
                        Line(pixels, a, b, colour);
                        Line(pixels, b, c, colour);
                        Line(pixels, c, a, colour);
                    }
                }
            }

            // The 0 to 1 square, drawn last so it stays visible.
            var frame = new Color32(120, 120, 140, 255);
            Line(pixels, new Vector2(0f, 0f), new Vector2(1f, 0f), frame);
            Line(pixels, new Vector2(1f, 0f), new Vector2(1f, 1f), frame);
            Line(pixels, new Vector2(1f, 1f), new Vector2(0f, 1f), frame);
            Line(pixels, new Vector2(0f, 1f), new Vector2(0f, 0f), frame);

            int covered = 0, overlapped = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > 0)
                    covered++;
                if (counts[i] > 1)
                    overlapped++;
            }

            float total = Size * Size;
            return new Layout(name, meshes.Length, triangles, 100f * covered / total, 100f * overlapped / total, outside);
        }

        static int Outside(Vector2 p) => p.x < -0.001f || p.x > 1.001f || p.y < -0.001f || p.y > 1.001f ? 1 : 0;

        static Vector2 ToPixel(Vector2 uv) => new Vector2(uv.x * (Size - 1), uv.y * (Size - 1));

        static void Line(Color32[] pixels, Vector2 a, Vector2 b, Color32 colour)
        {
            Vector2 p = ToPixel(a), q = ToPixel(b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(q.x - p.x), Mathf.Abs(q.y - p.y))));
            if (steps > 4 * Size)
                return;   // a corner far outside the square; the outside count already reports it
            for (int i = 0; i <= steps; i++)
            {
                Vector2 point = Vector2.Lerp(p, q, i / (float)steps);
                int x = Mathf.RoundToInt(point.x), y = Mathf.RoundToInt(point.y);
                if ((uint)x < Size && (uint)y < Size)
                    pixels[y * Size + x] = colour;
            }
        }

        // Counts, per grid cell whose sample point lies inside the triangle, one more triangle.
        static void Fill(byte[] counts, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 p = ToPixel(a), q = ToPixel(b), r = ToPixel(c);
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p.x, q.x, r.x)));
            int maxX = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(p.x, q.x, r.x)));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p.y, q.y, r.y)));
            int maxY = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(p.y, q.y, r.y)));
            float area = (q.x - p.x) * (r.y - p.y) - (r.x - p.x) * (q.y - p.y);
            if (Mathf.Abs(area) < 1e-6f)
                return;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // The sample is nudged by a hair so a point never sits exactly on an edge two triangles share,
                    // which would count twice and read as an overlap.
                    float px = x + 0.00071f, py = y + 0.00113f;
                    float w0 = ((q.x - px) * (r.y - py) - (r.x - px) * (q.y - py)) / area;
                    float w1 = ((r.x - px) * (p.y - py) - (p.x - px) * (r.y - py)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 >= 0f && w1 >= 0f && w2 >= 0f && counts[y * Size + x] < 255)
                        counts[y * Size + x]++;
                }
            }
        }
    }
}
