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
        /// <summary>Runtime object ID, matching AgentIntent.ActionTargetId; null means no door.</summary>
        public int? DoorId { get; }
        public bool IsDoorClosed { get; }
        /// <summary>Static topology: doorway, or walkable cell with at most four legal walkable neighbours.</summary>
        public bool IsChokepoint { get; }
        public bool IsTraversable => IsSoundTraversable && !IsDoorClosed;
        /// <summary>Closed doors transmit sound; walls and ordinary blockers do not.</summary>
        public bool IsSoundTraversable => Walkable && BlockerCount == 0;

        internal GridNode(Vector2Int cell, Vector3 worldPosition, bool walkable,
            int blockerCount, bool isDoorway, int? doorId = null, bool isDoorClosed = false,
            bool isChokepoint = false)
        {
            Cell = cell;
            WorldPosition = worldPosition;
            Walkable = walkable;
            BlockerCount = blockerCount;
            IsDoorway = isDoorway;
            DoorId = doorId;
            IsDoorClosed = isDoorClosed;
            IsChokepoint = isChokepoint;
        }
    }
}
