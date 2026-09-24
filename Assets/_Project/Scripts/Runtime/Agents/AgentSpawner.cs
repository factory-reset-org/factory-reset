using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Agents.Mock;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;

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

        [Tooltip("Spawn as soon as the scene starts. Use in test scenes that have no scene loader; leave off in Agents.unity.")]
        [SerializeField] bool spawnOnStart;

        readonly List<AgentController> _spawned = new List<AgentController>();
        WorldBlackboard _blackboard;

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
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Start()
        {
            if (spawnOnStart)
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

            foreach (SpawnPoint point in GetComponentsInChildren<SpawnPoint>())
            {
                // Spawn points mark where the agent's feet go; the prefab's pivot is higher up.
                Vector3 position = point.transform.position + Vector3.up * feetToPivot;

                // Parented under the spawner so agents stay in the Agents scene when scenes load additively.
                AgentController agent = Instantiate(agentPrefab, position, point.transform.rotation, transform);
                agent.name = $"{point.AgentType}_{_spawned.Count}";
                agent.Initialise(CreateBrain(point), _blackboard);
                _spawned.Add(agent);
            }
        }

        /// <summary>
        /// Builds the brain for a spawn point's agent type. Every type uses the mock brain
        /// until its owner's real brain exists; each owner replaces only their own case.
        /// </summary>
        static IAgentBrain CreateBrain(SpawnPoint point)
        {
            switch (point.AgentType)
            {
                case AgentType.Tracker:   // S1: replace with the Tracker brain when ready
                case AgentType.Guard:     // S2: replace with the Guard brain when ready
                case AgentType.Saboteur:  // S3: replace with the Saboteur brain when ready
                case AgentType.Captain:   // S4: replace with the Captain brain when ready
                default:
                    return new MockPathProvider(point.GetPatrolPositions());
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
