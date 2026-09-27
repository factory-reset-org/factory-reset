using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using ToyFactory.AI.Core.Collections;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// One-to-all shortest-path costs from a single source cell (Dijkstra / uniform-cost
    /// search). After <see cref="Compute"/>, <see cref="Cost"/> answers "how far is this
    /// cell from the source?" for any cell in O(1), so one search serves many questions.
    /// Used by the Captain for goal fields (distance from every cell to a goal) and for its
    /// own reach field (how soon it can get to every cell).
    /// </summary>
    /// <remarks>
    /// Costs are in grid units under the given <see cref="ICostModel"/> (multiply by
    /// <see cref="GridGraph.CellSize"/> for metres). Grid movement and the base cost model
    /// are symmetric, so the cost from the source to a cell equals the cost from that cell
    /// back to the source. All per-cell arrays are allocated once per grid size and reused:
    /// each compute bumps a stamp instead of clearing them, so computing allocates nothing.
    /// </remarks>
    public sealed class DijkstraField
    {
        static readonly ProfilerMarker Marker = new ProfilerMarker("AI.DijkstraField.Compute");

        readonly GridGraph _grid;
        readonly Vector2Int[] _neighbourBuffer = new Vector2Int[8];
        readonly Stopwatch _stopwatch = new Stopwatch();

        BinaryHeap _open;
        float[] _cost;
        int[] _seenStamp;
        int[] _closedStamp;
        int _stamp;
        int _cellCount;

        /// <summary>The cell the field was last computed from.</summary>
        public Vector2Int Source { get; private set; }

        /// <summary>True once <see cref="Compute"/> has run at least once.</summary>
        public bool HasBeenComputed { get; private set; }

        /// <summary>The grid's <see cref="GridGraph.Version"/> when the field was last computed.</summary>
        public int GraphVersion { get; private set; }

        /// <summary>True when the grid has changed since the field was computed.</summary>
        public bool IsStale => !HasBeenComputed || _grid.Version != GraphVersion;

        /// <summary>
        /// The cost limit of the last compute. Cells further than this from the source are
        /// left unreachable. <see cref="float.PositiveInfinity"/> means unbounded.
        /// </summary>
        public float MaxCost { get; private set; } = float.PositiveInfinity;

        /// <summary>Cells expanded by the last compute, for the performance log.</summary>
        public int NodesExpanded { get; private set; }

        /// <summary>Wall-clock time of the last compute, in milliseconds.</summary>
        public float ElapsedMs { get; private set; }

        public DijkstraField(GridGraph grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            AllocateForGridSize();
        }

        /// <summary>
        /// Fills the field with the shortest-path cost from <paramref name="source"/> to every
        /// reachable cell. If the source is not traversable, every cell is left unreachable;
        /// callers snap the source first with <see cref="GridGraph.TryFindNearestTraversable"/>.
        /// </summary>
        /// <param name="source">Cell the costs are measured from.</param>
        /// <param name="cost">Step cost model; use <see cref="BaseCostModel.Instance"/> for plain distance.</param>
        /// <param name="maxCost">
        /// Stop spreading past this cost (grid units). Cells beyond it stay unreachable, which
        /// keeps the work proportional to the area that matters. Unbounded by default.
        /// </param>
        public void Compute(Vector2Int source, ICostModel cost, float maxCost = float.PositiveInfinity)
        {
            if (cost == null)
                throw new ArgumentNullException(nameof(cost));
            if (float.IsNaN(maxCost) || maxCost < 0f)
                throw new ArgumentOutOfRangeException(nameof(maxCost), "Max cost must be zero or positive.");

            using (Marker.Auto())
            {
                _stopwatch.Restart();
                if (_grid.CellCount != _cellCount)
                    AllocateForGridSize();

                NextStamp();
                _open.Clear();
                Source = source;
                MaxCost = maxCost;
                GraphVersion = _grid.Version;
                HasBeenComputed = true;

                int expanded = 0;
                if (_grid.IsTraversable(source))
                {
                    int sourceIndex = _grid.ToIndex(source);
                    _cost[sourceIndex] = 0f;
                    _seenStamp[sourceIndex] = _stamp;
                    _open.Push(sourceIndex, 0f);

                    while (!_open.IsEmpty)
                    {
                        // Popped in increasing cost order, so this cell's cost is now final.
                        int current = _open.Pop();
                        _closedStamp[current] = _stamp;
                        expanded++;

                        Vector2Int currentCell = _grid.FromIndex(current);
                        int neighbourCount = _grid.GetNeighboursNonAlloc(currentCell, _neighbourBuffer);

                        for (int i = 0; i < neighbourCount; i++)
                        {
                            Vector2Int next = _neighbourBuffer[i];
                            int nextIndex = _grid.ToIndex(next);
                            if (_closedStamp[nextIndex] == _stamp)
                                continue;

                            float newCost = _cost[current] + cost.StepCost(currentCell, next);

                            // Beyond the bound: never queued, so it stays unreachable and
                            // the search stops spreading in that direction.
                            if (newCost > maxCost)
                                continue;

                            bool firstVisit = _seenStamp[nextIndex] != _stamp;
                            if (!firstVisit && newCost >= _cost[nextIndex])
                                continue;

                            _cost[nextIndex] = newCost;
                            if (firstVisit)
                            {
                                _seenStamp[nextIndex] = _stamp;
                                _open.Push(nextIndex, newCost);
                            }
                            else
                            {
                                _open.DecreaseKey(nextIndex, newCost);
                            }
                        }
                    }
                }

                NodesExpanded = expanded;
                ElapsedMs = (float)_stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>
        /// Shortest-path cost from the source to <paramref name="cell"/> in grid units, or
        /// <see cref="float.PositiveInfinity"/> if the cell is unreachable, outside the grid,
        /// or the field has not been computed.
        /// </summary>
        public float Cost(Vector2Int cell)
        {
            if (!HasBeenComputed || !_grid.Contains(cell))
                return float.PositiveInfinity;

            int index = _grid.ToIndex(cell);
            return _closedStamp[index] == _stamp ? _cost[index] : float.PositiveInfinity;
        }

        /// <summary>True if the last compute found a route from the source to <paramref name="cell"/>.</summary>
        public bool IsReachable(Vector2Int cell) => !float.IsPositiveInfinity(Cost(cell));

        void AllocateForGridSize()
        {
            _cellCount = _grid.CellCount;
            _open = new BinaryHeap(_cellCount);
            _cost = new float[_cellCount];
            _seenStamp = new int[_cellCount];
            _closedStamp = new int[_cellCount];
            _stamp = 0;
            HasBeenComputed = false;
        }

        void NextStamp()
        {
            if (_stamp == int.MaxValue)
            {
                Array.Clear(_seenStamp, 0, _seenStamp.Length);
                Array.Clear(_closedStamp, 0, _closedStamp.Length);
                _stamp = 0;
            }
            _stamp++;
        }
    }
}
