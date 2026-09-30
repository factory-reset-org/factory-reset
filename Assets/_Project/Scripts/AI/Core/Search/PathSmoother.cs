using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// Turns a grid path (one waypoint per cell centre) into the few waypoints an agent
    /// actually has to turn at, so it walks straight lines instead of a 45-degree staircase.
    /// </summary>
    public static class PathSmoother
    {
        /// <summary>
        /// String pulling: from each kept waypoint, skips ahead as far as a straight line
        /// stays walkable (<see cref="GridLineCheck"/>), then keeps the last reachable point
        /// and carries on from there. The first and last waypoints are always kept.
        /// </summary>
        /// <remarks>
        /// Every segment of the result is either a step of the original path or a line that
        /// passed <see cref="GridLineCheck"/>, so the smoothed route never crosses a cell the
        /// original route avoided. The result keeps a subset of the original waypoints in
        /// order, so it is never longer. Each waypoint is tested once, so the cost is one
        /// line check per waypoint. Writes into <paramref name="result"/> (cleared first) and
        /// allocates nothing once that list has enough capacity.
        /// </remarks>
        /// <param name="grid">The grid the path was planned on.</param>
        /// <param name="path">World-space waypoints in walking order. Not modified.</param>
        /// <param name="result">Receives the kept waypoints. Must not be <paramref name="path"/>.</param>
        public static void StringPull(GridGraph grid, IReadOnlyList<Vector3> path, List<Vector3> result)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (ReferenceEquals(path, result))
                throw new ArgumentException("The result list must be a different list from the path.", nameof(result));

            result.Clear();
            if (path.Count <= 2)
            {
                for (int i = 0; i < path.Count; i++)
                    result.Add(path[i]);
                return;
            }

            int last = path.Count - 1;
            Vector3 anchor = path[0];
            result.Add(anchor);

            // path[i] is always reachable in a straight line from the anchor. Try to reach
            // one point further; if the line is blocked, path[i] is a corner and is kept.
            for (int i = 1; i < last; i++)
            {
                if (GridLineCheck.IsWalkable(grid, anchor, path[i + 1]))
                    continue;

                anchor = path[i];
                result.Add(anchor);
            }

            result.Add(path[last]);
        }

        /// <summary>Default distance in metres between curve samples: one grid cell.</summary>
        public const float DefaultSpacing = GridGraph.CellSize;

        /// <summary>
        /// Rounds the corners of a path with a centripetal Catmull-Rom spline through its
        /// waypoints, sampled about every <paramref name="spacing"/> metres. Every original
        /// waypoint stays on the curve. A span whose curve would enter a cell that fails
        /// <see cref="GridLineCheck"/> is kept straight instead, so the curve never cuts
        /// into a wall that the straight path avoided.
        /// </summary>
        /// <remarks>
        /// Centripetal parameterisation (alpha = 0.5) spaces the spline's knots by the square
        /// root of the distance between waypoints. Unlike the uniform version it never forms
        /// loops or cusps, and it overshoots less at sharp corners. The ends get a mirrored
        /// phantom point, so the first and last spans leave and arrive in a straight line.
        /// Writes into <paramref name="result"/> (cleared first) and allocates nothing once
        /// that list has enough capacity. Use after <see cref="StringPull"/>.
        /// </remarks>
        public static void CatmullRom(GridGraph grid, IReadOnlyList<Vector3> path, List<Vector3> result,
            float spacing = DefaultSpacing)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (ReferenceEquals(path, result))
                throw new ArgumentException("The result list must be a different list from the path.", nameof(result));
            if (!(spacing > 0f) || float.IsInfinity(spacing))
                throw new ArgumentOutOfRangeException(nameof(spacing), "Spacing must be a positive, finite number of metres.");

            result.Clear();
            if (path.Count == 0)
                return;

            result.Add(path[0]);
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector3 p1 = path[i];
                Vector3 p2 = path[i + 1];
                Vector3 p0 = i > 0 ? path[i - 1] : 2f * p1 - p2;
                Vector3 p3 = i + 2 < path.Count ? path[i + 2] : 2f * p2 - p1;

                int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(p1, p2) / spacing));
                int spanStart = result.Count;
                Vector3 previous = p1;
                bool safe = true;

                for (int k = 1; k < samples; k++)
                {
                    Vector3 point = CentripetalPoint(p0, p1, p2, p3, (float)k / samples);
                    if (!GridLineCheck.IsWalkable(grid, previous, point))
                    {
                        safe = false;
                        break;
                    }
                    result.Add(point);
                    previous = point;
                }

                if (safe && !GridLineCheck.IsWalkable(grid, previous, p2))
                    safe = false;

                // Unsafe curve: drop this span's samples and walk it straight.
                if (!safe)
                    result.RemoveRange(spanStart, result.Count - spanStart);

                result.Add(p2);
            }
        }

        // Point on the centripetal Catmull-Rom span from p1 to p2, with u running 0..1.
        // Barry and Goldman's pyramidal form: three linear blends, then two, then one.
        static Vector3 CentripetalPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            const float MinKnotGap = 1e-4f;
            float t0 = 0f;
            float t1 = t0 + Mathf.Max(MinKnotGap, Mathf.Sqrt(Vector3.Distance(p0, p1)));
            float t2 = t1 + Mathf.Max(MinKnotGap, Mathf.Sqrt(Vector3.Distance(p1, p2)));
            float t3 = t2 + Mathf.Max(MinKnotGap, Mathf.Sqrt(Vector3.Distance(p2, p3)));
            float t = Mathf.Lerp(t1, t2, u);

            Vector3 a1 = Blend(p0, p1, t0, t1, t);
            Vector3 a2 = Blend(p1, p2, t1, t2, t);
            Vector3 a3 = Blend(p2, p3, t2, t3, t);
            Vector3 b1 = Blend(a1, a2, t0, t2, t);
            Vector3 b2 = Blend(a2, a3, t1, t3, t);
            return Blend(b1, b2, t1, t2, t);
        }

        static Vector3 Blend(Vector3 a, Vector3 b, float ta, float tb, float t) =>
            (tb - t) / (tb - ta) * a + (t - ta) / (tb - ta) * b;
    }
}
