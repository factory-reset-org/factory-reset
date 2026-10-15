using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.AI.Core
{
    /// <summary>Another agent near enough to matter for routing: where it stands and how fast it moves.</summary>
    public readonly struct CrowdMember
    {
        /// <summary>Its world position (feet).</summary>
        public readonly Vector3 Position;

        /// <summary>How fast it is really moving, m/s: 0 for one standing, knocked out or blocked.</summary>
        public readonly float Speed;

        public CrowdMember(Vector3 position, float speed)
        {
            Position = position;
            Speed = speed;
        }
    }

    /// <summary>
    /// Implemented by a brain that routes round the other agents (the Captain), which the grid
    /// does not know about: they are bodies, not walls. Optional: a brain that does not
    /// implement it is never called, so <see cref="AgentContext"/> and every other brain stay
    /// as they are.
    /// </summary>
    /// <remarks>
    /// The runtime calls it just before every tick with every other agent that is up or
    /// knocked out (not scrapped). The list is reused: copy what is needed, do not keep it.
    /// </remarks>
    public interface ICrowdAware
    {
        /// <summary>The other agents this tick.</summary>
        void OnCrowd(IReadOnlyList<CrowdMember> others);
    }
}
