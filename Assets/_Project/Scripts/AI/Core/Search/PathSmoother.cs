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
    }
}
