using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.AI.Core
{
    /// <summary>
    /// What an agent's brain wants to happen this tick, returned from
    /// <see cref="IAgentBrain.Tick"/>. Runtime reads this and drives the agent's body;
    /// the brain never moves anything itself.
    /// </summary>
    public struct AgentIntent
    {
        /// <summary>
        /// Grid cells from the agent's current cell to its target, in world-position order.
        /// Null means "keep following the current path" rather than "stop".
        /// </summary>
        public List<Vector3> Path;

        /// <summary>Desired movement speed in metres per second.</summary>
        public float DesiredSpeed;

        /// <summary>World point to face, if any.</summary>
        public Vector3? LookTarget;

        /// <summary>Action to perform this tick, if any.</summary>
        public AgentAction Action;

        /// <summary>Id of the object <see cref="Action"/> targets (a door, trap or battery).</summary>
        public int ActionTargetId;

        /// <summary>Human-readable current state, shown by the debug overlay and driving "!"/"?" icons.</summary>
        public string DebugState;
    }
}
