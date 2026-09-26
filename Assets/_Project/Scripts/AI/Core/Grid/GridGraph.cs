using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.AI.Core.Grid
{
    /// <summary>A scene-independent, bounded XZ grid with eight-connected movement.</summary>
    /// <remarks>
    /// Origin is the minimum corner of cell (0,0), not its centre. Cell Y maps to world Z.
    /// World Y is ignored on input; centres use Origin.y. Bounds are minimum-inclusive
    /// and maximum-exclusive. This graph is intended for single-threaded use.
    /// </remarks>
    public sealed class GridGraph
    {
        public const float CellSize = 0.5f;

        // Cardinals first, then diagonals; stable ordering for repeatable searches.
        static readonly Vector2Int[] Offsets =
        {
            new Vector2Int(0, 1), new Vector2Int(1, 0),
            new Vector2Int(0, -1), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1),
            new Vector2Int(-1, -1), new Vector2Int(-1, 1)
        };

        readonly GridNode[] _nodes;

        public int Width { get; }
        public int Height { get; }
        public int CellCount => _nodes.Length;
        public Vector3 Origin { get; }
        public int Version { get; private set; }

        /// <summary>
        /// Raised synchronously after each effective mutation, including metadata and
        /// blocker count changes that leave traversability unchanged. No-op writes emit nothing.
        /// </summary>
        public event Action<GridChange> Changed;

        public GridGraph(int width, int height, Vector3 origin)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0 || (long)width * height > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (!IsFinite(origin.x) || !IsFinite(origin.y) || !IsFinite(origin.z))
                throw new ArgumentException("Origin must be finite.", nameof(origin));

            Width = width;
            Height = height;
            Origin = origin;
            _nodes = new GridNode[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var cell = new Vector2Int(x, y);
                _nodes[y * width + x] = new GridNode(cell, CellToWorld(cell), true, 0, false);
            }
            for (int i = 0; i < CellCount; i++) RefreshChokepoint(i);
        }

        public bool Contains(Vector2Int cell) =>
            cell.x >= 0 && cell.x < Width && cell.y >= 0 && cell.y < Height;

        /// <summary>Converts without clamping. Finite X/Z must map to representable integer cells.</summary>
        public Vector2Int WorldToCell(Vector3 worldPosition) => new Vector2Int(
            ToCellCoordinate(worldPosition.x, Origin.x),
            ToCellCoordinate(worldPosition.z, Origin.z));

        /// <summary>Returns false outside bounds; the converted cell is still returned.</summary>
        public bool TryWorldToCell(Vector3 worldPosition, out Vector2Int cell)
        {
            cell = WorldToCell(worldPosition);
            return Contains(cell);
        }

        public Vector3 CellToWorld(Vector2Int cell)
        {
            ValidateCell(cell);
            return new Vector3(Origin.x + (cell.x + 0.5f) * CellSize,
                Origin.y, Origin.z + (cell.y + 0.5f) * CellSize);
        }

        public GridNode GetNode(Vector2Int cell)
        {
            return _nodes[ToIndex(cell)];
        }

        /// <summary>Row-major heap/search ID: y * Width + x.</summary>
        public int ToIndex(Vector2Int cell)
        {
            ValidateCell(cell);
            return cell.y * Width + cell.x;
        }

        public Vector2Int FromIndex(int index)
        {
            if (index < 0 || index >= CellCount) throw new ArgumentOutOfRangeException(nameof(index));
            return new Vector2Int(index % Width, index / Width);
        }

        public bool IsTraversable(Vector2Int cell) => Contains(cell) && GetNode(cell).IsTraversable;

        /// <summary>Allocating convenience API. Search loops should use GetNeighboursNonAlloc.</summary>
        public IEnumerable<Vector2Int> GetNeighbours(Vector2Int cell)
        {
            foreach (Vector2Int offset in Offsets)
            {
                if (CanStep(cell, offset, false, false)) yield return cell + offset;
            }
        }

        /// <summary>
        /// Writes up to eight cells into a reusable buffer (length at least eight), returning
        /// the valid prefix length. No allocations. Order: N,E,S,W,NE,SE,SW,NW.
        /// allowClosedDoors exposes sound connectivity, not movement: callers inspect DoorId
        /// and IsDoorClosed to apply their configured acoustic penalty. Ordinary blockers
        /// still obstruct sound. Both modes enforce diagonal side-cell clearance.
        /// Do not mutate the graph during a search; compare Version to detect stale results.
        /// </summary>
        public int GetNeighboursNonAlloc(Vector2Int cell, Vector2Int[] buffer, bool allowClosedDoors = false)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length < 8) throw new ArgumentException("Buffer must hold eight neighbours.", nameof(buffer));
            int count = 0;
            foreach (Vector2Int offset in Offsets)
                if (CanStep(cell, offset, allowClosedDoors, false)) buffer[count++] = cell + offset;
            return count;
        }

        bool CanStep(Vector2Int cell, Vector2Int offset, bool allowClosedDoors, bool staticOnly)
        {
            if (!CanOccupy(cell, allowClosedDoors, staticOnly) ||
                !CanOccupy(cell + offset, allowClosedDoors, staticOnly)) return false;
            return offset.x == 0 || offset.y == 0 ||
                (CanOccupy(new Vector2Int(cell.x + offset.x, cell.y), allowClosedDoors, staticOnly) &&
                 CanOccupy(new Vector2Int(cell.x, cell.y + offset.y), allowClosedDoors, staticOnly));
        }

        bool CanOccupy(Vector2Int cell, bool allowClosedDoors, bool staticOnly)
        {
            if (!Contains(cell)) return false;
            GridNode node = _nodes[cell.y * Width + cell.x];
            return staticOnly ? node.Walkable : allowClosedDoors ? node.IsSoundTraversable : node.IsTraversable;
        }

        /// <summary>
        /// Stages one logical change. Call Commit explicitly; disposal without Commit rolls back.
        /// Graph queries see the old state until commit. A stale batch is rejected, not merged.
        /// </summary>
        public Batch BeginBatch() => new Batch(this);

        public void SetWalkable(Vector2Int cell, bool walkable)
        {
            using (Batch batch = BeginBatch()) { batch.SetWalkable(cell, walkable); batch.Commit(); }
        }

        /// <summary>Tags an architectural doorway; removing the tag also removes door ID/state.</summary>
        public void SetDoorway(Vector2Int cell, bool isDoorway)
        {
            using (Batch batch = BeginBatch()) { batch.SetDoorway(cell, isDoorway); batch.Commit(); }
        }

        /// <summary>
        /// Associates a door ID/state independently of ordinary blockers. Null removes the
        /// door but preserves the architectural doorway. Update all cells of a door in one batch.
        /// </summary>
        public void SetDoor(Vector2Int cell, int? doorId, bool isClosed)
        {
            using (Batch batch = BeginBatch()) { batch.SetDoor(cell, doorId, isClosed); batch.Commit(); }
        }

        public void AddBlocker(Vector2Int cell)
        {
            using (Batch batch = BeginBatch()) { batch.AddBlocker(cell); batch.Commit(); }
        }

        public void RemoveBlocker(Vector2Int cell)
        {
            using (Batch batch = BeginBatch()) { batch.RemoveBlocker(cell); batch.Commit(); }
        }

        void Commit(Dictionary<int, GridNode> staged)
        {
            var changed = new List<int>();
            foreach (KeyValuePair<int, GridNode> entry in staged)
            {
                GridNode before = _nodes[entry.Key];
                GridNode after = entry.Value;
                if (before.Walkable != after.Walkable || before.BlockerCount != after.BlockerCount ||
                    before.IsDoorway != after.IsDoorway || before.DoorId != after.DoorId ||
                    before.IsDoorClosed != after.IsDoorClosed) changed.Add(entry.Key);
            }
            if (changed.Count == 0) return;
            int nextVersion = checked(Version + 1);
            changed.Sort();
            var affected = new SortedSet<int>();
            foreach (int index in changed)
            {
                affected.Add(index);
                Vector2Int cell = FromIndex(index);
                foreach (Vector2Int offset in Offsets)
                    if (Contains(cell + offset)) affected.Add(ToIndex(cell + offset));
            }
            var changedCells = new Vector2Int[changed.Count];
            for (int i = 0; i < changed.Count; i++) changedCells[i] = FromIndex(changed[i]);
            var affectedCells = new Vector2Int[affected.Count];
            int cursor = 0;
            foreach (int index in affected) affectedCells[cursor++] = FromIndex(index);
            var change = new GridChange(nextVersion, changedCells, affectedCells);
            foreach (int index in changed) _nodes[index] = staged[index];
            foreach (int index in affected) RefreshChokepoint(index);
            Version = nextVersion;
            Changed?.Invoke(change);
        }

        void RefreshChokepoint(int index)
        {
            GridNode node = _nodes[index];
            int count = 0;
            foreach (Vector2Int offset in Offsets)
                if (CanStep(node.Cell, offset, false, true)) count++;
            _nodes[index] = new GridNode(node.Cell, node.WorldPosition, node.Walkable,
                node.BlockerCount, node.IsDoorway, node.DoorId, node.IsDoorClosed,
                node.IsDoorway || (node.Walkable && count <= 4));
        }

        /// <summary>Atomic staged edits. Mutation-time allocations are outside search loops.</summary>
        public sealed class Batch : IDisposable
        {
            readonly GridGraph _graph;
            readonly int _version;
            readonly Dictionary<int, GridNode> _staged = new Dictionary<int, GridNode>();
            bool _finished;

            internal Batch(GridGraph graph) { _graph = graph; _version = graph.Version; }

            GridNode Read(Vector2Int cell)
            {
                EnsureActive();
                int index = _graph.ToIndex(cell);
                return _staged.TryGetValue(index, out GridNode node) ? node : _graph._nodes[index];
            }

            void Write(GridNode node, bool walkable, int blockers, bool doorway, int? doorId, bool closed)
            {
                _staged[_graph.ToIndex(node.Cell)] = new GridNode(node.Cell, node.WorldPosition,
                    walkable, blockers, doorway, doorId, closed);
            }

            public void SetWalkable(Vector2Int cell, bool walkable)
            {
                GridNode n = Read(cell);
                Write(n, walkable, n.BlockerCount, n.IsDoorway, n.DoorId, n.IsDoorClosed);
            }

            public void SetDoorway(Vector2Int cell, bool doorway)
            {
                GridNode n = Read(cell);
                Write(n, n.Walkable, n.BlockerCount, doorway, doorway ? n.DoorId : null,
                    doorway && n.IsDoorClosed);
            }

            public void SetDoor(Vector2Int cell, int? doorId, bool isClosed)
            {
                GridNode n = Read(cell);
                if (!doorId.HasValue && isClosed)
                    throw new ArgumentException("A closed door requires an ID.", nameof(doorId));
                Write(n, n.Walkable, n.BlockerCount, n.IsDoorway || doorId.HasValue, doorId, isClosed);
            }

            public void AddBlocker(Vector2Int cell)
            {
                GridNode n = Read(cell);
                Write(n, n.Walkable, checked(n.BlockerCount + 1), n.IsDoorway, n.DoorId, n.IsDoorClosed);
            }

            public void RemoveBlocker(Vector2Int cell)
            {
                GridNode n = Read(cell);
                if (n.BlockerCount == 0) throw new InvalidOperationException("The cell has no blocker to remove.");
                Write(n, n.Walkable, n.BlockerCount - 1, n.IsDoorway, n.DoorId, n.IsDoorClosed);
            }

            public void Commit()
            {
                EnsureActive();
                _finished = true;
                _graph.Commit(_staged);
            }

            public void Dispose() { _finished = true; }

            void EnsureActive()
            {
                if (_finished) throw new ObjectDisposedException(nameof(Batch));
                if (_graph.Version != _version)
                    throw new InvalidOperationException("Graph changed since this batch began.");
            }
        }

        void ValidateCell(Vector2Int cell)
        {
            if (!Contains(cell)) throw new ArgumentOutOfRangeException(nameof(cell));
        }

        static int ToCellCoordinate(float value, float origin)
        {
            double coordinate = Math.Floor(((double)value - origin) / CellSize);
            if (double.IsNaN(coordinate) || coordinate < int.MinValue || coordinate > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), "Coordinate must map to a finite integer cell.");
            return (int)coordinate;
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
