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

        /// <summary>Called when the agent is stunned for <paramref name="duration"/> seconds.</summary>
        void OnStunned(float duration);
    }
}
