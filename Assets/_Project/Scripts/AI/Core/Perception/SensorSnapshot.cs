using UnityEngine;

namespace ToyFactory.AI.Core.Perception
{
    /// <summary>
    /// What a single agent can currently see and hear, read by that agent's brain
    /// from <see cref="AgentContext.Senses"/>.
    /// </summary>
    /// <remarks>
    /// Runtime builds this once per agent per tick; brains never query Physics or NavMesh
    /// directly. <c>default(SensorSnapshot)</c> means "heard nothing".
    /// <para><b>Hearing</b> (filled by S4's runtime): after each <c>NoiseEvents.Emit</c>,
    /// the runtime runs <c>NoisePropagation</c> once and reads the propagated level at this
    /// agent's cell. If several noises reach the agent between two ticks, it keeps the
    /// loudest. Levels at or below the hearing threshold (10) are not reported.</para>
    /// </remarks>
    public readonly struct SensorSnapshot
    {
        /// <summary>True when a noise reached this agent since its previous tick.</summary>
        public readonly bool HasNoise;

        /// <summary>Where the noise was made (its source, not where the agent heard it).</summary>
        public readonly Vector3 NoisePosition;

        /// <summary>Propagated level at this agent's cell: L0 minus distance and door losses.</summary>
        public readonly float NoiseLevel;

        /// <summary>Who made it: a prop or agent id, -1 for the player.</summary>
        public readonly int NoiseSourceId;

        /// <summary>Game time the noise was made.</summary>
        public readonly float NoiseTime;

        /// <summary>The noise is a deliberate lure (the thrown wind-up toy, <c>NoiseEvent.IsLure</c>).</summary>
        public readonly bool NoiseIsLure;

        public SensorSnapshot(Vector3 noisePosition, float noiseLevel, int noiseSourceId, float noiseTime,
            bool noiseIsLure = false)
        {
            HasNoise = true;
            NoisePosition = noisePosition;
            NoiseLevel = noiseLevel;
            NoiseSourceId = noiseSourceId;
            NoiseTime = noiseTime;
            NoiseIsLure = noiseIsLure;
        }

        /// <summary>
        /// The louder of two snapshots' noises, for a runtime that collects every noise heard
        /// between two ticks: <c>snapshot = SensorSnapshot.Loudest(snapshot, heard)</c>.
        /// A snapshot that heard nothing always loses; a tie keeps the newer noise.
        /// </summary>
        public static SensorSnapshot Loudest(in SensorSnapshot a, in SensorSnapshot b)
        {
            if (!b.HasNoise) return a;
            if (!a.HasNoise) return b;
            if (b.NoiseLevel != a.NoiseLevel) return b.NoiseLevel > a.NoiseLevel ? b : a;
            return b.NoiseTime >= a.NoiseTime ? b : a;
        }
    }
}
