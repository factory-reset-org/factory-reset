using UnityEngine;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Perception;

namespace ToyFactory.AI.Core
{
    /// <summary>
    /// Everything an agent's brain is given for a single <see cref="IAgentBrain.Tick"/> call.
    /// Built by Runtime each tick; brains never touch a GameObject directly.
    /// </summary>
    public readonly struct AgentContext
    {
        /// <summary>
        /// The agent's current grid cell, for starting searches. Not an arrival test: the
        /// body counts a waypoint as reached within 0.3 m, which is more than half a cell
        /// (0.25 m), so an agent that has arrived may stand in a neighbouring cell. Decide
        /// arrival from <see cref="Position"/> instead.
        /// </summary>
        public readonly Vector2Int Cell;

        /// <summary>The agent's current world position.</summary>
        public readonly Vector3 Position;

        /// <summary>The agent's current forward direction.</summary>
        public readonly Vector3 Forward;

        /// <summary>
        /// Game time in seconds, for cooldowns and timers. It stops during cutscenes and the
        /// pause menu, so timers resume where they left off. Never use UnityEngine.Time in a brain.
        /// </summary>
        public readonly float Time;

        /// <summary>Shared, read-only world state (player, noises, doors, batteries, reservations).</summary>
        public readonly WorldBlackboard World;

        /// <summary>What this specific agent can currently see and hear.</summary>
        public readonly SensorSnapshot Senses;

        public AgentContext(Vector2Int cell, Vector3 position, Vector3 forward, float time,
            WorldBlackboard world, SensorSnapshot senses)
        {
            Cell = cell;
            Position = position;
            Forward = forward;
            Time = time;
            World = world;
            Senses = senses;
        }
    }
}
