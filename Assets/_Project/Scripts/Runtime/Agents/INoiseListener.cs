using UnityEngine;
using ToyFactory.AI.Core.Perception;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Something that can hear noises spread by <see cref="AgentHearing"/>: an agent body.
    /// An interface rather than <see cref="AgentController"/> itself, so hearing can be
    /// tested without spawning agents.
    /// </summary>
    public interface INoiseListener
    {
        /// <summary>Where the listener is, in world space (its feet).</summary>
        Vector3 HearingPosition { get; }

        /// <summary>False while it cannot hear: scrapped or knocked out.</summary>
        bool CanHear { get; }

        /// <summary>A noise reached the listener at the level in <paramref name="heard"/>.</summary>
        void Hear(in SensorSnapshot heard);
    }
}
