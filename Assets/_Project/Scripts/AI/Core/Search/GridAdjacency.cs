using System;
using System.Runtime.CompilerServices;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// Every walkable cell's movement neighbours, worked out once and kept in flat arrays:
    /// for cell index i, slots i*8 to i*8 + Count(i) - 1 hold each neighbour's cell index and
    /// its base step cost (1 or sqrt(2)). A search reads these instead of asking the grid for
    /// neighbours at every expansion. One table per grid, shared by every search that asks
    /// for it with <see cref="For"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Why:</b> profiling a full <see cref="DijkstraField"/> on the level grid showed
    /// that about 84% of its time went on <see cref="GridGraph.GetNeighboursNonAlloc"/>:
    /// up to eight bounds checks, node reads and diagonal clearance checks per expansion, plus
    /// cell/index conversions. Those answers only change when the grid does, so they are
    /// stored instead of recomputed.</para>
    /// <para><b>Kept current:</b> the table listens to <see cref="GridGraph.Changed"/> and
    /// refreshes only <see cref="GridChange.AffectedCells"/>. That is enough because a cell's
    /// neighbour list depends only on cells at most one step away (the target and, for a
    /// diagonal, its two side cells), and the affected cells are exactly the changed cells and
    /// their neighbours. A box pushed one cell refreshes about a dozen cells, not the grid.</para>
    /// <para>The same neighbours, in the same order, as
    /// <see cref="GridGraph.GetNeighboursNonAlloc"/> with closed doors blocking: it is built
    /// from that method, so the corner-cutting and door rules cannot drift apart.</para>
    /// </remarks>
    public sealed class GridAdjacency
    {
        /// <summary>Neighbour slots per cell (eight-connected).</summary>
        public const int Slots = 8;

        /// <summary>How many recent grid changes are kept for <see cref="TryGetChange"/>.</summary>
        public const int ChangeLogSize = 64;

        static readonly ConditionalWeakTable<GridGraph, GridAdjacency> Tables =
            new ConditionalWeakTable<GridGraph, GridAdjacency>();

        readonly GridGraph _grid;
        readonly int[] _count;
        readonly int[] _next;
        readonly float[] _step;
        readonly UnityEngine.Vector2Int[] _buffer = new UnityEngine.Vector2Int[Slots];
        readonly GridChange[] _log = new GridChange[ChangeLogSize];
        int _version;

        /// <summary>Cells refreshed since the table was built, for tests and the performance log.</summary>
        public int CellsRefreshed { get; private set; }

        /// <summary>The shared table for <paramref name="grid"/>, built the first time it is asked for.</summary>
        public static GridAdjacency For(GridGraph grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            return Tables.GetValue(grid, g => new GridAdjacency(g));
        }

        GridAdjacency(GridGraph grid)
        {
            _grid = grid;
            int cells = grid.CellCount;
            _count = new int[cells];
            _next = new int[cells * Slots];
            _step = new float[cells * Slots];
            RefreshAll();
            // The table lives as long as the grid (the grid's event holds it), which is what
            // the weak table wants: both go together.
            grid.Changed += OnChanged;
        }

        /// <summary>Number of neighbours of the cell at <paramref name="index"/> (0 for a blocked cell).</summary>
        public int Count(int index)
        {
            Sync();
            return _count[index];
        }

        /// <summary>
        /// The raw arrays, for search loops: neighbours of cell i are
        /// <paramref name="next"/>[i * 8 + k] with base cost <paramref name="step"/>[i * 8 + k]
        /// for k below <paramref name="count"/>[i]. Valid until the grid next changes.
        /// </summary>
        public void GetArrays(out int[] count, out int[] next, out float[] step)
        {
            Sync();
            count = _count;
            next = _next;
            step = _step;
        }

        /// <summary>
        /// The grid change that produced <paramref name="version"/>, if it is one of the last
        /// <see cref="ChangeLogSize"/>. Lets a <see cref="DijkstraField"/> repair just the
        /// cells a change touched instead of searching again from scratch.
        /// </summary>
        public bool TryGetChange(int version, out GridChange change)
        {
            change = version > 0 ? _log[version % ChangeLogSize] : null;
            if (change != null && change.Version == version)
                return true;
            change = null;
            return false;
        }

        void OnChanged(GridChange change)
        {
            for (int i = 0; i < change.AffectedCells.Count; i++)
                Refresh(_grid.ToIndex(change.AffectedCells[i]));
            _version = change.Version;
            _log[change.Version % ChangeLogSize] = change;
        }

        // A change made while nothing was listening cannot happen (the table subscribes as it
        // is built), but rebuilding is cheap insurance against a stale table.
        void Sync()
        {
            if (_version != _grid.Version)
                RefreshAll();
        }

        void RefreshAll()
        {
            for (int i = 0; i < _count.Length; i++)
                Refresh(i);
            _version = _grid.Version;
        }

        void Refresh(int index)
        {
            CellsRefreshed++;
            UnityEngine.Vector2Int cell = _grid.FromIndex(index);
            if (!_grid.IsTraversable(cell))
            {
                _count[index] = 0;
                return;
            }

            int found = _grid.GetNeighboursNonAlloc(cell, _buffer);
            int baseSlot = index * Slots;
            for (int k = 0; k < found; k++)
            {
                UnityEngine.Vector2Int next = _buffer[k];
                _next[baseSlot + k] = next.y * _grid.Width + next.x;
                _step[baseSlot + k] = next.x != cell.x && next.y != cell.y
                    ? BaseCostModel.DiagonalCost
                    : BaseCostModel.StraightCost;
            }
            _count[index] = found;
        }
    }
}
