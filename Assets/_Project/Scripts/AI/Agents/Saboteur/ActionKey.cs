using System;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// The Saboteur's action types. The numeric order is the deterministic tie-break
    /// order when two candidates rank equally.
    /// </summary>
    public enum SaboteurActionKind
    {
        CloseDoor = 0,
        ArmTrap = 1,
        StealBattery = 2,
        AttackPlayer = 3,
        Flee = 4,
        Idle = 5
    }

    /// <summary>An action paired with the target it acts on (a door, trap or battery id).</summary>
    public readonly struct ActionKey : IEquatable<ActionKey>
    {
        /// <summary>Target id for actions with no target (attack, flee, idle).</summary>
        public const int NoTarget = -1;

        /// <summary>The action type.</summary>
        public SaboteurActionKind Kind { get; }

        /// <summary>The target id, or <see cref="NoTarget"/>.</summary>
        public int TargetId { get; }

        /// <summary>Creates an action-target pair.</summary>
        public ActionKey(SaboteurActionKind kind, int targetId = NoTarget)
        {
            Kind = kind;
            TargetId = targetId;
        }

        /// <inheritdoc />
        public bool Equals(ActionKey other) => Kind == other.Kind && TargetId == other.TargetId;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is ActionKey other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => ((int)Kind * 397) ^ TargetId;

        /// <inheritdoc />
        public override string ToString() => TargetId == NoTarget ? Kind.ToString() : $"{Kind}({TargetId})";
    }

    /// <summary>
    /// An eligible action-target pair with its compensated base score. Ineligible pairs
    /// are never turned into candidates.
    /// </summary>
    public readonly struct ActionCandidate
    {
        /// <summary>The action-target pair.</summary>
        public ActionKey Key { get; }

        /// <summary>The compensated score in [0, 1], before momentum.</summary>
        public float BaseScore { get; }

        /// <summary>Creates a candidate.</summary>
        public ActionCandidate(ActionKey key, float baseScore)
        {
            Key = key;
            BaseScore = baseScore;
        }
    }
}
