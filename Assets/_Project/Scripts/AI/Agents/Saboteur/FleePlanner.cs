using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Picks where a fleeing Saboteur runs to: a free cell well away from the player that it can
    /// reach without running past the player. Plain C# over the grid and the shared pathfinder, so the
    /// brain stays free of scene objects and the choice can be tested on a bare grid.
    /// </summary>
    /// <remarks>
    /// <para><b>Candidates.</b> Cells 5, 8 and 12 m from the Saboteur in the eight compass directions,
    /// plus any spots the caller knows (the patrol points). A cell that is not walkable is snapped to the
    /// nearest walkable one within <see cref="SnapRadius"/> cells or dropped.</para>
    /// <para><b>Ranking.</b> By <c>distance from the player - TravelWeight x distance to run</c>, so a far
    /// refuge beats a near one only if it is not a long run, and only cells at least
    /// <see cref="MinGainMetres"/> farther from the player than the Saboteur is now are kept. Ranking uses
    /// straight-line distances; the shared A* runs for at most <see cref="MaxSearches"/> of the best, so a
    /// retreat costs a handful of searches, once a second at most.</para>
    /// <para><b>Safety.</b> A route that goes closer to the player than the Saboteur stands now (down to
    /// <see cref="PlayerClearanceMetres"/>) is rejected, so it never runs through the player.</para>
    /// </remarks>
    public static class FleePlanner
    {
        /// <summary>How much farther from the player, in metres, a refuge must be than the Saboteur is now.</summary>
        public const float MinGainMetres = 3f;

        /// <summary>How much a metre of running costs against a metre gained from the player.</summary>
        public const float TravelWeight = 0.4f;

        /// <summary>A route may not pass closer than this to the player, unless the Saboteur already stands closer.</summary>
        public const float PlayerClearanceMetres = 2.5f;

        /// <summary>The most A* searches one retreat may use.</summary>
        public const int MaxSearches = 3;

        /// <summary>How far, in cells, a blocked candidate may be snapped to a walkable cell.</summary>
        public const int SnapRadius = 2;

        static readonly float[] RadiiMetres = { 5f, 8f, 12f };

        readonly struct Refuge
        {
            public readonly Vector2Int Cell;
            public readonly float Score;

            public Refuge(Vector2Int cell, float score)
            {
                Cell = cell;
                Score = score;
            }
        }

        /// <summary>Finds a retreat route, or false if the Saboteur is cornered.</summary>
        /// <param name="grid">The navigation grid.</param>
        /// <param name="pathfinder">The shared search over <paramref name="grid"/>.</param>
        /// <param name="fromCell">The Saboteur's cell, where the route starts.</param>
        /// <param name="from">The Saboteur's world position.</param>
        /// <param name="player">The player's world position.</param>
        /// <param name="knownSpots">Extra cells worth considering as refuges; may be null.</param>
        /// <param name="route">The cells to run along, start first.</param>
        public static bool TryChooseRetreat(GridGraph grid, IPathfinder pathfinder, Vector2Int fromCell, Vector3 from,
            Vector3 player, IReadOnlyList<Vector2Int> knownSpots, out List<Vector2Int> route)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (pathfinder == null) throw new ArgumentNullException(nameof(pathfinder));

            route = null;
            float startDistance = Flat(from, player);
            var refuges = new List<Refuge>(RadiiMetres.Length * 8 + (knownSpots != null ? knownSpots.Count : 0));

            for (int direction = 0; direction < 8; direction++)
            {
                float angle = direction * Mathf.PI * 0.25f;
                float dx = Mathf.Cos(angle);
                float dz = Mathf.Sin(angle);
                for (int r = 0; r < RadiiMetres.Length; r++)
                {
                    var world = new Vector3(from.x + dx * RadiiMetres[r], from.y, from.z + dz * RadiiMetres[r]);
                    if (grid.TryWorldToCell(world, out Vector2Int cell))
                        AddRefuge(grid, cell, fromCell, from, player, startDistance, refuges);
                }
            }

            if (knownSpots != null)
            {
                for (int i = 0; i < knownSpots.Count; i++)
                    AddRefuge(grid, knownSpots[i], fromCell, from, player, startDistance, refuges);
            }

            // Best first. A stable order keeps the choice repeatable when two refuges score alike.
            for (int i = 1; i < refuges.Count; i++)
            {
                Refuge item = refuges[i];
                int j = i - 1;
                while (j >= 0 && refuges[j].Score < item.Score)
                {
                    refuges[j + 1] = refuges[j];
                    j--;
                }

                refuges[j + 1] = item;
            }

            float clearance = Math.Min(PlayerClearanceMetres, startDistance - 0.01f);
            int searches = 0;
            for (int i = 0; i < refuges.Count && searches < MaxSearches; i++)
            {
                searches++;
                PathResult result = pathfinder.FindPath(fromCell, refuges[i].Cell, BaseCostModel.Instance);
                if (!result.Found || result.Cells == null || result.Cells.Count == 0)
                    continue;

                if (PassesPlayer(grid, result.Cells, player, clearance))
                    continue;

                route = result.Cells;
                return true;
            }

            return false;
        }

        static void AddRefuge(GridGraph grid, Vector2Int cell, Vector2Int fromCell, Vector3 from, Vector3 player,
            float startDistance, List<Refuge> refuges)
        {
            if (!grid.Contains(cell))
                return;
            if (!grid.IsTraversable(cell) && !grid.TryFindNearestTraversable(cell, SnapRadius, out cell))
                return;
            if (cell == fromCell)
                return;

            Vector3 world = grid.CellToWorld(cell);
            float fromPlayer = Flat(world, player);
            if (fromPlayer < startDistance + MinGainMetres)
                return;

            float travel = BaseCostModel.OctileDistance(fromCell, cell) * GridGraph.CellSize;
            refuges.Add(new Refuge(cell, fromPlayer - TravelWeight * travel));
        }

        static bool PassesPlayer(GridGraph grid, List<Vector2Int> cells, Vector3 player, float clearance)
        {
            if (clearance <= 0f)
                return false;

            float limit = clearance * clearance;
            for (int i = 0; i < cells.Count; i++)
            {
                Vector3 world = grid.CellToWorld(cells[i]);
                float dx = world.x - player.x;
                float dz = world.z - player.z;
                if (dx * dx + dz * dz < limit)
                    return true;
            }

            return false;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
