using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Agents.Mock;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Agents.Tracker;
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
    /// It also owns the Runtime writers that fill the blackboard (objectives, chapter,
    /// player, the Captain's wake) and the hearing that fills each agent's senses.
    /// </summary>
    /// <remarks>
    /// Runs before the default execution order, so the player snapshot is written each
    /// frame before any agent's brain reads it.
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class AgentSpawner : MonoBehaviour
    {
        /// <summary>The spawner in the loaded Agents scene, for the scene loader to call.</summary>
        public static AgentSpawner Instance { get; private set; }

        /// <summary>The body prefab for one agent type.</summary>
        [Serializable]
        struct AgentBody
        {
            public AgentType type;
            public AgentController prefab;
        }

        [Tooltip("Body to spawn for each agent type: its model, Animator and a capsule sized for that agent.")]
        [SerializeField] AgentBody[] bodies = new AgentBody[0];

        [Tooltip("Fallback body for any type not listed above, e.g. the placeholder capsule.")]
        [SerializeField] AgentController agentPrefab;

        [Tooltip("Spawn as soon as the scene starts, building the level grid first if the scene has a GridManager. Use in test scenes that have no scene loader; leave off in Agents.unity.")]
        [SerializeField] bool spawnOnStart;

        readonly List<AgentController> _spawned = new List<AgentController>();
        WorldBlackboard _blackboard;
        ObjectiveTargetWriter _objectiveWriter;
        ChapterIndexWriter _chapterWriter;
        CaptainWakeWriter _captainWakeWriter;
        PlayerStateWriter _playerWriter;
        AgentHearing _hearing;

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
            _chapterWriter = new ChapterIndexWriter(_blackboard);
            _captainWakeWriter = new CaptainWakeWriter(_blackboard);
            _playerWriter = new PlayerStateWriter(_blackboard);

            // Reads the live agent list and the grid at each noise, so it works before and
            // after spawning and picks up a grid built later.
            _hearing = new AgentHearing(_spawned, () => GridManager.Current);
        }

        void Update() => _playerWriter.Write(GridManager.Current);

        void OnDestroy()
        {
            _objectiveWriter?.Dispose();
            _chapterWriter?.Dispose();
            _captainWakeWriter?.Dispose();
            _hearing?.Dispose();
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

                AgentController body = BodyFor(point.AgentType);
                if (body == null)
                {
                    Debug.LogError($"{nameof(AgentSpawner)} on {name} has no body for {point.AgentType} and no fallback prefab.", point);
                    continue;
                }

                // Spawn points mark where the agent's feet go; the prefab's pivot may be higher up.
                Vector3 position = point.transform.position + Vector3.up * FeetToPivotHeight(body);

                // Parented under the spawner so agents stay in the Agents scene when scenes load additively.
                AgentController agent = Instantiate(body, position, point.transform.rotation, transform);
                agent.name = identity.ToString();
                var setup = new BrainSetup(identity, grid, pathfinder, _blackboard, point.GetPatrolPositions());
                agent.Initialise(identity, CreateBrain(point, setup), _blackboard, grid);
                _spawned.Add(agent);
            }
        }

        /// <summary>
        /// Spawns one more agent of <paramref name="type"/> with <paramref name="brain"/>,
        /// standing at <paramref name="feet"/>, after the level's own agents: for demos such as
        /// the intercept against a chaser. It takes the next id, the shared blackboard and the
        /// level grid, and joins <see cref="SpawnedAgents"/>, so the debug overlay draws it.
        /// Returns null if there is no body for the type.
        /// </summary>
        public AgentController Spawn(AgentType type, IAgentBrain brain, Vector3 feet, Quaternion rotation, string label)
        {
            if (brain == null)
                throw new ArgumentNullException(nameof(brain));
            AgentController body = BodyFor(type);
            if (body == null)
                return null;
            var identity = new AgentIdentity(type, _spawned.Count);
            AgentController agent = Instantiate(body, feet + Vector3.up * FeetToPivotHeight(body), rotation, transform);
            agent.name = string.IsNullOrEmpty(label) ? identity.ToString() : label;
            agent.Initialise(identity, brain, _blackboard, GridManager.Current);
            _spawned.Add(agent);
            return agent;
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
                case AgentType.Tracker:
                    // The Tracker plans on the grid with its own GBFS; without a grid (or a
                    // patrol route) it falls back to the mock, like the unfinished types.
                    // In the game it leaves a player who has not moved since spawning unseen.
                    if (setup.HasGrid && setup.PatrolPoints != null && setup.PatrolPoints.Count > 0)
                        return new TrackerBrain(setup.Grid, setup.Blackboard, setup.PatrolPoints, spawnGrace: true);
                    return new MockPathProvider(setup.PatrolPoints);
                case AgentType.Captain:
                    // The Captain predicts and intercepts on the grid; without one it has
                    // nothing to reason about, so it falls back to the mock.
                    if (setup.HasGrid)
                        return new CaptainBrain(setup.Grid, setup.Pathfinder, setup.Blackboard, point.StartAwake);
                    return new MockPathProvider(setup.PatrolPoints);
                case AgentType.Guard:
                    // The Guard picks cover on the grid and checks sight lines with physics;
                    // without a grid it falls back to the mock. It holds the room its patrol
                    // is in, and fights only for it.
                    if (setup.HasGrid)
                        return new GuardBrain(setup.Grid, setup.Pathfinder, setup.Blackboard,
                            new PhysicsCoverVisibility(setup.Grid), setup.Identity.Id, setup.PatrolPoints,
                            AreaGuardHome.ForPatrol(setup.PatrolPoints));
                    return new MockPathProvider(setup.PatrolPoints);
                case AgentType.Saboteur:
                    // The four Saboteurs share one squad through the blackboard's claims and one
                    // detour cache; the squad slot is the letter. They attack a player in sight
                    // within 8 m (the Guard's physics sight check) and close doors that lengthen the
                    // player's route; the controller carries the door out and answers through
                    // IActionFeedback. Without a grid, or a spawn point with no squad slot (A-D), it
                    // falls back to the mock.
                    if (setup.HasGrid && setup.Identity.IsInSquad && setup.Identity.SquadIndex <= (int)SaboteurLetter.D)
                    {
                        DetourCache detours = DetourCache.For(setup.Blackboard, setup.Grid, setup.Pathfinder);
                        var candidates = new CompositeCandidateSource(
                            new AttackPlayerSource(new CoverVisibilitySight(new PhysicsCoverVisibility(setup.Grid))),
                            new CloseDoorSource(detours));
                        return new SaboteurBrain(
                            new SaboteurIdentity(setup.Identity.Id, (SaboteurLetter)setup.Identity.SquadIndex),
                            setup.Grid, setup.Pathfinder, setup.Blackboard.Claims, setup.PatrolPoints, null, 0,
                            candidates, detours);
                    }
                    return new MockPathProvider(setup.PatrolPoints);
                default:
                    return new MockPathProvider(setup.PatrolPoints);
            }
        }

        /// <summary>The body listed for <paramref name="type"/>, or the fallback prefab.</summary>
        AgentController BodyFor(AgentType type)
        {
            for (int i = 0; i < bodies.Length; i++)
                if (bodies[i].type == type && bodies[i].prefab != null)
                    return bodies[i].prefab;
            return agentPrefab;
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
