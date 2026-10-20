using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Everything the spawner hands to <c>AgentSpawner.CreateBrain</c> when it builds one
    /// agent's brain. Grouped in one struct so a new dependency (for example the squad's
    /// shared target claims) is a new field here, not a new parameter in every owner's case.
    /// </summary>
    public readonly struct BrainSetup
    {
        /// <summary>The agent's type, unique id and squad slot.</summary>
        public AgentIdentity Identity { get; }

        /// <summary>The level grid every brain plans on, or null when no grid has been built.</summary>
        public GridGraph Grid { get; }

        /// <summary>One A* search over <see cref="Grid"/>, shared by every brain; null without a grid.</summary>
        public IPathfinder Pathfinder { get; }

        /// <summary>The blackboard shared by every brain.</summary>
        public WorldBlackboard Blackboard { get; }

        /// <summary>The spawn point's patrol route in world space, never empty.</summary>
        public IReadOnlyList<Vector3> PatrolPoints { get; }

        /// <summary>True when a level grid exists, so brains can plan their own routes.</summary>
        public bool HasGrid => Grid != null;

        public BrainSetup(AgentIdentity identity, GridGraph grid, IPathfinder pathfinder,
            WorldBlackboard blackboard, IReadOnlyList<Vector3> patrolPoints)
        {
            Identity = identity;
            Grid = grid;
            Pathfinder = pathfinder;
            Blackboard = blackboard;
            PatrolPoints = patrolPoints;
        }
    }
}
