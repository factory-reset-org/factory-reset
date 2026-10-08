using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Read-only view of what an agent's body is doing right now. Animation, the HUD,
    /// scoring and the journey read agents through this, never through their brain.
    /// </summary>
    public interface IAgentState
    {
        /// <summary>Which of the four enemy classes this agent is (the same as <c>Identity.Type</c>).</summary>
        AgentType Type { get; }

        /// <summary>This agent's unique id and squad position, fixed at spawn.</summary>
        AgentIdentity Identity { get; }

        /// <summary>
        /// Where the agent's body stands right now: the world-space position of its root, on
        /// the floor under its feet, in metres. Lets the journey follow an agent (the Chapter 3
        /// beacon on Saboteur A) and drop things where one fell. A scrapped agent keeps
        /// reporting the spot where it went down; after its body is destroyed this is the last
        /// position it had.
        /// </summary>
        Vector3 Position { get; }

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
