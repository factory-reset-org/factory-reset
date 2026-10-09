using System;
using UnityEngine;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Agents.Guard
{
    /// <summary>
    /// Remembers line-of-sight answers so each cell is only tested once while nothing has
    /// changed. One cover decision asks about the same cells many times over (a cell's own
    /// protection, its neighbours' peek checks, every step of the tactical A*), and in the
    /// game each fresh answer is a physics raycast.
    /// </summary>
    /// <remarks>
    /// The owner calls <see cref="Invalidate"/> when the answers can have changed: the player
    /// moved to another cell, or the grid changed. Only the two heights cover is judged at
    /// are remembered; any other height goes straight to the inner check. No allocation
    /// after the first query on a grid.
    /// </remarks>
    public sealed class CachedCoverVisibility : ICoverVisibility
    {
        readonly ICoverVisibility _inner;
        readonly GridGraph _grid;

        bool[] _low, _chest;
        int[] _lowStamp, _chestStamp;
        int _stamp = 1;
        int _cellCount;

        public CachedCoverVisibility(ICoverVisibility inner, GridGraph grid)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        /// <summary>Queries answered so far, remembered or not.</summary>
        public int Queries { get; private set; }

        /// <summary>Queries that had to be passed to the inner check (in the game, raycasts).</summary>
        public int InnerQueries { get; private set; }

        /// <summary>Forgets every remembered answer.</summary>
        public void Invalidate()
        {
            if (_stamp == int.MaxValue)
            {
                if (_lowStamp != null)
                {
                    Array.Clear(_lowStamp, 0, _lowStamp.Length);
                    Array.Clear(_chestStamp, 0, _chestStamp.Length);
                }
                _stamp = 0;
            }
            _stamp++;
        }

        public bool IsBlocked(Vector2Int cell, float height)
        {
            Queries++;
            if (!_grid.Contains(cell))
                return Ask(cell, height);

            if (_grid.CellCount != _cellCount)
                Allocate();

            if (height == CoverEvaluator.LowCoverHeight)
                return Remembered(cell, height, _low, _lowStamp);
            if (height == CoverEvaluator.ChestHeight)
                return Remembered(cell, height, _chest, _chestStamp);
            return Ask(cell, height);
        }

        bool Remembered(Vector2Int cell, float height, bool[] answers, int[] stamps)
        {
            int index = _grid.ToIndex(cell);
            if (stamps[index] != _stamp)
            {
                answers[index] = Ask(cell, height);
                stamps[index] = _stamp;
            }
            return answers[index];
        }

        bool Ask(Vector2Int cell, float height)
        {
            InnerQueries++;
            return _inner.IsBlocked(cell, height);
        }

        void Allocate()
        {
            _cellCount = _grid.CellCount;
            _low = new bool[_cellCount];
            _chest = new bool[_cellCount];
            _lowStamp = new int[_cellCount];
            _chestStamp = new int[_cellCount];
        }
    }
}
