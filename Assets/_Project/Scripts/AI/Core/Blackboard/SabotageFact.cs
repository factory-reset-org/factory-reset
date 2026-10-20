using UnityEngine;

namespace ToyFactory.AI.Core.Blackboard
{
    /// <summary>What kind of object a <see cref="SabotageFact"/> describes.</summary>
    public enum SabotageFactKind
    {
        Trap,
        Battery
    }

    /// <summary>
    /// What a Saboteur may know about one trap or battery: its id (unique across doors, traps and
    /// batteries, because the squad claims a target by id alone), the grid cell it stands on, and
    /// whether the action would still do anything.
    /// </summary>
    public readonly struct SabotageFact
    {
        public int Id { get; }

        public Vector2Int Cell { get; }

        public SabotageFactKind Kind { get; }

        /// <summary>A trap that is not armed yet, or a battery that is still there.</summary>
        public bool Available { get; }

        public SabotageFact(int id, Vector2Int cell, SabotageFactKind kind, bool available)
        {
            Id = id;
            Cell = cell;
            Kind = kind;
            Available = available;
        }
    }
}
