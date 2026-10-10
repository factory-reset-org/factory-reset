using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ToyFactory.Editor.World
{
    /// <summary>
    /// Paints the level's signs, posters and decals into one texture atlas in code: anti-aliased
    /// rounded panels, circles, rings, stripes, polygons and text (Unity's built-in font, drawn by
    /// a temporary camera). Pixel (0, 0) is the bottom-left corner, as in UV space, so
    /// <see cref="Uv"/> maps a painted rectangle straight onto a quad.
    /// </summary>
    public sealed class SignPainter
    {
        const int TextLayer = 31;

        readonly int _size;
        readonly Color[] _pixels;

        public SignPainter(int size, Color background)
        {
            _size = size;
            _pixels = new Color[size * size];
            for (int i = 0; i < _pixels.Length; i++)
                _pixels[i] = background;
        }

        /// <summary>The UV rectangle of a pixel rectangle.</summary>
        public Rect Uv(RectInt r) => new Rect(r.x / (float)_size, r.y / (float)_size, r.width / (float)_size, r.height / (float)_size);

        // Blends colour c over the pixel at (x, y) with coverage a (0..1).
        void Blend(int x, int y, Color c, float a)
        {
            if (x < 0 || y < 0 || x >= _size || y >= _size || a <= 0f)
                return;
            int i = y * _size + x;
            a *= c.a;
            _pixels[i] = new Color(Mathf.Lerp(_pixels[i].r, c.r, a), Mathf.Lerp(_pixels[i].g, c.g, a),
                Mathf.Lerp(_pixels[i].b, c.b, a), 1f);
        }

        // Fills every pixel in the bounds by a signed distance function (negative inside), with a one pixel soft edge.
        void Fill(RectInt bounds, Func<Vector2, float> distance, Color c)
        {
            for (int y = bounds.yMin - 1; y <= bounds.yMax; y++)
                for (int x = bounds.xMin - 1; x <= bounds.xMax; x++)
                    Blend(x, y, c, Mathf.Clamp01(0.5f - distance(new Vector2(x + 0.5f, y + 0.5f))));
        }

        /// <summary>A rounded rectangle.</summary>
        public void RoundRect(RectInt r, float radius, Color c)
        {
            Vector2 centre = r.center, half = new Vector2(r.width, r.height) * 0.5f;
            Fill(r, p =>
            {
                Vector2 q = new Vector2(Mathf.Abs(p.x - centre.x), Mathf.Abs(p.y - centre.y)) - half + Vector2.one * radius;
                return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            }, c);
        }

        /// <summary>A rounded rectangle outline of the given thickness.</summary>
        public void RoundFrame(RectInt r, float radius, float thickness, Color c)
        {
            Vector2 centre = r.center, half = new Vector2(r.width, r.height) * 0.5f;
            Fill(r, p =>
            {
                Vector2 q = new Vector2(Mathf.Abs(p.x - centre.x), Mathf.Abs(p.y - centre.y)) - half + Vector2.one * radius;
                float d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                return Mathf.Max(d, -d - thickness);
            }, c);
        }

        /// <summary>A filled circle.</summary>
        public void Circle(Vector2 centre, float radius, Color c) =>
            Fill(Bounds(centre, radius), p => Vector2.Distance(p, centre) - radius, c);

        /// <summary>A ring between two radii.</summary>
        public void Ring(Vector2 centre, float inner, float outer, Color c) =>
            Fill(Bounds(centre, outer), p =>
            {
                float d = Vector2.Distance(p, centre);
                return Mathf.Max(d - outer, inner - d);
            }, c);

        /// <summary>A line with round ends.</summary>
        public void Line(Vector2 a, Vector2 b, float thickness, Color c)
        {
            var bounds = new RectInt(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - thickness), Mathf.FloorToInt(Mathf.Min(a.y, b.y) - thickness),
                Mathf.CeilToInt(Mathf.Abs(a.x - b.x) + thickness * 2f), Mathf.CeilToInt(Mathf.Abs(a.y - b.y) + thickness * 2f));
            Fill(bounds, p =>
            {
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
                return Vector2.Distance(p, a + ab * t) - thickness * 0.5f;
            }, c);
        }

        /// <summary>A filled convex or concave polygon (even-odd), 4x supersampled.</summary>
        public void Polygon(Vector2[] points, Color c)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector2 p in points)
            {
                minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y);
            }
            for (int y = Mathf.FloorToInt(minY); y <= Mathf.CeilToInt(maxY); y++)
                for (int x = Mathf.FloorToInt(minX); x <= Mathf.CeilToInt(maxX); x++)
                {
                    int inside = 0;
                    for (int s = 0; s < 4; s++)
                        if (Inside(points, new Vector2(x + 0.25f + 0.5f * (s % 2), y + 0.25f + 0.5f * (s / 2))))
                            inside++;
                    Blend(x, y, c, inside / 4f);
                }
        }

        static bool Inside(Vector2[] polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                    p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>45 degree stripes of two colours, <paramref name="width"/> pixels each.</summary>
        public void Stripes(RectInt r, int width, Color a, Color b)
        {
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                    Blend(x, y, ((x + y) / width) % 2 == 0 ? a : b, 1f);
        }

        /// <summary>
        /// Text centred in <paramref name="r"/>, scaled to fit it (keeping its proportions), in the
        /// built-in font. Use \n for more lines.
        /// </summary>
        public void Text(string text, RectInt r, Color c, bool bold = true)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var label = new GameObject("SignText") { hideFlags = HideFlags.HideAndDontSave, layer = TextLayer };
            var cameraObject = new GameObject("SignCamera") { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(r.width, r.height, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false);
            try
            {
                label.transform.position = new Vector3(5000f, 5000f, 5000f);
                var mesh = label.AddComponent<TextMesh>();
                mesh.font = font;
                mesh.text = text;
                mesh.fontSize = 200;
                mesh.characterSize = 0.05f;
                mesh.lineSpacing = 0.9f;
                mesh.anchor = TextAnchor.MiddleCenter;
                mesh.alignment = TextAlignment.Center;
                mesh.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                mesh.color = Color.white;
                var renderer = label.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = font.material;

                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.cullingMask = 1 << TextLayer;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 50f;
                camera.transform.position = label.transform.position + Vector3.back * 10f;
                Bounds bounds = renderer.bounds;
                float aspect = r.width / (float)r.height;
                camera.aspect = aspect;
                camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / aspect) * 1.04f;
                camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, camera.transform.position.z);
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, r.width, r.height), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                camera.targetTexture = null;

                Color[] coverage = read.GetPixels();
                for (int y = 0; y < r.height; y++)
                    for (int x = 0; x < r.width; x++)
                        Blend(r.x + x, r.y + y, c, coverage[y * r.width + x].r);
            }
            finally
            {
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(read);
                UnityEngine.Object.DestroyImmediate(label);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>Writes the atlas as a PNG asset (sRGB, mipmapped, trilinear, anisotropic 8) and returns it.</summary>
        public Texture2D Save(string assetPath)
        {
            var texture = new Texture2D(_size, _size, TextureFormat.RGBA32, false);
            texture.SetPixels(_pixels);
            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        static RectInt Bounds(Vector2 centre, float radius) =>
            new RectInt(Mathf.FloorToInt(centre.x - radius), Mathf.FloorToInt(centre.y - radius),
                Mathf.CeilToInt(radius * 2f) + 1, Mathf.CeilToInt(radius * 2f) + 1);
    }
}
