using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Collections;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>
    /// A* for the Captain that may go through closed doors, because it can open them. A
    /// closed door costs extra to step into, the time it takes to stop, open it and wait for
    /// it to swing open, so a route through a door is only taken when walking round is longer
    /// (or impossible). Walls and boxes still block.
    /// </summary>
    /// <remarks>
    /// <para><b>Cost.</b> Each step costs the octile base cost (1 straight, √2 diagonal), plus
    /// <see cref="DoorPenalty"/> on entering a closed door from outside it: once per door, not
    /// once per door cell. The penalty only adds to the base cost, so the octile heuristic
    /// stays admissible and A* still returns the cheapest route.</para>
    /// <para><b>Why it exists.</b> The shared A* and the distance fields treat a closed door as a
    /// wall, which is right for every agent that cannot open doors. The Captain runs this beside
    /// the shared A* for each new route and takes it only when it crosses a closed door and is
    /// cheaper (or the only way).</para>
    /// <para>Arrays are allocated once per grid size and reused; a search allocates only the
    /// returned route.</para>
    /// </remarks>
    public sealed class DoorRouter
    {
        /// <summary>
        /// Extra cost (grid units) of going through a closed door: about 1.2 s to open it and
        /// step through, at the Captain's 4.6 m/s, is 5.5 m, or 11 cells of 0.5 m.
        /// </summary>
        public const float DoorPenalty = 11f;

        readonly GridGraph _grid;
        readonly Vector2Int[] _neighbours = new Vector2Int[8];
        BinaryHeap _open;
        float[] _cost;
        int[] _parent;
        int[] _stamp;
        int _search;

        /// <summary>Cells expanded by the last search, for the performance log.</summary>
        public int NodesExpanded { get; private set; }

        /// <summary>Cost (grid units, door penalties included) of the route the last search returned.</summary>
        public float LastCost { get; private set; }

        public DoorRouter(GridGraph grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        /// <summary>
        /// The cheapest route from <paramref name="start"/> to <paramref name="goal"/> (both
        /// included), going through closed doors when that is cheaper or the only way. Doors
        /// for which <paramref name="isDoorBlocked"/> returns true are treated as walls (one the
        /// Captain failed to open). Null if there is no route.
        /// </summary>
        public List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal, Predicate<int> isDoorBlocked = null)
        {
            NodesExpanded = 0;
            if (!_grid.Contains(start) || !_grid.Contains(goal) || !Enterable(start, isDoorBlocked) || !Enterable(goal, isDoorBlocked))
                return null;

            Prepare();
            int width = _grid.Width;
            int startIndex = start.y * width + start.x;
            int goalIndex = goal.y * width + goal.x;
            Visit(startIndex, 0f, -1);
            _open.Push(startIndex, BaseCostModel.OctileDistance(start, goal));

            while (!_open.IsEmpty)
            {
                int current = _open.Pop();
                NodesExpanded++;
                if (current == goalIndex)
                    return Build(goalIndex);

                var cell = new Vector2Int(current % width, current / width);
                GridNode node = _grid.GetNode(cell);
                int count = _grid.GetNeighboursNonAlloc(cell, _neighbours, allowClosedDoors: true);
                for (int i = 0; i < count; i++)
                {
                    Vector2Int next = _neighbours[i];
                    GridNode nextNode = _grid.GetNode(next);
                    if (!Enterable(next, isDoorBlocked))
                        continue;
                    float step = BaseCostModel.Instance.StepCost(cell, next);
                    // Entering a closed door from outside it: pay once for opening it.
                    if (nextNode.IsDoorClosed && !(node.IsDoorClosed && node.DoorId == nextNode.DoorId))
                        step += DoorPenalty;

                    int index = next.y * width + next.x;
                    float cost = _cost[current] + step;
                    if (_stamp[index] == _search && cost >= _cost[index])
                        continue;
                    bool known = _stamp[index] == _search && _open.Contains(index);
                    Visit(index, cost, current);
                    float priority = cost + BaseCostModel.OctileDistance(next, goal);
                    if (known)
                        _open.DecreaseKey(index, priority);
                    else
                        _open.Push(index, priority);
                }
            }
            return null;
        }

        // A closed door counts as open unless the Captain has given up on it.
        bool Enterable(Vector2Int cell, Predicate<int> isDoorBlocked)
        {
            GridNode node = _grid.GetNode(cell);
            if (!node.IsSoundTraversable)
                return false;
            return !(node.IsDoorClosed && node.DoorId.HasValue && isDoorBlocked != null && isDoorBlocked(node.DoorId.Value));
        }

        void Visit(int index, float cost, int parent)
        {
            _stamp[index] = _search;
            _cost[index] = cost;
            _parent[index] = parent;
        }

        List<Vector2Int> Build(int goalIndex)
        {
            LastCost = _cost[goalIndex];
            int width = _grid.Width;
            var route = new List<Vector2Int>();
            for (int index = goalIndex; index >= 0; index = _parent[index])
                route.Add(new Vector2Int(index % width, index / width));
            route.Reverse();
            return route;
        }

        // Stamp trick: a new search number invalidates every cell's cost without clearing arrays.
        void Prepare()
        {
            int cells = _grid.CellCount;
            if (_cost == null || _cost.Length != cells)
            {
                _cost = new float[cells];
                _parent = new int[cells];
                _stamp = new int[cells];
                _open = new BinaryHeap(cells);
                _search = 0;
            }
            _open.Clear();
            _search++;
        }
    }
}
