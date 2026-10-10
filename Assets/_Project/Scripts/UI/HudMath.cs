using UnityEngine;

namespace ToyFactory.UI
{
    /// <summary>
    /// The HUD's arithmetic, kept apart from the views so it can be tested without a scene: bar
    /// fractions, the damage vignette and where the objective arrow sits on the screen edge.
    /// </summary>
    public static class HudMath
    {
        /// <summary>Seconds the damage flash takes to fade after a hit.</summary>
        public const float HitFlashSeconds = 0.5f;

        /// <summary>Peak vignette opacity right after a hit.</summary>
        public const float HitFlashAlpha = 0.55f;

        /// <summary>Integrity below this fraction keeps a steady vignette on.</summary>
        public const float LowHealthFraction = 0.3f;

        /// <summary>Peak opacity of the low-health vignette, at zero integrity.</summary>
        public const float LowHealthAlpha = 0.3f;

        /// <summary>"CHAPTER 2 OF 4".</summary>
        public static string ChapterTag(int chapter, int chapterCount) => $"CHAPTER {chapter} OF {chapterCount}";

        /// <summary>A fraction clamped to [0, 1]; NaN reads as 0.</summary>
        public static float Fraction(float value) => float.IsNaN(value) ? 0f : Mathf.Clamp01(value);

        /// <summary>
        /// The overcharge bar's fill: time left over the longest overcharge seen so far, so the bar
        /// starts full and drains whatever the length of the overcharge is.
        /// </summary>
        public static float OverchargeFraction(float timeLeft, float longestSeen) =>
            longestSeen <= 0f ? 0f : Fraction(timeLeft / longestSeen);

        /// <summary>
        /// Opacity of the red damage vignette: a flash that fades over <see cref="HitFlashSeconds"/>
        /// after a hit, or a steady glow while integrity is low, whichever is stronger.
        /// </summary>
        /// <param name="secondsSinceHit">Seconds since the last hit; pass a large number for "never hit".</param>
        /// <param name="integrity">Player integrity in [0, 1].</param>
        public static float VignetteAlpha(float secondsSinceHit, float integrity)
        {
            float flash = secondsSinceHit < 0f || secondsSinceHit >= HitFlashSeconds
                ? 0f
                : HitFlashAlpha * (1f - secondsSinceHit / HitFlashSeconds);

            float health = Fraction(integrity);
            float low = health >= LowHealthFraction ? 0f : LowHealthAlpha * (1f - health / LowHealthFraction);
            return Mathf.Max(flash, low);
        }

        /// <summary>
        /// Where the objective arrow goes. <paramref name="viewport"/> is the objective's position in
        /// viewport space (<c>Camera.WorldToViewportPoint</c>: x and y in 0-1 across the screen, z the
        /// distance in front of the camera, negative behind it). Returns false when the objective is
        /// on screen, so the arrow hides and the beacon in the world shows the way. Otherwise
        /// <paramref name="edgePosition"/> is a viewport point on the screen edge inset by
        /// <paramref name="margin"/>, and <paramref name="angleDegrees"/> points the arrow at the
        /// objective (0 = right, 90 = up). An objective behind the camera points the opposite way, so
        /// the arrow always turns the player towards it.
        /// </summary>
        public static bool TryPlaceArrow(Vector3 viewport, float margin, out Vector2 edgePosition, out float angleDegrees)
        {
            margin = Mathf.Clamp(margin, 0f, 0.45f);
            bool inFront = viewport.z > 0f;
            if (inFront && viewport.x > margin && viewport.x < 1f - margin && viewport.y > margin && viewport.y < 1f - margin)
            {
                edgePosition = default;
                angleDegrees = 0f;
                return false;
            }

            var direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (!inFront)
                direction = -direction;

            // Straight behind or dead centre has no side to point to: point down.
            if (direction.sqrMagnitude < 1e-8f)
                direction = Vector2.down;

            float halfWidth = 0.5f - margin;
            float halfHeight = 0.5f - margin;
            float scaleX = Mathf.Abs(direction.x) > 1e-6f ? halfWidth / Mathf.Abs(direction.x) : float.MaxValue;
            float scaleY = Mathf.Abs(direction.y) > 1e-6f ? halfHeight / Mathf.Abs(direction.y) : float.MaxValue;
            float scale = Mathf.Min(scaleX, scaleY);

            edgePosition = new Vector2(0.5f + direction.x * scale, 0.5f + direction.y * scale);
            angleDegrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>Distance text for the arrow: "12 m", rounded to whole metres.</summary>
        public static string DistanceText(float metres) => $"{Mathf.Max(0, Mathf.RoundToInt(metres))} m";
    }
}
