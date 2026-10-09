using System;
using UnityEngine;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// Answers "can an agent walk in a straight line from A to B?" on the same grid the
    /// brains plan on. Used by path smoothing to drop waypoints the agent does not need.
    /// <see cref="IsSightClear"/> answers "can a tall agent see from A to B?": the same trace,
    /// but props and boxes (grid blockers) do not block it, only walls and closed doors.
    /// </summary>
    /// <remarks>
    /// The segment is traced cell by cell through every cell it touches (a supercover
    /// line, Amanatides and Woo traversal), and every one of those cells must be
    /// traversable. When the line passes exactly through a cell corner, both side cells
    /// must be traversable too, which is the same no-corner-cutting rule
    /// <see cref="GridGraph"/> uses for diagonal steps. So a straight line accepted here is
    /// never a shortcut that A* itself would forbid, closed doors included. World Y is
    /// ignored. Allocates nothing.
    /// </remarks>
    public static class GridLineCheck
    {
        // Two boundary crossings closer than this (in cells along the line) count as one
        // crossing through a corner.
        const float CornerTolerance = 1e-4f;

        /// <summary>True if every cell the segment from <paramref name="from"/> to <paramref name="to"/> touches is traversable.</summary>
        public static bool IsWalkable(GridGraph grid, Vector3 from, Vector3 to) => Trace(grid, from, to, overProps: false);

        /// <summary>
        /// True if a tall agent standing at <paramref name="from"/> can see <paramref name="to"/>:
        /// the same cell-by-cell trace, but only walls and closed doors block it. Cells blocked
        /// by a box or a prop (grid blockers: the console, a switch cage, a crate) do not, because
        /// those are low enough to see over. For walking, use <see cref="IsWalkable"/>.
        /// </summary>
        public static bool IsSightClear(GridGraph grid, Vector3 from, Vector3 to) => Trace(grid, from, to, overProps: true);

        // A cell the line may pass through: traversable for walking; for sight over props, any
        // walkable floor that is not a closed door, whatever stands on it.
        static bool Passable(GridGraph grid, Vector2Int cell, bool overProps)
        {
            if (!overProps)
                return grid.IsTraversable(cell);
            if (!grid.Contains(cell))
                return false;
            GridNode node = grid.GetNode(cell);
            return node.Walkable && !node.IsDoorClosed;
        }

        static bool Trace(GridGraph grid, Vector3 from, Vector3 to, bool overProps)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            // Continuous grid coordinates: one unit per cell, cell (x, y) spans [x, x + 1).
            float u0 = (from.x - grid.Origin.x) / GridGraph.CellSize;
            float v0 = (from.z - grid.Origin.z) / GridGraph.CellSize;
            float u1 = (to.x - grid.Origin.x) / GridGraph.CellSize;
            float v1 = (to.z - grid.Origin.z) / GridGraph.CellSize;

            int x = Mathf.FloorToInt(u0);
            int y = Mathf.FloorToInt(v0);
            int endX = Mathf.FloorToInt(u1);
            int endY = Mathf.FloorToInt(v1);

            if (!Passable(grid, new Vector2Int(x, y), overProps))
                return false;

            float du = u1 - u0;
            float dv = v1 - v0;
            int stepX = du > 0f ? 1 : du < 0f ? -1 : 0;
            int stepY = dv > 0f ? 1 : dv < 0f ? -1 : 0;

            // t runs from 0 at the start to 1 at the end. tMax is the t of the next
            // vertical or horizontal cell boundary; tDelta is the t between boundaries.
            float tDeltaX = stepX != 0 ? 1f / Mathf.Abs(du) : float.PositiveInfinity;
            float tDeltaY = stepY != 0 ? 1f / Mathf.Abs(dv) : float.PositiveInfinity;
            float tMaxX = stepX > 0 ? (x + 1 - u0) * tDeltaX : stepX < 0 ? (u0 - x) * tDeltaX : float.PositiveInfinity;
            float tMaxY = stepY > 0 ? (y + 1 - v0) * tDeltaY : stepY < 0 ? (v0 - y) * tDeltaY : float.PositiveInfinity;

            float length = Mathf.Sqrt(du * du + dv * dv);
            float tolerance = length > 0f ? CornerTolerance / length : 0f;

            // Each step moves one cell closer on X or Y (a corner step moves on both), so
            // this bounds the loop even if rounding would otherwise walk past the end.
            int remaining = Mathf.Abs(endX - x) + Mathf.Abs(endY - y);
            while (remaining > 0)
            {
                if (Mathf.Abs(tMaxX - tMaxY) <= tolerance && remaining >= 2)
                {
                    // Exactly through a corner: a diagonal step, which needs both side cells.
                    if (!Passable(grid, new Vector2Int(x + stepX, y), overProps) ||
                        !Passable(grid, new Vector2Int(x, y + stepY), overProps))
                        return false;
                    x += stepX;
                    y += stepY;
                    tMaxX += tDeltaX;
                    tMaxY += tDeltaY;
                    remaining -= 2;
                }
                else if (tMaxX < tMaxY)
                {
                    x += stepX;
                    tMaxX += tDeltaX;
                    remaining--;
                }
                else
                {
                    y += stepY;
                    tMaxY += tDeltaY;
                    remaining--;
                }

                if (!Passable(grid, new Vector2Int(x, y), overProps))
                    return false;
            }

            return true;
        }
    }
}
