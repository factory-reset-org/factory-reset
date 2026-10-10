using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>Describes one sound at its source before propagation or attenuation.</summary>
    public readonly struct NoiseEvent
    {
        /// <summary>World-space position where the sound occurred.</summary>
        public Vector3 Position { get; }

        /// <summary>Sound level at the source.</summary>
        public float Loudness { get; }

        /// <summary>Emitter identity, or -1 when the player made the sound.</summary>
        public int SourceId { get; }

        /// <summary>Game time at which the sound occurred.</summary>
        public float Time { get; }

        /// <summary>
        /// True for a deliberate lure (the thrown wind-up toy): an agent that can be distracted
        /// treats it as one from its first noise, instead of waiting to hear it repeat.
        /// </summary>
        public bool IsLure { get; }

        public NoiseEvent(Vector3 position, float loudness, int sourceId, float time, bool isLure = false)
        {
            Position = position;
            Loudness = loudness;
            SourceId = sourceId;
            Time = time;
            IsLure = isLure;
        }
    }
}
