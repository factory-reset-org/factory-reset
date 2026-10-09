using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using ToyFactory.AI.Core.Collections;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// The shortest-path cost between two cells, and nothing else: A* with the octile
    /// heuristic that stops when the target is reached and keeps no route. For a question
    /// about one pair of cells, where a <see cref="DijkstraField"/> would cost the whole
    /// reachable area to answer it.
    /// </summary>
    /// <remarks>
    /// <para><b>Exact:</b> the octile distance never overestimates under any
    /// <see cref="ICostModel"/> (models only add to the base step cost), so it is admissible,
    /// and it is consistent, so the target's cost is final the moment it is popped. The answer
    /// equals <see cref="DijkstraField.Cost"/> from either end.</para>
    /// <para><b>Bound:</b> with <c>maxCost</c>, a cell whose cost so far plus its octile
    /// distance to the target already exceeds the bound is never queued (the heuristic is a
    /// lower bound, so no route through it can come in under the bound). A target that is far
    /// away or cut off then costs only the area inside the bound to rule out.</para>
    /// <para>Neighbours come from the grid's shared <see cref="GridAdjacency"/>; per-cell
    /// arrays are allocated once per grid size and reset by stamping, so a query allocates
    /// nothing.</para>
    /// </remarks>
    public sealed class OneToOneCost
    {
        static readonly ProfilerMarker Marker = new ProfilerMarker("AI.OneToOneCost.Compute");

        readonly GridGraph _grid;
        readonly GridAdjacency _adjacency;
        readonly Stopwatch _stopwatch = new Stopwatch();

        BinaryHeap _open;
        float[] _cost;
        int[] _seenStamp;
        int[] _closedStamp;
        int _stamp;
        int _cellCount;

        /// <summary>Cells expanded by the last query, for tests and the performance log.</summary>
        public int NodesExpanded { get; private set; }

        /// <summary>Wall-clock time of the last query, in milliseconds.</summary>
        public float ElapsedMs { get; private set; }

        public OneToOneCost(GridGraph grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _adjacency = GridAdjacency.For(grid);
            AllocateForGridSize();
        }

        /// <summary>
        /// Shortest-path cost from <paramref name="from"/> to <paramref name="to"/> in grid
        /// units, or <see cref="float.PositiveInfinity"/> if either cell is blocked or off the
        /// grid, the target cannot be reached, or every route costs more than
        /// <paramref name="maxCost"/>.
        /// </summary>
        public float Compute(Vector2Int from, Vector2Int to, ICostModel cost, float maxCost = float.PositiveInfinity,
            int maxExpanded = int.MaxValue)
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

                float result = Search(from, to, cost, maxCost, maxExpanded, out int expanded);
                NodesExpanded = expanded;
                ElapsedMs = (float)_stopwatch.Elapsed.TotalMilliseconds;
                return result;
            }
        }

        float Search(Vector2Int from, Vector2Int to, ICostModel cost, float maxCost, int maxExpanded, out int expanded)
        {
            expanded = 0;
            if (!_grid.IsTraversable(from) || !_grid.IsTraversable(to))
                return float.PositiveInfinity;
            if (from == to)
                return 0f;
            if (BaseCostModel.OctileDistance(from, to) > maxCost)
                return float.PositiveInfinity;

            NextStamp();
            _open.Clear();
            _adjacency.GetArrays(out int[] counts, out int[] neighbours, out float[] steps);
            bool baseCost = cost is BaseCostModel;
            int width = _grid.Width;
            int target = _grid.ToIndex(to);

            int start = _grid.ToIndex(from);
            _cost[start] = 0f;
            _seenStamp[start] = _stamp;
            _open.Push(start, BaseCostModel.OctileDistance(from, to));

            while (!_open.IsEmpty)
            {
                int current = _open.Pop();
                if (current == target)
                    return _cost[current];
                _closedStamp[current] = _stamp;
                expanded++;

                // Out of effort: the caller wanted an answer within this many cells, or none.
                if (expanded > maxExpanded)
                    return float.PositiveInfinity;

                float currentCost = _cost[current];
                int firstSlot = current * GridAdjacency.Slots;
                int lastSlot = firstSlot + counts[current];
                for (int slot = firstSlot; slot < lastSlot; slot++)
                {
                    int nextIndex = neighbours[slot];
                    if (_closedStamp[nextIndex] == _stamp)
                        continue;

                    var nextCell = new Vector2Int(nextIndex % width, nextIndex / width);
                    float step = baseCost
                        ? steps[slot]
                        : cost.StepCost(new Vector2Int(current % width, current / width), nextCell);
                    float newCost = currentCost + step;
                    bool firstVisit = _seenStamp[nextIndex] != _stamp;
                    if (!firstVisit && newCost >= _cost[nextIndex])
                        continue;

                    float priority = newCost + BaseCostModel.OctileDistance(nextCell, to);
                    if (priority > maxCost)
                        continue; // no route through here can come in under the bound

                    _cost[nextIndex] = newCost;
                    if (firstVisit)
                    {
                        _seenStamp[nextIndex] = _stamp;
                        _open.Push(nextIndex, priority);
                    }
                    else
                    {
                        _open.DecreaseKey(nextIndex, priority);
                    }
                }
            }
            return float.PositiveInfinity;
        }

        void AllocateForGridSize()
        {
            _cellCount = _grid.CellCount;
            _open = new BinaryHeap(_cellCount);
            _cost = new float[_cellCount];
            _seenStamp = new int[_cellCount];
            _closedStamp = new int[_cellCount];
            _stamp = 0;
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
