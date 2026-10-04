using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using ToyFactory.AI.Core.Collections;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Tracker
{
    /// <summary>
    /// The Tracker's pathfinder: Greedy Best-First Search on the shared grid. It always
    /// expands the open cell that looks closest to the goal, f(n) = h(n), the octile
    /// distance, ignoring the cost already travelled.
    /// </summary>
    /// <remarks>
    /// <para><b>Why GBFS for the Tracker:</b> it commits straight towards the target, so it
    /// expands far fewer cells than A* in open rooms. That matters for a chaser that replans
    /// every 0.5 s, and its slightly wandering routes around obstacles read as a toy
    /// sniffing its way towards a sound.</para>
    /// <para><b>Not optimal:</b> without g(n) it cannot tell a long detour from a short one,
    /// so around obstacles the route can be longer than A*'s.</para>
    /// <para><b>Complete:</b> a cell is marked when it is first pushed and never pushed
    /// again, so every cell is expanded at most once (the closed set). On a finite grid the
    /// search must therefore either reach the goal or run out of cells; it cannot loop.</para>
    /// <para>Arrays are allocated once per grid size and reused with a stamp, so a search
    /// allocates nothing except the returned path list.</para>
    /// </remarks>
    public sealed class GreedyBestFirstSearch : IPathfinder
    {
        static readonly ProfilerMarker Marker = new ProfilerMarker("AI.Tracker.GBFS");

        readonly GridGraph _grid;
        readonly Vector2Int[] _neighbourBuffer = new Vector2Int[8];
        readonly Stopwatch _stopwatch = new Stopwatch();

        BinaryHeap _open;
        int[] _parent;
        // The closed set (and the open list): a cell whose stamp equals the current search's
        // stamp has already been pushed, so it is never pushed or expanded again.
        int[] _visitedStamp;
        int _stamp;
        int _cellCount;

        public GreedyBestFirstSearch(GridGraph grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            AllocateForGridSize();
        }

        /// <summary>
        /// Finds a route from <paramref name="start"/> to <paramref name="goal"/>. The cost model
        /// is required by <see cref="IPathfinder"/> but not used for ordering: greedy search
        /// ranks cells by the heuristic alone, which is what makes it fast and non-optimal.
        /// </summary>
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

                int startIndex = _grid.ToIndex(start);
                int goalIndex = _grid.ToIndex(goal);
                _parent[startIndex] = -1;
                _visitedStamp[startIndex] = _stamp;
                _open.Push(startIndex, BaseCostModel.OctileDistance(start, goal));

                int expanded = 0;
                while (!_open.IsEmpty)
                {
                    int current = _open.Pop();
                    expanded++;

                    if (current == goalIndex)
                        return new PathResult(BuildPath(goalIndex), true, expanded, ElapsedMs(), version);

                    Vector2Int currentCell = _grid.FromIndex(current);
                    int neighbourCount = _grid.GetNeighboursNonAlloc(currentCell, _neighbourBuffer);

                    for (int i = 0; i < neighbourCount; i++)
                    {
                        Vector2Int next = _neighbourBuffer[i];
                        int nextIndex = _grid.ToIndex(next);

                        // h(n) never changes, so a cell's first priority is final: there is
                        // nothing to improve with DecreaseKey, and pushing it once is enough.
                        if (_visitedStamp[nextIndex] == _stamp)
                            continue;

                        _visitedStamp[nextIndex] = _stamp;
                        _parent[nextIndex] = current;
                        _open.Push(nextIndex, BaseCostModel.OctileDistance(next, goal));
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
            _parent = new int[_cellCount];
            _visitedStamp = new int[_cellCount];
            _stamp = 0;
        }

        void NextStamp()
        {
            if (_stamp == int.MaxValue)
            {
                Array.Clear(_visitedStamp, 0, _visitedStamp.Length);
                _stamp = 0;
            }
            _stamp++;
        }

        float ElapsedMs() => (float)_stopwatch.Elapsed.TotalMilliseconds;
    }
}
