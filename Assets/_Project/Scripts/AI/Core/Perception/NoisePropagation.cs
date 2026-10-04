using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using ToyFactory.AI.Core.Collections;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Core.Perception
{
    /// <summary>
    /// Spreads one noise through the level along the grid and reports how loud it is at any
    /// cell: <c>L(cell) = L0 - 4 * metres travelled - 35 * closed doors crossed</c>. Sound goes
    /// round corners and through closed doors (losing 35 for each), but not through walls or
    /// boxes. A cell hears the noise while its level is above the hearing threshold (10).
    /// </summary>
    /// <remarks>
    /// <para><b>Algorithm.</b> A bounded Dijkstra over the sound graph
    /// (<see cref="GridGraph.GetNeighboursNonAlloc"/> with closed doors allowed). The cost of a
    /// path is the level it loses: 4 per metre (2 per orthogonal step of 0.5 m, 2.83 per
    /// diagonal), plus 35 when the path steps into a closed door from outside that door.
    /// Charging only on entry means a door two or three cells deep still costs 35 once.
    /// Dijkstra pops cells in increasing loss, so each cell's level is the loudest the noise
    /// can reach it with, which is the shortest sound path.</para>
    /// <para><b>Bound.</b> A cell is never queued once its level would be at or below the
    /// threshold, so the work is limited to the area that can hear it: a radius of
    /// <c>(L0 - 10) / 4</c> metres in the open (22.5 m for a blaster shot, 3.75 m for footsteps).</para>
    /// <para><b>Reuse.</b> The arrays are allocated once per grid size and reset with a stamp
    /// (as in <c>DijkstraField</c>), so propagating allocates nothing.</para>
    /// <para>Pure C#: S4's runtime calls <see cref="Propagate"/> once per <c>NoiseEvents.Emit</c>
    /// and reads <see cref="Level"/> at each agent's cell.</para>
    /// </remarks>
    public sealed class NoisePropagation
    {
        /// <summary>Level lost per metre travelled (alpha).</summary>
        public const float LossPerMetre = 4f;

        /// <summary>Level lost crossing a closed door.</summary>
        public const float ClosedDoorLoss = 35f;

        /// <summary>A noise is heard only where its level is above this.</summary>
        public const float HearingThreshold = 10f;

        /// <summary>How far a noise source in a wall or box is moved to the nearest open cell (cells).</summary>
        public const int SourceSnapRadius = 2;

        const float OrthogonalLoss = LossPerMetre * GridGraph.CellSize;
        const float DiagonalLoss = LossPerMetre * GridGraph.CellSize * 1.41421356f;

        static readonly ProfilerMarker Marker = new ProfilerMarker("AI.NoisePropagation.Propagate");

        readonly GridGraph _grid;
        readonly Vector2Int[] _neighbourBuffer = new Vector2Int[8];
        readonly Stopwatch _stopwatch = new Stopwatch();

        BinaryHeap _open;
        float[] _loss;
        int[] _seenStamp;
        int[] _closedStamp;
        int _stamp;
        int _cellCount;

        /// <summary>The cell the noise spread from (after snapping out of a wall), if it was heard at all.</summary>
        public Vector2Int Source { get; private set; }

        /// <summary>The level at the source of the last noise (L0).</summary>
        public float SourceLevel { get; private set; }

        /// <summary>True once <see cref="Propagate"/> has run.</summary>
        public bool HasPropagated { get; private set; }

        /// <summary>The grid's version when the last noise was propagated.</summary>
        public int GraphVersion { get; private set; }

        /// <summary>Cells that heard the last noise, for the performance log.</summary>
        public int CellsReached { get; private set; }

        /// <summary>Wall-clock time of the last propagation, in milliseconds.</summary>
        public float ElapsedMs { get; private set; }

        public NoisePropagation(GridGraph grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            AllocateForGridSize();
        }

        /// <summary>Furthest a noise of level <paramref name="sourceLevel"/> is heard in the open, in metres.</summary>
        public static float AudibleRadius(float sourceLevel) =>
            Mathf.Max(0f, (sourceLevel - HearingThreshold) / LossPerMetre);

        /// <summary>
        /// Spreads a noise of level <paramref name="sourceLevel"/> from <paramref name="source"/>.
        /// A source inside a wall or box spreads from the nearest open cell within
        /// <see cref="SourceSnapRadius"/>; a source outside the grid, or one no louder than the
        /// threshold, is heard nowhere.
        /// </summary>
        public void Propagate(Vector2Int source, float sourceLevel)
        {
            if (float.IsNaN(sourceLevel))
                throw new ArgumentOutOfRangeException(nameof(sourceLevel), "Level must be a number.");

            using (Marker.Auto())
            {
                _stopwatch.Restart();
                if (_grid.CellCount != _cellCount)
                    AllocateForGridSize();

                NextStamp();
                _open.Clear();
                SourceLevel = sourceLevel;
                GraphVersion = _grid.Version;
                HasPropagated = true;
                Source = source;

                int reached = 0;
                float maxLoss = sourceLevel - HearingThreshold;
                if (maxLoss > 0f && TrySnapSource(source, out Vector2Int start))
                {
                    Source = start;
                    int startIndex = _grid.ToIndex(start);
                    _loss[startIndex] = 0f;
                    _seenStamp[startIndex] = _stamp;
                    _open.Push(startIndex, 0f);

                    while (!_open.IsEmpty)
                    {
                        // Popped in increasing loss, so this cell's level is now final.
                        int current = _open.Pop();
                        _closedStamp[current] = _stamp;
                        reached++;

                        Vector2Int currentCell = _grid.FromIndex(current);
                        GridNode currentNode = _grid.GetNode(currentCell);
                        int count = _grid.GetNeighboursNonAlloc(currentCell, _neighbourBuffer, allowClosedDoors: true);

                        for (int i = 0; i < count; i++)
                        {
                            Vector2Int next = _neighbourBuffer[i];
                            int nextIndex = _grid.ToIndex(next);
                            if (_closedStamp[nextIndex] == _stamp)
                                continue;

                            float loss = _loss[current] + StepLoss(currentCell, currentNode, next);
                            // At or below the threshold: never queued, so the spread stops here.
                            if (loss >= maxLoss)
                                continue;

                            bool firstVisit = _seenStamp[nextIndex] != _stamp;
                            if (!firstVisit && loss >= _loss[nextIndex])
                                continue;

                            _loss[nextIndex] = loss;
                            if (firstVisit)
                            {
                                _seenStamp[nextIndex] = _stamp;
                                _open.Push(nextIndex, loss);
                            }
                            else
                            {
                                _open.DecreaseKey(nextIndex, loss);
                            }
                        }
                    }
                }

                CellsReached = reached;
                ElapsedMs = (float)_stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>
        /// Level of the last noise at <paramref name="cell"/>, or 0 where it is not heard
        /// (too far, walled off, outside the grid, or nothing propagated yet).
        /// </summary>
        public float Level(Vector2Int cell)
        {
            if (!HasPropagated || !_grid.Contains(cell))
                return 0f;
            int index = _grid.ToIndex(cell);
            return _closedStamp[index] == _stamp ? SourceLevel - _loss[index] : 0f;
        }

        /// <summary>True where the last noise is heard (level above the threshold).</summary>
        public bool IsHeard(Vector2Int cell) => Level(cell) > HearingThreshold;

        // 4 per metre, plus the door penalty when stepping into a closed door from outside it.
        float StepLoss(Vector2Int from, GridNode fromNode, Vector2Int to)
        {
            float loss = from.x != to.x && from.y != to.y ? DiagonalLoss : OrthogonalLoss;
            GridNode toNode = _grid.GetNode(to);
            if (toNode.IsDoorClosed && !(fromNode.IsDoorClosed && fromNode.DoorId == toNode.DoorId))
                loss += ClosedDoorLoss;
            return loss;
        }

        // Sound starts from the source cell if sound can be there (a closed door cell is fine:
        // a door slam is heard on both sides), otherwise from the nearest such cell.
        bool TrySnapSource(Vector2Int source, out Vector2Int start)
        {
            start = source;
            if (IsSoundCell(source))
                return true;

            int bestSquared = int.MaxValue;
            bool found = false;
            for (int dy = -SourceSnapRadius; dy <= SourceSnapRadius; dy++)
            for (int dx = -SourceSnapRadius; dx <= SourceSnapRadius; dx++)
            {
                int squared = dx * dx + dy * dy;
                if (squared > SourceSnapRadius * SourceSnapRadius || squared >= bestSquared)
                    continue;
                var candidate = new Vector2Int(source.x + dx, source.y + dy);
                if (!IsSoundCell(candidate))
                    continue;
                start = candidate;
                bestSquared = squared;
                found = true;
            }
            return found;
        }

        bool IsSoundCell(Vector2Int cell) => _grid.Contains(cell) && _grid.GetNode(cell).IsSoundTraversable;

        void AllocateForGridSize()
        {
            _cellCount = _grid.CellCount;
            _open = new BinaryHeap(_cellCount);
            _loss = new float[_cellCount];
            _seenStamp = new int[_cellCount];
            _closedStamp = new int[_cellCount];
            _stamp = 0;
            HasPropagated = false;
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
