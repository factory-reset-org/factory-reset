using System;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>The four Saboteur instances. The order is also the squad tick-stagger order.</summary>
    public enum SaboteurLetter
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3
    }

    /// <summary>
    /// Who one Saboteur instance is: its claim owner id on the shared blackboard and its
    /// letter. Given to the brain at spawn; the brain never derives it from a scene object.
    /// </summary>
    public readonly struct SaboteurIdentity
    {
        /// <summary>Unique, non-negative id used as the owner of target claims.</summary>
        public int AgentId { get; }

        /// <summary>Which of the four instances this is.</summary>
        public SaboteurLetter Letter { get; }

        /// <summary>Saboteur A carries the chapter keycard and drops it when destroyed.</summary>
        public bool CarriesKeycard => Letter == SaboteurLetter.A;

        /// <summary>
        /// True when built through the constructor. <c>default(SaboteurIdentity)</c> skips the
        /// checks and would read as Saboteur A, so brains reject an identity that is not assigned.
        /// </summary>
        public bool IsAssigned { get; }

        /// <summary>Creates an identity.</summary>
        public SaboteurIdentity(int agentId, SaboteurLetter letter)
        {
            if (agentId < 0)
                throw new ArgumentOutOfRangeException(nameof(agentId), "Agent id must be non-negative.");
            if (!Enum.IsDefined(typeof(SaboteurLetter), letter))
                throw new ArgumentOutOfRangeException(nameof(letter));

            AgentId = agentId;
            Letter = letter;
            IsAssigned = true;
        }

        /// <inheritdoc />
        public override string ToString() => $"Saboteur {Letter} (#{AgentId})";
    }
}
