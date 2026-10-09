using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// Which cells of a grid can reach which: every traversable cell carries the number of
    /// its connected area, so "can I get from A to B at all?" is two array reads instead of a
    /// search that floods the level before it can say no.
    /// </summary>
    /// <remarks>
    /// The areas are worked out with one flood over the shared neighbour table, so they follow
    /// the same movement rules as every search (a closed door splits two rooms). They are
    /// rebuilt lazily, on the first question after the grid changes. One instance per grid,
    /// shared by everything that asks; no allocation after it is built.
    /// </remarks>
    public sealed class GridRegions
    {
        static readonly ConditionalWeakTable<GridGraph, GridRegions> Tables =
            new ConditionalWeakTable<GridGraph, GridRegions>();

        readonly GridGraph _grid;
        readonly GridAdjacency _adjacency;
        readonly int[] _region;   // 0 = not traversable, otherwise the area's number
        readonly int[] _queue;
        int _version = -1;

        /// <summary>The regions of <paramref name="grid"/>, built the first time they are asked for.</summary>
        public static GridRegions For(GridGraph grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            return Tables.GetValue(grid, g => new GridRegions(g));
        }

        GridRegions(GridGraph grid)
        {
            _grid = grid;
            _adjacency = GridAdjacency.For(grid);
            _region = new int[grid.CellCount];
            _queue = new int[grid.CellCount];
        }

        /// <summary>Number of separate areas. Rebuilds first if the grid has changed.</summary>
        public int Count
        {
            get
            {
                Sync();
                return _count;
            }
        }

        int _count;

        /// <summary>How often the areas have been worked out, for tests and the performance log.</summary>
        public int Rebuilds { get; private set; }

        /// <summary>True if a walk from <paramref name="a"/> to <paramref name="b"/> exists. False if either cell is not traversable.</summary>
        public bool Connected(Vector2Int a, Vector2Int b)
        {
            if (!_grid.Contains(a) || !_grid.Contains(b))
                return false;
            Sync();
            int regionA = _region[_grid.ToIndex(a)];
            return regionA != 0 && regionA == _region[_grid.ToIndex(b)];
        }

        void Sync()
        {
            if (_version != _grid.Version)
                Rebuild();
        }

        void Rebuild()
        {
            Rebuilds++;
            _adjacency.GetArrays(out int[] counts, out int[] neighbours, out _);
            Array.Clear(_region, 0, _region.Length);
            _count = 0;

            for (int seed = 0; seed < _region.Length; seed++)
            {
                if (_region[seed] != 0 || !_grid.IsTraversable(_grid.FromIndex(seed)))
                    continue;

                int number = ++_count;
                int head = 0, tail = 0;
                _region[seed] = number;
                _queue[tail++] = seed;
                while (head < tail)
                {
                    int current = _queue[head++];
                    int firstSlot = current * GridAdjacency.Slots;
                    int lastSlot = firstSlot + counts[current];
                    for (int slot = firstSlot; slot < lastSlot; slot++)
                    {
                        int next = neighbours[slot];
                        if (_region[next] != 0)
                            continue;
                        _region[next] = number;
                        _queue[tail++] = next;
                    }
                }
            }
            _version = _grid.Version;
        }
    }
}
