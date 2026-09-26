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
    /// visited or closed if its stored stamp matches the current one.
    /// </remarks>
    public sealed class AStarSearch : IPathfinder
    {
        static readonly ProfilerMarker Marker = new ProfilerMarker("AI.AStarSearch.FindPath");

        readonly IGridGraph _grid;
        readonly List<Vector2Int> _neighbours = new List<Vector2Int>(8);
        readonly Stopwatch _stopwatch = new Stopwatch();

        BinaryHeap _open;
        float[] _costSoFar;
        int[] _parent;
        int[] _seenStamp;
        int[] _closedStamp;
        int _stamp;
        int _width;

        public AStarSearch(IGridGraph grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            AllocateForGridSize();
        }

        public PathResult FindPath(Vector2Int start, Vector2Int goal, ICostModel cost)
        {
            if (cost == null)
                throw new ArgumentNullException(nameof(cost));

            using (Marker.Auto())
            {
                _stopwatch.Restart();
                if (_grid.Width * _grid.Height != _costSoFar.Length || _grid.Width != _width)
                    AllocateForGridSize();

                int version = _grid.Version;
                if (!_grid.IsTraversable(start) || !_grid.IsTraversable(goal))
                    return PathResult.NotFound(0, ElapsedMs(), version);

                NextStamp();
                _open.Clear();

                int startIndex = Index(start);
                int goalIndex = Index(goal);
                _costSoFar[startIndex] = 0f;
                _parent[startIndex] = -1;
                _seenStamp[startIndex] = _stamp;
                _open.Push(startIndex, Heuristic(start, goal));

                int expanded = 0;
                while (!_open.IsEmpty)
                {
                    int current = _open.Pop();
                    _closedStamp[current] = _stamp;
                    expanded++;

                    if (current == goalIndex)
                        return new PathResult(BuildPath(goalIndex), true, expanded, ElapsedMs(), version);

                    Vector2Int currentCell = Cell(current);
                    _grid.GetNeighbours(currentCell, _neighbours);

                    for (int i = 0; i < _neighbours.Count; i++)
                    {
                        Vector2Int next = _neighbours[i];
                        int nextIndex = Index(next);

                        // The heuristic is consistent, so a closed cell can never be improved.
                        if (_closedStamp[nextIndex] == _stamp)
                            continue;

                        float newCost = _costSoFar[current] + cost.StepCost(currentCell, next);
                        bool firstVisit = _seenStamp[nextIndex] != _stamp;
                        if (!firstVisit && newCost >= _costSoFar[nextIndex])
                            continue;

                        _costSoFar[nextIndex] = newCost;
                        _parent[nextIndex] = current;
                        float priority = newCost + Heuristic(next, goal);

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

        // Octile distance: exact cost of the cheapest route on an open 8-connected grid with
        // the base step costs. Cost models only ever add to those costs, so this never
        // overestimates (admissible) and obeys the triangle inequality (consistent).
        static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            return Mathf.Abs(dx - dy) + Mathf.Min(dx, dy) * BaseCostModel.DiagonalCost;
        }

        List<Vector2Int> BuildPath(int goalIndex)
        {
            var path = new List<Vector2Int>();
            for (int index = goalIndex; index != -1; index = _parent[index])
                path.Add(Cell(index));
            path.Reverse();
            return path;
        }

        void AllocateForGridSize()
        {
            _width = _grid.Width;
            int cellCount = _grid.Width * _grid.Height;
            _open = new BinaryHeap(cellCount);
            _costSoFar = new float[cellCount];
            _parent = new int[cellCount];
            _seenStamp = new int[cellCount];
            _closedStamp = new int[cellCount];
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

        int Index(Vector2Int cell) => cell.y * _width + cell.x;

        Vector2Int Cell(int index) => new Vector2Int(index % _width, index / _width);

        float ElapsedMs() => (float)_stopwatch.Elapsed.TotalMilliseconds;
    }
}
