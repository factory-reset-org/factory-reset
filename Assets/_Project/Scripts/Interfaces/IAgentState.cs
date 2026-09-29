namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Read-only view of what an agent's body is doing right now. Animation, the HUD,
    /// scoring and the journey read agents through this, never through their brain.
    /// </summary>
    public interface IAgentState
    {
        /// <summary>Which of the four enemy classes this agent is.</summary>
        AgentType Type { get; }

        /// <summary>Current ground speed in metres per second.</summary>
        float Speed { get; }

        /// <summary>Current turn rate in degrees per second. Positive means turning right.</summary>
        float TurnRate { get; }

        /// <summary>True while the agent is carrying out an attack.</summary>
        bool IsAttacking { get; }

        /// <summary>True once the agent has been scrapped. It never becomes false again.</summary>
        bool IsDead { get; }
    }
}
