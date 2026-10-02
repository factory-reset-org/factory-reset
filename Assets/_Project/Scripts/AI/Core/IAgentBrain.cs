using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.AI.Core
{
    /// <summary>
    /// The decision-making contract every agent implements. A brain is pure C#: it
    /// never references a GameObject, Transform, or anything Unity-scene-specific
    /// beyond the math types used in <see cref="AgentContext"/> and <see cref="AgentIntent"/>.
    /// </summary>
    public interface IAgentBrain
    {
        /// <summary>
        /// Called once per decision tick. Given the current <paramref name="ctx"/>,
        /// returns what the agent wants to do.
        /// </summary>
        AgentIntent Tick(in AgentContext ctx);

        /// <summary>
        /// Called when the grid changes (a door opens/closes, a box settles or moves).
        /// Implementations should only replan if <paramref name="changedCells"/> actually
        /// affects their current path or reserved cell.
        /// </summary>
        void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells);

        /// <summary>
        /// Called when the agent is knocked out. The body has already stopped, and
        /// <see cref="Tick"/> is not called again until the agent reboots, so the controller
        /// owns the stun timing. Do not start a stun timer of your own: drop the current plan
        /// (and release anything you should not hold while down), then plan a fresh route on
        /// the first tick after the reboot.
        /// </summary>
        /// <param name="duration">Seconds until the agent reboots, for information only.</param>
        void OnStunned(float duration);

        /// <summary>
        /// Called once when the agent leaves the game for good: a Saboteur is scrapped, or
        /// the agent's scene unloads. Release anything held on the shared blackboard here
        /// (target claims, cover reservations). <see cref="Tick"/> is never called afterwards.
        /// </summary>
        void OnDestroyed();
    }
}
