using System;
using UnityEngine;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Which way a pushable box goes when the player slides it. The box moves along the floor's
    /// four axes only, so it stays square to the walls and the grid. The way is the one that points
    /// most directly away from the player that is not blocked; when the box is jammed (in a corner,
    /// against a wall), the free way that is closest to "away" is taken, so pressing Interact from
    /// the side of a cornered box slides it out along the wall.
    /// </summary>
    public static class BoxShove
    {
        static readonly Vector3[] Axes =
        {
            new Vector3(1f, 0f, 0f),
            new Vector3(-1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, -1f)
        };

        /// <summary>A way that points more directly back at the player than this is never taken: the player is in the way.</summary>
        public const float TowardPlayerLimit = -0.9f;

        /// <summary>
        /// The way to shove, as a unit axis vector. <paramref name="away"/> points from the player to
        /// the box (its height is ignored). <paramref name="isFree"/> says whether the box can move
        /// that way. False, and a zero vector, if there is nowhere to go.
        /// </summary>
        public static bool TryChoose(Vector3 away, Func<Vector3, bool> isFree, out Vector3 direction)
        {
            direction = Vector3.zero;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f || isFree == null)
                return false;
            away.Normalize();

            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < Axes.Length; i++)
            {
                float score = Vector3.Dot(away, Axes[i]);
                if (score < TowardPlayerLimit || score <= bestScore)
                    continue;
                if (!isFree(Axes[i]))
                    continue;

                bestScore = score;
                direction = Axes[i];
            }

            return bestScore > float.NegativeInfinity;
        }
    }
}
