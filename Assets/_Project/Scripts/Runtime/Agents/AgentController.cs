using System;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Perception;
using ToyFactory.Runtime.Movement;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Connects one pure-C# brain to one agent body. Every frame it builds the brain's
    /// <see cref="AgentContext"/>, asks the brain what to do, and hands the answer to the
    /// body. This is the only place brain and body meet, which keeps the AI decoupled from
    /// Unity objects: the brain never touches a GameObject and the body never decides.
    /// </summary>
    [RequireComponent(typeof(AgentPathFollower))]
    public sealed class AgentController : MonoBehaviour
    {
        AgentPathFollower _follower;
        IAgentBrain _brain;
        WorldBlackboard _blackboard;
        bool _warnedNotInitialised;

        /// <summary>True once a brain has been given to this agent.</summary>
        public bool IsInitialised => _brain != null;

        /// <summary>The brain's current state name, for the debug overlay and "!"/"?" icons.</summary>
        public string DebugState { get; private set; } = string.Empty;

        void Awake()
        {
            _follower = GetComponent<AgentPathFollower>();
        }

        /// <summary>
        /// Gives this agent its brain and the shared world blackboard. Called once by the
        /// spawner, so nothing has to be looked up at runtime.
        /// </summary>
        public void Initialise(IAgentBrain brain, WorldBlackboard blackboard)
        {
            _brain = brain ?? throw new ArgumentNullException(nameof(brain));
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        }

        void Update()
        {
            if (_brain == null)
            {
                // Warn once rather than every frame, and do nothing rather than throw.
                if (!_warnedNotInitialised)
                {
                    Debug.LogWarning($"{nameof(AgentController)} on {name} has no brain; call {nameof(Initialise)} first.", this);
                    _warnedNotInitialised = true;
                }
                return;
            }

            // The cell stays at zero until the grid exists; no brain uses it yet.
            var context = new AgentContext(Vector2Int.zero, transform.position, transform.forward,
                Time.time, _blackboard, new SensorSnapshot());

            AgentIntent intent = _brain.Tick(context);

            ApplyPath(intent);
            DebugState = intent.DebugState ?? string.Empty;
        }

        void ApplyPath(in AgentIntent intent)
        {
            // Null means "keep following the current path": nothing to do.
            if (intent.Path == null)
                return;

            if (intent.Path.Count == 0)
                _follower.Stop();
            else
                _follower.SetPath(intent.Path, intent.DesiredSpeed);
        }
    }
}
