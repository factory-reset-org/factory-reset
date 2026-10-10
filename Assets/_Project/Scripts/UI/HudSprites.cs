using System;
using UnityEngine;

namespace ToyFactory.UI
{
    /// <summary>
    /// The three small sprites the HUD needs and has no art for: a circle, a triangle and a soft
    /// vignette. They are drawn into textures once and owned by the presenter, which destroys them.
    /// </summary>
    internal sealed class HudSprites : IDisposable
    {
        const int Size = 64;

        public Sprite Circle { get; }
        public Sprite Triangle { get; }
        public Sprite Vignette { get; }

        readonly Texture2D[] _textures;

        public HudSprites()
        {
            Texture2D circle = Make(Size, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                return Mathf.Clamp01((1f - d) * Size * 0.5f);
            });

            // Points right: the left edge is the base, the tip is the middle of the right edge.
            Texture2D triangle = Make(Size, (x, y) =>
            {
                float u = (x + 1f) * 0.5f;
                float halfHeight = 1f - u;
                return Mathf.Clamp01((halfHeight - Mathf.Abs(y)) * Size * 0.5f);
            });

            Texture2D vignette = Make(128, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y) / 1.2f;
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, d));
            });

            _textures = new[] { circle, triangle, vignette };
            Circle = ToSprite(circle);
            Triangle = ToSprite(triangle);
            Vignette = ToSprite(vignette);
        }

        // Fills a white texture whose alpha is the function of the pixel's position in [-1, 1] x [-1, 1].
        static Texture2D Make(int size, Func<float, float, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(nx, ny)) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static Sprite ToSprite(Texture2D texture)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        public void Dispose()
        {
            DestroyObject(Circle);
            DestroyObject(Triangle);
            DestroyObject(Vignette);
            for (int i = 0; i < _textures.Length; i++)
                DestroyObject(_textures[i]);
        }

        static void DestroyObject(UnityEngine.Object o)
        {
            if (o == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(o);
            else
                UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
