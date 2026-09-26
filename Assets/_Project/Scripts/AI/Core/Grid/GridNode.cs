using UnityEngine;

namespace ToyFactory.AI.Core.Grid
{
    /// <summary>An immutable snapshot of one cell. Obtain a fresh snapshot after graph changes.</summary>
    public readonly struct GridNode
    {
        public Vector2Int Cell { get; }
        public Vector3 WorldPosition { get; }
        public bool Walkable { get; }
        public int BlockerCount { get; }
        public bool IsDoorway { get; }
        public bool IsTraversable => Walkable && BlockerCount == 0;

        internal GridNode(Vector2Int cell, Vector3 worldPosition, bool walkable,
            int blockerCount, bool isDoorway)
        {
            Cell = cell;
            WorldPosition = worldPosition;
            Walkable = walkable;
            BlockerCount = blockerCount;
            IsDoorway = isDoorway;
        }
    }
}
