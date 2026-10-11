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
        /// Null means "keep following the current path" rather than "stop";
        /// an empty list means "stop where you are".
        /// The body may smooth the route, and it counts each waypoint as reached within
        /// 0.3 m on the ground plane, then stops after the last one. So the agent ends up
        /// near the last waypoint, not exactly on it: check arrival by distance, not by cell.
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

        /// <summary>Human-readable current state, shown by the debug overlay.</summary>
        public string DebugState;

        /// <summary>
        /// How aware the agent is of the player, shown as a "?" or "!" icon above its head.
        /// Optional: left at <see cref="AlertLevel.None"/>, the body works it out from
        /// <see cref="DebugState"/> instead.
        /// </summary>
        public AlertLevel Alert;

        /// <summary>
        /// True when the agent is busy with something else (the Tracker watching a thrown toy)
        /// and its body should not stand off from, face or strike the player this tick, even
        /// at close range. Optional: left false, the body engages a player in reach as before.
        /// </summary>
        public bool IgnorePlayer;
    }
}
