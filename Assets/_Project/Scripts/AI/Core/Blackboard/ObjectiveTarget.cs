using UnityEngine;

namespace ToyFactory.AI.Core.Blackboard
{
    /// <summary>Grid-space information for one currently active gameplay objective.</summary>
    public readonly struct ObjectiveTarget
    {
        /// <summary>Stable identity of the target while it is active.</summary>
        public int Id { get; }

        /// <summary>Grid cell agents should associate with the target.</summary>
        public Vector2Int Cell { get; }

        /// <summary>The target's gameplay role.</summary>
        public ObjectiveTargetKind Kind { get; }

        public ObjectiveTarget(int id, Vector2Int cell, ObjectiveTargetKind kind)
        {
            Id = id;
            Cell = cell;
            Kind = kind;
        }
    }
}
