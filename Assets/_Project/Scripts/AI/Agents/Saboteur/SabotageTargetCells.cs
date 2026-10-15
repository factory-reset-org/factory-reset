using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Where a trap or battery can be reached on the grid. The blackboard's fact holds the cell the
    /// object stands on, which can lie outside the grid or inside a prop, so the sources and the
    /// brain both move it to the nearest walkable cell, and skip it if there is none close by.
    /// </summary>
    public static class SabotageTargetCells
    {
        /// <summary>How far, in cells, a fact's cell may be moved to a walkable one.</summary>
        public const int SnapRadius = 4;

        /// <summary>
        /// The walkable cell to stand on to act on an object at <paramref name="factCell"/>. False when
        /// the cell is outside the grid or nothing walkable lies within <see cref="SnapRadius"/>.
        /// </summary>
        public static bool TrySnap(GridGraph grid, Vector2Int factCell, out Vector2Int cell)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            cell = default;
            if (!grid.Contains(factCell))
                return false;

            if (grid.IsTraversable(factCell))
            {
                cell = factCell;
                return true;
            }

            return grid.TryFindNearestTraversable(factCell, SnapRadius, out cell);
        }

        /// <summary>The straight octile distance between two cells, in metres.</summary>
        public static float OctileMetres(Vector2Int from, Vector2Int to) =>
            BaseCostModel.OctileDistance(from, to) * GridGraph.CellSize;

        /// <summary>
        /// Metres along <paramref name="route"/> from its first cell to the first cell within
        /// <paramref name="clearanceCells"/> cells (either axis) of <paramref name="target"/>. False when
        /// the route never comes that close.
        /// </summary>
        public static bool TryMetresAlong(IReadOnlyList<Vector2Int> route, Vector2Int target, int clearanceCells, out float metres)
        {
            metres = 0f;
            if (route == null)
                return false;

            float cells = 0f;
            for (int i = 0; i < route.Count; i++)
            {
                if (i > 0)
                    cells += BaseCostModel.OctileDistance(route[i - 1], route[i]);

                if (Math.Abs(route[i].x - target.x) <= clearanceCells && Math.Abs(route[i].y - target.y) <= clearanceCells)
                {
                    metres = cells * GridGraph.CellSize;
                    return true;
                }
            }

            return false;
        }
    }
}
