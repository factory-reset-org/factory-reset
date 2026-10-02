using System;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Who one agent is: its type, a unique id, and its place in a squad. Given out once by
    /// the spawner and never changed, so brains, the HUD and scoring can all tell agents
    /// apart without looking at scene objects.
    /// </summary>
    public readonly struct AgentIdentity
    {
        /// <summary><see cref="SquadIndex"/> of an agent that is not part of a squad.</summary>
        public const int NoSquad = -1;

        /// <summary>Which of the four enemy classes this agent is.</summary>
        public AgentType Type { get; }

        /// <summary>
        /// Unique, non-negative id, given out in spawn order. Used as the owner id of target
        /// claims and as the key for HUD markers.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Position in its type's squad, starting at 0 (Saboteurs: 0 = A, 1 = B, 2 = C,
        /// 3 = D), or <see cref="NoSquad"/>.
        /// </summary>
        public int SquadIndex { get; }

        /// <summary>True if this agent has a squad position.</summary>
        public bool IsInSquad => SquadIndex >= 0;

        /// <summary>The squad position as a letter ('A' for 0), or '-' if not in a squad.</summary>
        public char SquadLetter => IsInSquad ? (char)('A' + SquadIndex) : '-';

        public AgentIdentity(AgentType type, int id, int squadIndex = NoSquad)
        {
            if (id < 0)
                throw new ArgumentOutOfRangeException(nameof(id), "Agent id must be non-negative.");
            if (squadIndex < NoSquad || squadIndex >= 26)
                throw new ArgumentOutOfRangeException(nameof(squadIndex), "Squad index must be -1 (no squad) or 0-25.");

            Type = type;
            Id = id;
            SquadIndex = squadIndex;
        }

        public override string ToString() =>
            IsInSquad ? $"{Type} {SquadLetter} (#{Id})" : $"{Type} (#{Id})";
    }
}
