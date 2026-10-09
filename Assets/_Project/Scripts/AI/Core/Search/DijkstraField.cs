using System;
using System.Collections.Generic;
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
    /// Neighbours are read from the grid's shared <see cref="GridAdjacency"/> table.
    /// </remarks>
    public sealed class DijkstraField
    {
        static readonly ProfilerMarker Marker = new ProfilerMarker("AI.DijkstraField.Compute");
        static readonly ProfilerMarker RepairMarker = new ProfilerMarker("AI.DijkstraField.Repair");

        // Two costs closer than this are equal. Costs are a + b*sqrt(2) for whole a and b, and
        // on this grid two different such costs differ by at least about 0.002, so the
        // tolerance only absorbs float rounding. A rounding error larger than this makes a
        // cell look unsupported, which only repairs more cells than needed, never fewer.
        const float SameCost = 5e-4f;

        readonly GridGraph _grid;
        readonly GridAdjacency _adjacency;
        readonly Stopwatch _stopwatch = new Stopwatch();

        BinaryHeap _open;
        float[] _cost;
        int[] _seenStamp;
        int[] _closedStamp;
        int _stamp;
        int _cellCount;
        ICostModel _costModel;

        // Repair state: a mark counts when it equals _repairStamp.
        int[] _affectedMark;
        int[] _invalidMark;
        int _repairStamp;
        readonly List<int> _affected = new List<int>(64);
        readonly List<int> _invalid = new List<int>(256);
        readonly List<int> _pending = new List<int>(256);

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

        /// <summary>Cells expanded by the last compute or repair, for the performance log.</summary>
        public int NodesExpanded { get; private set; }

        /// <summary>True if the last <see cref="Refresh"/> repaired the field in place rather than computing it again.</summary>
        public bool LastRefreshWasRepair { get; private set; }

        /// <summary>Wall-clock time of the last compute, in milliseconds.</summary>
        public float ElapsedMs { get; private set; }

        public DijkstraField(GridGraph grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _adjacency = GridAdjacency.For(grid);
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
                _costModel = cost;
                GraphVersion = _grid.Version;
                HasBeenComputed = true;

                int expanded = 0;
                if (_grid.IsTraversable(source))
                {
                    int sourceIndex = _grid.ToIndex(source);
                    _cost[sourceIndex] = 0f;
                    _seenStamp[sourceIndex] = _stamp;
                    _open.Push(sourceIndex, 0f);

                    // Neighbours come from the shared table (GridAdjacency), not from the
                    // grid: same cells, same order, without re-checking every expansion.
                    // With the base cost model the step cost is the table's too; any other
                    // model is still asked about each step.
                    _adjacency.GetArrays(out int[] counts, out int[] neighbours, out float[] steps);
                    bool baseCost = cost is BaseCostModel;
                    int width = _grid.Width;

                    while (!_open.IsEmpty)
                    {
                        // Popped in increasing cost order, so this cell's cost is now final.
                        int current = _open.Pop();
                        _closedStamp[current] = _stamp;
                        expanded++;

                        float currentCost = _cost[current];
                        int firstSlot = current * GridAdjacency.Slots;
                        int lastSlot = firstSlot + counts[current];

                        for (int slot = firstSlot; slot < lastSlot; slot++)
                        {
                            int nextIndex = neighbours[slot];
                            if (_closedStamp[nextIndex] == _stamp)
                                continue;

                            float step = baseCost
                                ? steps[slot]
                                : cost.StepCost(new Vector2Int(current % width, current / width),
                                    new Vector2Int(nextIndex % width, nextIndex / width));
                            float newCost = currentCost + step;

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
        /// Brings a stale field up to date with the grid, keeping its source, cost model and
        /// bound. An unbounded base-cost field is repaired in place, touching only the cells
        /// whose cost the grid changes can have altered; any other field is computed again.
        /// Does nothing if the field is not stale.
        /// </summary>
        /// <remarks>
        /// <para><b>Why:</b> a pushed box changes a few cells, and every goal field the Captain
        /// holds goes stale. Computing each one again searches the whole level (about 4,800
        /// cells) to correct the costs of the few cells behind the box.</para>
        /// <para><b>How</b> (the idea behind LPA* and D* Lite): every changed edge has both ends
        /// among the changes' <see cref="GridChange.AffectedCells"/>.</para>
        /// <list type="number">
        /// <item><b>Invalidate.</b> A reached cell keeps its cost only while some still-valid
        /// neighbour supports it exactly: cost(u) + step(u, v) = cost(v). An affected cell with
        /// no such neighbour (it was blocked, or its route ran through a blocked cell) loses its
        /// cost, and so, in turn, does every cell that relied on it alone. Support always comes
        /// from a cheaper cell, so this cannot go round in circles.</item>
        /// <item><b>Search again from the edge of the damage.</b> Each invalidated cell restarts
        /// from its cheapest valid neighbour, every valid affected cell offers its neighbours a
        /// step across any new edge (a box that moved away, a door that opened), and Dijkstra
        /// runs from there. A valid cell is reopened only if it gets strictly cheaper.</item>
        /// </list>
        /// <para>The result equals a fresh <see cref="Compute"/>; tests check this on random grids
        /// and changes. Computes again instead if the grid's change log no longer reaches back
        /// to this field's version, or the source itself was blocked.</para>
        /// </remarks>
        public void Refresh()
        {
            if (!HasBeenComputed)
                throw new InvalidOperationException("Compute the field once before refreshing it.");
            if (!IsStale)
                return;

            bool repaired;
            using (RepairMarker.Auto())
                repaired = TryRepair();
            LastRefreshWasRepair = repaired;
            if (!repaired)
                Compute(Source, _costModel, MaxCost);
        }

        bool TryRepair()
        {
            if (!(_costModel is BaseCostModel) || !float.IsPositiveInfinity(MaxCost))
                return false;
            if (_grid.CellCount != _cellCount || !_grid.IsTraversable(Source))
                return false;
            int sourceIndex = _grid.ToIndex(Source);
            if (_closedStamp[sourceIndex] != _stamp)
                return false;
            if (_grid.Version - GraphVersion > GridAdjacency.ChangeLogSize)
                return false;

            _stopwatch.Restart();
            NextRepairStamp();
            _affected.Clear();
            for (int version = GraphVersion + 1; version <= _grid.Version; version++)
            {
                if (!_adjacency.TryGetChange(version, out GridChange change))
                    return false;
                for (int i = 0; i < change.AffectedCells.Count; i++)
                {
                    int index = _grid.ToIndex(change.AffectedCells[i]);
                    if (_affectedMark[index] == _repairStamp)
                        continue;
                    _affectedMark[index] = _repairStamp;
                    _affected.Add(index);
                }
            }

            _adjacency.GetArrays(out int[] counts, out int[] neighbours, out float[] steps);
            int width = _grid.Width;
            int height = _grid.Height;

            // 1. Invalidate the cells that lost the route their cost came from.
            _invalid.Clear();
            _pending.Clear();
            for (int i = 0; i < _affected.Count; i++)
            {
                int cell = _affected[i];
                if (cell != sourceIndex && _closedStamp[cell] == _stamp && !HasSupport(cell, counts, neighbours, steps))
                    Invalidate(cell);
            }
            while (_pending.Count > 0)
            {
                int cell = _pending[_pending.Count - 1];
                _pending.RemoveAt(_pending.Count - 1);
                // Any of the eight cells round it may have relied on it. The geometric
                // neighbourhood, not the table: a cell that was blocked has no table entries.
                int x = cell % width;
                int y = cell / width;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || x + dx < 0 || x + dx >= width || y + dy < 0 || y + dy >= height)
                        continue;
                    int next = cell + dy * width + dx;
                    if (next == sourceIndex || _closedStamp[next] != _stamp || _invalidMark[next] == _repairStamp)
                        continue;
                    float step = dx != 0 && dy != 0 ? BaseCostModel.DiagonalCost : BaseCostModel.StraightCost;
                    if (Math.Abs(_cost[cell] + step - _cost[next]) > SameCost)
                        continue; // its cost never came through this cell
                    if (!HasSupport(next, counts, neighbours, steps))
                        Invalidate(next);
                }
            }

            // 2. Search again from the edge of the damage and across new edges.
            _open.Clear();
            for (int i = 0; i < _invalid.Count; i++)
            {
                int cell = _invalid[i];
                _closedStamp[cell] = 0;
                _seenStamp[cell] = 0;
            }
            for (int i = 0; i < _invalid.Count; i++)
            {
                int cell = _invalid[i];
                float best = float.PositiveInfinity;
                int first = cell * GridAdjacency.Slots;
                int last = first + counts[cell];
                for (int slot = first; slot < last; slot++)
                {
                    int from = neighbours[slot];
                    if (_closedStamp[from] == _stamp)
                        best = Math.Min(best, _cost[from] + steps[slot]);
                }
                if (!float.IsPositiveInfinity(best))
                {
                    _cost[cell] = best;
                    _seenStamp[cell] = _stamp;
                    _open.Push(cell, best);
                }
            }
            for (int i = 0; i < _affected.Count; i++)
            {
                int cell = _affected[i];
                if (_closedStamp[cell] == _stamp)
                    Relax(cell, counts, neighbours, steps);
            }

            int expanded = _invalid.Count;
            while (!_open.IsEmpty)
            {
                int current = _open.Pop();
                _closedStamp[current] = _stamp;
                expanded++;
                Relax(current, counts, neighbours, steps);
            }

            GraphVersion = _grid.Version;
            NodesExpanded = expanded;
            ElapsedMs = (float)_stopwatch.Elapsed.TotalMilliseconds;
            return true;
        }

        // True if a reached, still-valid neighbour gives this cell exactly its cost.
        bool HasSupport(int cell, int[] counts, int[] neighbours, float[] steps)
        {
            int first = cell * GridAdjacency.Slots;
            int last = first + counts[cell];
            for (int slot = first; slot < last; slot++)
            {
                int from = neighbours[slot];
                if (_closedStamp[from] == _stamp && _invalidMark[from] != _repairStamp &&
                    Math.Abs(_cost[from] + steps[slot] - _cost[cell]) <= SameCost)
                    return true;
            }
            return false;
        }

        void Invalidate(int cell)
        {
            _invalidMark[cell] = _repairStamp;
            _invalid.Add(cell);
            _pending.Add(cell);
        }

        // Offers each neighbour the step from this cell: queued if not reached yet, lowered if
        // already queued, and reopened if already final but now strictly cheaper.
        void Relax(int cell, int[] counts, int[] neighbours, float[] steps)
        {
            float cellCost = _cost[cell];
            int first = cell * GridAdjacency.Slots;
            int last = first + counts[cell];
            for (int slot = first; slot < last; slot++)
            {
                int next = neighbours[slot];
                float newCost = cellCost + steps[slot];
                if (_closedStamp[next] == _stamp)
                {
                    if (newCost >= _cost[next] - SameCost)
                        continue;
                    _closedStamp[next] = 0;
                    _cost[next] = newCost;
                    _seenStamp[next] = _stamp;
                    _open.Push(next, newCost);
                }
                else if (_open.Contains(next))
                {
                    if (newCost < _cost[next])
                    {
                        _cost[next] = newCost;
                        _open.DecreaseKey(next, newCost);
                    }
                }
                else
                {
                    _cost[next] = newCost;
                    _seenStamp[next] = _stamp;
                    _open.Push(next, newCost);
                }
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
            _affectedMark = new int[_cellCount];
            _invalidMark = new int[_cellCount];
            _repairStamp = 0;
            HasBeenComputed = false;
        }

        void NextRepairStamp()
        {
            if (_repairStamp == int.MaxValue)
            {
                Array.Clear(_affectedMark, 0, _affectedMark.Length);
                Array.Clear(_invalidMark, 0, _invalidMark.Length);
                _repairStamp = 0;
            }
            _repairStamp++;
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
