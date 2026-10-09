using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

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

        // A cell that could be cover, before its line of sight has been tested.
        struct Pending
        {
            public Vector2Int Cell;
            public float Estimate;   // octile distance from the agent, the cheap path cost
            public float Bound;      // the best score the cell could reach
        }

        sealed class ByBoundDescending : IComparer<Pending>
        {
            public static readonly ByBoundDescending Instance = new ByBoundDescending();
            public int Compare(Pending a, Pending b) => b.Bound.CompareTo(a.Bound);
        }

        readonly GridGraph _grid;
        readonly ICoverVisibility _visibility;
        readonly float _cellSize;
        readonly List<Pending> _pending = new List<Pending>();

        // Every traversable cell with an obstacle next to it: the only cells that can be
        // cover. It depends on the grid alone, so it is rebuilt only when the grid changes.
        readonly List<Vector2Int> _edgeCells = new List<Vector2Int>();
        int _edgeVersion = -1;

        /// <summary>How often the list of cells next to obstacles has been rebuilt.</summary>
        public int EdgeRebuilds { get; private set; }

        /// <summary>Cells whose line of sight the latest <see cref="FindBest"/> had to test.</summary>
        public int CellsTested { get; private set; }

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
        /// <summary>
        /// Fills <paramref name="results"/> (cleared first, best first) with the
        /// <paramref name="count"/> highest scoring cover cells, using the octile distance from
        /// <paramref name="fromCell"/> as the path cost. It gives the same cells as scoring
        /// everything <see cref="FindCandidates"/> returns and keeping the top ones, but tests
        /// line of sight for far fewer.
        /// </summary>
        /// <remarks>
        /// Line of sight is the expensive part, and the rest of the score is not. So every
        /// possible cell first gets an upper bound: its score if it turned out to be full,
        /// peekable cover. Cells are then tested in order of that bound, and the search stops
        /// once the worst cell kept already scores at least the bound of the next one, because
        /// nothing after it can do better.
        /// </remarks>
        /// <param name="fullCoverOnly">Leave out half cover.</param>
        /// <param name="isAvailable">Leaves out cells it returns false for (reserved by another agent). May be null.</param>
        public void FindBest(Vector2Int playerCell, Vector2Int fromCell, float desiredRange, float maxCost,
            bool fullCoverOnly, Predicate<Vector2Int> isAvailable, int count, List<RankedCover> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");
            }

            results.Clear();
            _pending.Clear();
            CellsTested = 0;
            SyncEdgeCells();

            float maxCells = MaxSearchRange / _cellSize;
            float maxCellsSquared = maxCells * maxCells;
            for (int i = 0; i < _edgeCells.Count; i++)
            {
                Vector2Int cell = _edgeCells[i];
                int dx = cell.x - playerCell.x, dy = cell.y - playerCell.y;
                if (dx * dx + dy * dy > maxCellsSquared)
                {
                    continue;
                }

                if (isAvailable != null && !isAvailable(cell))
                {
                    continue;
                }

                float estimate = BaseCostModel.OctileDistance(fromCell, cell);
                float bound = Score(new CoverCandidate(cell, 1f, true), playerCell, desiredRange, estimate, maxCost);
                _pending.Add(new Pending { Cell = cell, Estimate = estimate, Bound = bound });
            }

            _pending.Sort(ByBoundDescending.Instance);

            for (int i = 0; i < _pending.Count; i++)
            {
                Pending pending = _pending[i];
                if (results.Count >= count && results[results.Count - 1].Score >= pending.Bound)
                {
                    break;
                }

                CellsTested++;
                float protection = ProtectionAt(pending.Cell);
                if (protection <= 0f || (fullCoverOnly && protection < 1f))
                {
                    continue;
                }

                var candidate = new CoverCandidate(pending.Cell, protection, CanPeekFrom(pending.Cell));
                float score = Score(candidate, playerCell, desiredRange, pending.Estimate, maxCost);
                Keep(results, new RankedCover(candidate, score), count);
            }
        }

        /// <summary>
        /// Judges one cell by the same rules as <see cref="FindCandidates"/>. False if it is not
        /// cover from the player right now.
        /// </summary>
        public bool TryEvaluate(Vector2Int cell, Vector2Int playerCell, out CoverCandidate candidate)
        {
            candidate = default;
            if (!_grid.IsTraversable(cell) || DistanceMetres(cell, playerCell) > MaxSearchRange || !IsNextToObstacle(cell))
            {
                return false;
            }

            float protection = ProtectionAt(cell);
            if (protection <= 0f)
            {
                return false;
            }

            candidate = new CoverCandidate(cell, protection, CanPeekFrom(cell));
            return true;
        }

        void SyncEdgeCells()
        {
            if (_edgeVersion == _grid.Version)
            {
                return;
            }

            EdgeRebuilds++;
            _edgeCells.Clear();
            for (int y = 0; y < _grid.Height; y++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (_grid.IsTraversable(cell) && IsNextToObstacle(cell))
                    {
                        _edgeCells.Add(cell);
                    }
                }
            }

            _edgeVersion = _grid.Version;
        }

        // Inserts in score order, best first, and drops whatever falls past the count.
        static void Keep(List<RankedCover> results, RankedCover item, int count)
        {
            int at = results.Count;
            while (at > 0 && results[at - 1].Score < item.Score)
            {
                at--;
            }

            if (at >= count)
            {
                return;
            }

            results.Insert(at, item);
            if (results.Count > count)
            {
                results.RemoveAt(results.Count - 1);
            }
        }

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
