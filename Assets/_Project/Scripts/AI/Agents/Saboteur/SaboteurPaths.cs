using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Walking distances for the Saboteur: how far a target really is along the grid (not in a straight line),
    /// and how much of a route is left from where the body stands. Both use the squad's pathfinder and grid,
    /// never a search of their own.
    /// </summary>
    public static class SaboteurPaths
    {
        /// <summary>How far, in cells, a blocked start or goal may be moved to a walkable cell.</summary>
        public const int SnapRadius = 4;

        // The A* runs that decide whether a door, trap or battery can be reached; one per candidate per decision.
        static readonly ProfilerMarker WalkMarker = new ProfilerMarker("AI.Saboteur.WalkReach");

        /// <summary>
        /// The walking distance in metres from <paramref name="from"/> to <paramref name="to"/>, or false
        /// when there is no route. Blocked ends are snapped to the nearest walkable cell first.
        /// </summary>
        public static bool TryWalkMetres(GridGraph grid, IPathfinder pathfinder, Vector2Int from, Vector2Int to, out float metres)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (pathfinder == null)
                throw new ArgumentNullException(nameof(pathfinder));

            metres = 0f;
            if (!Snap(grid, from, out from) || !Snap(grid, to, out to))
                return false;
            if (from == to)
                return true;

            using (WalkMarker.Auto())
            {
                PathResult result = pathfinder.FindPath(from, to, BaseCostModel.Instance);
                if (!result.Found || result.Cells == null || result.Cells.Count == 0)
                    return false;

                metres = Length(result.Cells, from);
                return true;
            }
        }

        /// <summary>The length of a route in metres, starting from <paramref name="start"/> (which may not be the first cell).</summary>
        public static float Length(IReadOnlyList<Vector2Int> cells, Vector2Int start)
        {
            float total = 0f;
            Vector2Int previous = start;
            for (int i = 0; i < cells.Count; i++)
            {
                total += BaseCostModel.OctileDistance(previous, cells[i]);
                previous = cells[i];
            }

            return total * GridGraph.CellSize;
        }

        /// <summary>
        /// Metres of <paramref name="route"/> still to walk from <paramref name="position"/>: the distance to
        /// the nearest route cell at or after <paramref name="index"/>, then along the route to its end. The
        /// index only moves forward, so a body that strays a little does not jump back to the start.
        /// This is progress along the way the Saboteur actually walks, so a route that first turns away from the
        /// target (round a wall) still counts as getting closer.
        /// </summary>
        public static float RemainingMetres(GridGraph grid, IReadOnlyList<Vector2Int> route, Vector3 position, ref int index)
        {
            if (route == null || route.Count == 0)
                return 0f;

            if (index < 0)
                index = 0;
            if (index >= route.Count)
                index = route.Count - 1;

            // Look a few cells ahead for the nearest one; the body walks the route in order.
            int best = index;
            float bestSqr = float.MaxValue;
            int last = Math.Min(route.Count - 1, index + 12);
            for (int i = index; i <= last; i++)
            {
                Vector3 world = grid.CellToWorld(route[i]);
                float dx = world.x - position.x;
                float dz = world.z - position.z;
                float sqr = dx * dx + dz * dz;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            index = best;
            float metres = (float)Math.Sqrt(bestSqr);
            for (int i = best + 1; i < route.Count; i++)
                metres += BaseCostModel.OctileDistance(route[i - 1], route[i]) * GridGraph.CellSize;
            return metres;
        }

        static bool Snap(GridGraph grid, Vector2Int cell, out Vector2Int result)
        {
            if (grid.IsTraversable(cell))
            {
                result = cell;
                return true;
            }

            return grid.TryFindNearestTraversable(cell, SnapRadius, out result);
        }
    }
}
