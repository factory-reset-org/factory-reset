using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Agents.Guard
{
    /// <summary>
    /// Finds and scores cover cells near the player. Ranking is cheap here; the caller
    /// runs tactical A* to the top candidates and passes the true path cost back in.
    /// </summary>
    public sealed class CoverEvaluator
    {
        public const float LowCoverHeight = 0.5f;
        public const float ChestHeight = 1.2f;
        public const float MaxSearchRange = 15f;

        public const float ProtectionWeight = 0.40f;
        public const float RangeWeight = 0.25f;
        public const float TravelWeight = 0.20f;
        public const float PeekWeight = 0.15f;

        static readonly Vector2Int[] NeighbourOffsets =
        {
            new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
            new Vector2Int(-1, 0), new Vector2Int(1, 0),
            new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1),
        };

        readonly GridGraph _grid;
        readonly ICoverVisibility _visibility;
        readonly float _cellSize;

        public CoverEvaluator(GridGraph grid, ICoverVisibility visibility, float cellSize = 0.5f)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
            _cellSize = cellSize;
        }

        /// <summary>
        /// Fills <paramref name="results"/> (cleared first) with every traversable cell within
        /// <see cref="MaxSearchRange"/> metres of the player that sits next to a static
        /// obstacle or a settled box and is at least half covered.
        /// </summary>
        public void FindCandidates(Vector2Int playerCell, List<CoverCandidate> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            int radius = Mathf.CeilToInt(MaxSearchRange / _cellSize);

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    Vector2Int cell = new Vector2Int(playerCell.x + dx, playerCell.y + dy);
                    if (!_grid.IsTraversable(cell) || DistanceMetres(cell, playerCell) > MaxSearchRange)
                    {
                        continue;
                    }

                    if (!IsNextToObstacle(cell))
                    {
                        continue;
                    }

                    float protection = ProtectionAt(cell);
                    if (protection <= 0f)
                    {
                        continue;
                    }

                    results.Add(new CoverCandidate(cell, protection, CanPeekFrom(cell)));
                }
            }
        }

        /// <summary>
        /// S(c) = 0.40 P + 0.25 R + 0.20 (1 - pathCost / maxCost) + 0.15 F, in [0, 1].
        /// </summary>
        public float Score(in CoverCandidate candidate, Vector2Int playerCell, float desiredRange, float pathCost, float maxCost)
        {
            if (desiredRange <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(desiredRange), "Desired range must be positive.");
            }

            if (maxCost <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCost), "Max cost must be positive.");
            }

            float range = RangeScore(candidate.Cell, playerCell, desiredRange);
            float travel = 1f - Mathf.Clamp01(pathCost / maxCost);
            float peek = candidate.CanPeek ? 1f : 0f;

            return ProtectionWeight * candidate.Protection
                 + RangeWeight * range
                 + TravelWeight * travel
                 + PeekWeight * peek;
        }

        bool IsNextToObstacle(Vector2Int cell)
        {
            for (int i = 0; i < NeighbourOffsets.Length; i++)
            {
                Vector2Int neighbour = cell + NeighbourOffsets[i];
                if (!_grid.Contains(neighbour))
                {
                    continue;
                }

                GridNode node = _grid.GetNode(neighbour);
                if (!node.Walkable || node.BlockerCount > 0)
                {
                    return true;
                }
            }

            return false;
        }

        float ProtectionAt(Vector2Int cell)
        {
            bool lowBlocked = _visibility.IsBlocked(cell, LowCoverHeight);
            bool highBlocked = _visibility.IsBlocked(cell, ChestHeight);

            if (lowBlocked && highBlocked)
            {
                return 1f;
            }

            return lowBlocked ? 0.5f : 0f;
        }

        bool CanPeekFrom(Vector2Int cell)
        {
            for (int i = 0; i < NeighbourOffsets.Length; i++)
            {
                Vector2Int neighbour = cell + NeighbourOffsets[i];
                if (_grid.IsTraversable(neighbour) && !_visibility.IsBlocked(neighbour, ChestHeight))
                {
                    return true;
                }
            }

            return false;
        }

        float RangeScore(Vector2Int cell, Vector2Int playerCell, float desiredRange)
        {
            float distance = DistanceMetres(cell, playerCell);
            return Mathf.Clamp01(1f - Mathf.Abs(distance - desiredRange) / desiredRange);
        }

        float DistanceMetres(Vector2Int a, Vector2Int b) =>
            Vector2Int.Distance(a, b) * _cellSize;
    }
}
