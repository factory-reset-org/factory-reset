using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Agents.Mock;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.World;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Creates every agent in the level. For each <see cref="SpawnPoint"/> under this
    /// object it instantiates an agent body, builds the brain for that agent type, and
    /// hands both the brain and the shared <see cref="WorldBlackboard"/> to the agent's
    /// <see cref="AgentController"/>. The scene loader calls <see cref="SpawnAll"/> once
    /// the level and grid exist; test scenes can use "Spawn On Start" instead.
    /// </summary>
    public sealed class AgentSpawner : MonoBehaviour
    {
        /// <summary>The spawner in the loaded Agents scene, for the scene loader to call.</summary>
        public static AgentSpawner Instance { get; private set; }

        [Tooltip("Agent body to spawn. One placeholder body is used for every type until the real models exist.")]
        [SerializeField] AgentController agentPrefab;

        [Tooltip("Spawn as soon as the scene starts, building the level grid first if the scene has a GridManager. Use in test scenes that have no scene loader; leave off in Agents.unity.")]
        [SerializeField] bool spawnOnStart;

        readonly List<AgentController> _spawned = new List<AgentController>();
        WorldBlackboard _blackboard;
        ObjectiveTargetWriter _objectiveWriter;

        /// <summary>Every agent spawned so far, for the debug overlay and tests.</summary>
        public IReadOnlyList<AgentController> SpawnedAgents => _spawned;

        /// <summary>The blackboard shared by every agent's brain.</summary>
        public WorldBlackboard Blackboard => _blackboard;

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"More than one {nameof(AgentSpawner)} is loaded; using the one on {name}.", this);

            Instance = this;
            _blackboard = new WorldBlackboard();

            // Created with the blackboard, in Awake, so it is already listening when the
            // chapter manager publishes Chapter 1's targets after the agents spawn.
            _objectiveWriter = new ObjectiveTargetWriter(_blackboard);
        }

        void OnDestroy()
        {
            _objectiveWriter?.Dispose();
            if (Instance == this)
                Instance = null;
        }

        void Start()
        {
            if (!spawnOnStart)
                return;

            // Test scenes have no scene loader to build the grid, so build it here first when
            // the scene has a GridManager. If the build fails (e.g. the NavMesh is not baked),
            // log why and still spawn, without a grid.
            if (GridManager.Current == null && GridManager.Instance != null)
            {
                try
                {
                    GridManager.Instance.BuildGrid();
                }
                catch (Exception e)
                {
                    Debug.LogException(e, GridManager.Instance);
                }
            }

            SpawnAll();
        }

        /// <summary>
        /// Spawns one agent at every child <see cref="SpawnPoint"/>. Safe to call only once;
        /// later calls are ignored with a warning.
        /// </summary>
        public void SpawnAll()
        {
            if (_spawned.Count > 0)
            {
                Debug.LogWarning($"{nameof(SpawnAll)} was called again; agents are already spawned.", this);
                return;
            }

            if (agentPrefab == null)
            {
                Debug.LogError($"{nameof(AgentSpawner)} on {name} has no agent prefab assigned.", this);
                return;
            }

            float feetToPivot = FeetToPivotHeight(agentPrefab);
            var usedSquadSlots = new HashSet<(AgentType, int)>();

            // The level grid is built by the scene loader before agents spawn. Without one
            // (a test scene with no GridManager) agents still spawn and patrol with the mock
            // brain, but nothing can plan routes on a grid.
            GridGraph grid = GridManager.Current;
            IPathfinder pathfinder = grid != null ? new AStarSearch(grid) : null;
            if (grid == null)
                Debug.LogWarning($"{nameof(AgentSpawner)}: no level grid has been built, so brains get no grid or pathfinder.", this);

            foreach (SpawnPoint point in GetComponentsInChildren<SpawnPoint>())
            {
                // Ids are given out in spawn order, so they are unique without anyone typing them.
                var identity = new AgentIdentity(point.AgentType, _spawned.Count, point.SquadIndex);
                if (identity.IsInSquad && !usedSquadSlots.Add((identity.Type, identity.SquadIndex)))
                    Debug.LogError($"Two {identity.Type} spawn points use squad slot {identity.SquadLetter}; each slot must be used once.", point);

                // Spawn points mark where the agent's feet go; the prefab's pivot is higher up.
                Vector3 position = point.transform.position + Vector3.up * feetToPivot;

                // Parented under the spawner so agents stay in the Agents scene when scenes load additively.
                AgentController agent = Instantiate(agentPrefab, position, point.transform.rotation, transform);
                agent.name = identity.ToString();
                var setup = new BrainSetup(identity, grid, pathfinder, _blackboard, point.GetPatrolPositions());
                agent.Initialise(identity, CreateBrain(point, setup), _blackboard, grid);
                _spawned.Add(agent);
            }
        }

        /// <summary>
        /// Builds the brain for a spawn point's agent type. Every type uses the mock brain
        /// until its owner's real brain exists; each owner replaces only their own case.
        /// <paramref name="setup"/> carries everything a brain may need: the identity (its
        /// unique id is the target-claim owner; its squad slot is the Saboteur letter), the
        /// level grid and the shared A* (both null without a grid), the blackboard and the
        /// patrol route.
        /// </summary>
        static IAgentBrain CreateBrain(SpawnPoint point, in BrainSetup setup)
        {
            switch (point.AgentType)
            {
                case AgentType.Tracker:   // S1: replace with the Tracker brain when ready
                case AgentType.Guard:     // S2: replace with the Guard brain when ready
                case AgentType.Saboteur:  // S3: replace with the Saboteur brain when ready
                case AgentType.Captain:   // S4: replace with the Captain brain when ready
                default:
                    return new MockPathProvider(setup.PatrolPoints);
            }
        }

        // Height from the bottom of the agent's CharacterController to its pivot, so an
        // agent spawned at floor level stands on the floor instead of inside it.
        static float FeetToPivotHeight(AgentController prefab)
        {
            var controller = prefab.GetComponent<CharacterController>();
            if (controller == null)
                return 0f;

            return controller.height * 0.5f - controller.center.y;
        }
    }
}
