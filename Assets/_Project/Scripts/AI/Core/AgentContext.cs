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
        /// <summary>The agent's current grid cell.</summary>
        public readonly Vector2Int Cell;

        /// <summary>The agent's current world position.</summary>
        public readonly Vector3 Position;

        /// <summary>The agent's current forward direction.</summary>
        public readonly Vector3 Forward;

        /// <summary>Seconds since the game started, for cooldowns and timers.</summary>
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
