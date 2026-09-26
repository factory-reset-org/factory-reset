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
            ValidateCell(cell);
            return _nodes[cell.y * Width + cell.x];
        }

        public bool IsTraversable(Vector2Int cell) => Contains(cell) && GetNode(cell).IsTraversable;

        /// <summary>Enumerates legal destinations; invalid or blocked sources have no neighbours.</summary>
        public IEnumerable<Vector2Int> GetNeighbours(Vector2Int cell)
        {
            if (!IsTraversable(cell)) yield break;
            foreach (Vector2Int offset in Offsets)
            {
                Vector2Int destination = cell + offset;
                if (!IsTraversable(destination)) continue;
                if (offset.x != 0 && offset.y != 0 &&
                    (!IsTraversable(new Vector2Int(cell.x + offset.x, cell.y)) ||
                     !IsTraversable(new Vector2Int(cell.x, cell.y + offset.y)))) continue;
                yield return destination;
            }
        }

        public void SetWalkable(Vector2Int cell, bool walkable)
        {
            GridNode node = GetNode(cell);
            if (node.Walkable == walkable) return;
            Update(node, walkable, node.BlockerCount, node.IsDoorway);
        }

        /// <summary>Doorway tagging does not itself block movement; closed doors use blockers.</summary>
        public void SetDoorway(Vector2Int cell, bool isDoorway)
        {
            GridNode node = GetNode(cell);
            if (node.IsDoorway == isDoorway) return;
            Update(node, node.Walkable, node.BlockerCount, isDoorway);
        }

        public void AddBlocker(Vector2Int cell)
        {
            GridNode node = GetNode(cell);
            Update(node, node.Walkable, checked(node.BlockerCount + 1), node.IsDoorway);
        }

        public void RemoveBlocker(Vector2Int cell)
        {
            GridNode node = GetNode(cell);
            if (node.BlockerCount == 0)
                throw new InvalidOperationException("The cell has no blocker to remove.");
            Update(node, node.Walkable, node.BlockerCount - 1, node.IsDoorway);
        }

        void Update(GridNode node, bool walkable, int blockers, bool doorway)
        {
            int nextVersion = checked(Version + 1);
            var affected = new List<Vector2Int> { node.Cell };
            foreach (Vector2Int offset in Offsets)
            {
                Vector2Int neighbour = node.Cell + offset;
                if (Contains(neighbour)) affected.Add(neighbour);
            }
            var change = new GridChange(nextVersion, node.Cell, affected.ToArray());
            _nodes[node.Cell.y * Width + node.Cell.x] =
                new GridNode(node.Cell, node.WorldPosition, walkable, blockers, doorway);
            Version = nextVersion;
            Changed?.Invoke(change);
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
