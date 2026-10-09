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
    /// Optimal one-to-one pathfinding on the shared grid: expands cells in order of
    /// f = g + h, where g is the cost so far under the given <see cref="ICostModel"/> and h
    /// is the octile distance to the goal. Used for movement by any agent that needs the
    /// cheapest route, with the base cost model or a tactical one.
    /// </summary>
    /// <remarks>
    /// All per-cell arrays are allocated once per grid size and reused. Instead of clearing
    /// them before every search, each search bumps a stamp and a cell only counts as
    /// visited or closed if its stored stamp matches the current one. Neighbours are read
    /// through <see cref="GridGraph.GetNeighboursNonAlloc"/> into a fixed 8-slot buffer, so a
    /// search allocates nothing on the heap.
    /// </remarks>
    public sealed class AStarSearch : IPathfinder
    {
        static readonly ProfilerMarker Marker = new ProfilerMarker("AI.AStarSearch.FindPath");

        readonly GridGraph _grid;
        readonly GridAdjacency _adjacency;
        readonly Stopwatch _stopwatch = new Stopwatch();

        BinaryHeap _open;
        float[] _costSoFar;
        int[] _parent;
        int[] _seenStamp;
        int[] _closedStamp;
        int _stamp;
        int _cellCount;

        public AStarSearch(GridGraph grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _adjacency = GridAdjacency.For(grid);
            AllocateForGridSize();
        }

        public PathResult FindPath(Vector2Int start, Vector2Int goal, ICostModel cost)
        {
            if (cost == null)
                throw new ArgumentNullException(nameof(cost));

            using (Marker.Auto())
            {
                _stopwatch.Restart();
                if (_grid.CellCount != _cellCount)
                    AllocateForGridSize();

                int version = _grid.Version;
                if (!_grid.IsTraversable(start) || !_grid.IsTraversable(goal))
                    return PathResult.NotFound(0, ElapsedMs(), version);

                NextStamp();
                _open.Clear();

                // The shared neighbour table: each cell's neighbours and base step costs are
                // read from arrays instead of being worked out again for every expansion.
                _adjacency.GetArrays(out int[] counts, out int[] neighbours, out float[] steps);
                bool baseCost = cost is BaseCostModel;
                int width = _grid.Width;

                int startIndex = _grid.ToIndex(start);
                int goalIndex = _grid.ToIndex(goal);
                _costSoFar[startIndex] = 0f;
                _parent[startIndex] = -1;
                _seenStamp[startIndex] = _stamp;
                _open.Push(startIndex, BaseCostModel.OctileDistance(start, goal));

                int expanded = 0;
                while (!_open.IsEmpty)
                {
                    int current = _open.Pop();
                    _closedStamp[current] = _stamp;
                    expanded++;

                    if (current == goalIndex)
                        return new PathResult(BuildPath(goalIndex), true, expanded, ElapsedMs(), version);

                    var currentCell = new Vector2Int(current % width, current / width);
                    int firstSlot = current * GridAdjacency.Slots;
                    int lastSlot = firstSlot + counts[current];

                    for (int slot = firstSlot; slot < lastSlot; slot++)
                    {
                        int nextIndex = neighbours[slot];

                        // The heuristic is consistent, so a closed cell can never be improved.
                        if (_closedStamp[nextIndex] == _stamp)
                            continue;

                        var next = new Vector2Int(nextIndex % width, nextIndex / width);
                        float step = baseCost ? steps[slot] : cost.StepCost(currentCell, next);
                        float newCost = _costSoFar[current] + step;
                        bool firstVisit = _seenStamp[nextIndex] != _stamp;
                        if (!firstVisit && newCost >= _costSoFar[nextIndex])
                            continue;

                        _costSoFar[nextIndex] = newCost;
                        _parent[nextIndex] = current;
                        float priority = newCost + BaseCostModel.OctileDistance(next, goal);

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

                return PathResult.NotFound(expanded, ElapsedMs(), version);
            }
        }

        List<Vector2Int> BuildPath(int goalIndex)
        {
            var path = new List<Vector2Int>();
            for (int index = goalIndex; index != -1; index = _parent[index])
                path.Add(_grid.FromIndex(index));
            path.Reverse();
            return path;
        }

        void AllocateForGridSize()
        {
            _cellCount = _grid.CellCount;
            _open = new BinaryHeap(_cellCount);
            _costSoFar = new float[_cellCount];
            _parent = new int[_cellCount];
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

        float ElapsedMs() => (float)_stopwatch.Elapsed.TotalMilliseconds;
    }
}
