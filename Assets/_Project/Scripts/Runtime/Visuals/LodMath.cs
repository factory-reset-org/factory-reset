using UnityEngine;

namespace ToyFactory.Runtime.Visuals
{
    /// <summary>
    /// The arithmetic behind a <see cref="LODGroup"/>: how tall an object looks on screen and which level
    /// that picks. The group itself does not say which level it is showing, and the showcase needs to,
    /// to print the triangle count the player is really paying for.
    /// </summary>
    public static class LodMath
    {
        /// <summary>Returned by <see cref="ActiveLevel"/> when the object is too small to draw.</summary>
        public const int Culled = -1;

        /// <summary>
        /// The share of the screen's height an object of <paramref name="size"/> metres covers from
        /// <paramref name="distance"/> metres, as the LOD group measures it.
        /// </summary>
        /// <param name="size">The group's size in metres (its largest dimension times the largest scale).</param>
        /// <param name="distance">Distance from the camera to the group's centre.</param>
        /// <param name="verticalFieldOfView">The camera's vertical field of view in degrees.</param>
        public static float ScreenRelativeHeight(float size, float distance, float verticalFieldOfView)
        {
            if (size <= 0f || distance <= 0f)
                return 1f;

            float visible = 2f * distance * Mathf.Tan(Mathf.Deg2Rad * verticalFieldOfView * 0.5f);
            return Mathf.Clamp01(size / visible);
        }

        /// <summary>The same for an orthographic camera, where distance does not matter.</summary>
        public static float ScreenRelativeHeightOrthographic(float size, float orthographicSize) =>
            orthographicSize <= 0f ? 1f : Mathf.Clamp01(size / (2f * orthographicSize));

        /// <summary>
        /// The level a group shows at <paramref name="height"/>, given the screen-relative heights below
        /// which each level gives way to the next (level 0 first). <see cref="Culled"/> below the last.
        /// </summary>
        public static int ActiveLevel(float height, float[] transitionHeights)
        {
            for (int i = 0; i < transitionHeights.Length; i++)
            {
                if (height >= transitionHeights[i])
                    return i;
            }

            return Culled;
        }
    }
}
