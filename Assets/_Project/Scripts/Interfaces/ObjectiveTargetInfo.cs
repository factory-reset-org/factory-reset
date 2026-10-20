using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>World-space information for one currently active gameplay objective.</summary>
    public readonly struct ObjectiveTargetInfo
    {
        /// <summary>Stable identity of the target while it is active.</summary>
        public int Id { get; }

        /// <summary>World-space position agents should associate with the target.</summary>
        public Vector3 Position { get; }

        /// <summary>The target's gameplay role.</summary>
        public ObjectiveKind Kind { get; }

        public ObjectiveTargetInfo(int id, Vector3 position, ObjectiveKind kind)
        {
            Id = id;
            Position = position;
            Kind = kind;
        }
    }
}
