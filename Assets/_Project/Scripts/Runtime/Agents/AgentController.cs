using System;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Perception;
using ToyFactory.Interfaces;
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
    public sealed class AgentController : MonoBehaviour, IAgentState
    {
        AgentPathFollower _follower;
        IAgentBrain _brain;
        WorldBlackboard _blackboard;
        bool _warnedNotInitialised;
        float _rebootAt;

        /// <summary>True once a brain has been given to this agent.</summary>
        public bool IsInitialised => _brain != null;

        /// <summary>The brain's current state name, for the debug overlay and "!"/"?" icons.</summary>
        public string DebugState { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public AgentType Type { get; private set; }

        /// <inheritdoc/>
        public float Speed => _follower.CurrentSpeed;

        /// <inheritdoc/>
        public float TurnRate => _follower.TurnRate;

        /// <summary>True while the brain's current action is <see cref="AgentAction.Shoot"/>.</summary>
        public bool IsAttacking { get; private set; }

        /// <summary>True once <see cref="Scrap"/> has been called. Never becomes false again.</summary>
        public bool IsDead { get; private set; }

        /// <summary>True while knocked out by <see cref="Disable"/>, until it reboots.</summary>
        public bool IsDisabled { get; private set; }

        void Awake()
        {
            _follower = GetComponent<AgentPathFollower>();
        }

        /// <summary>
        /// Gives this agent its type, its brain and the shared world blackboard. Called once
        /// by the spawner, so nothing has to be looked up at runtime.
        /// </summary>
        public void Initialise(AgentType type, IAgentBrain brain, WorldBlackboard blackboard)
        {
            Type = type;
            _brain = brain ?? throw new ArgumentNullException(nameof(brain));
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        }

        /// <summary>
        /// Scraps this agent for good: it stops where it is, its brain is never ticked again,
        /// and <see cref="AgentEvents.OnDestroyed"/> is raised once. Later calls do nothing.
        /// </summary>
        public void Scrap()
        {
            if (IsDead)
                return;

            IsDead = true;
            IsAttacking = false;
            _follower.Stop();
            AgentEvents.RaiseDestroyed(this);
        }

        /// <summary>
        /// Knocks this agent out for <paramref name="duration"/> seconds. It stops, its brain
        /// is told through <see cref="IAgentBrain.OnStunned"/> and is not ticked, and
        /// <see cref="AgentEvents.OnDisabled"/> is raised. It reboots by itself when the time
        /// is up. A hit while already disabled can only extend the time. Ignored once scrapped.
        /// </summary>
        public void Disable(float duration)
        {
            if (IsDead)
                return;

            bool wasDisabled = IsDisabled;
            _rebootAt = wasDisabled ? Mathf.Max(_rebootAt, Time.time + duration) : Time.time + duration;
            IsDisabled = true;
            IsAttacking = false;
            _follower.Stop();
            _brain?.OnStunned(_rebootAt - Time.time);

            if (!wasDisabled)
                AgentEvents.RaiseDisabled(this);
        }

        void Reboot()
        {
            IsDisabled = false;
            AgentEvents.RaiseRebooted(this);
        }

        void Update()
        {
            if (IsDead)
                return;

            if (IsDisabled)
            {
                if (Time.time < _rebootAt)
                    return;
                Reboot();
            }

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
            IsAttacking = intent.Action == AgentAction.Shoot;
            DebugState = intent.DebugState ?? string.Empty;
        }

        void OnDestroy()
        {
            if (_brain == null)
                return;

            // Clear the reference first so the brain is told exactly once and never ticked again.
            IAgentBrain brain = _brain;
            _brain = null;
            brain.OnDestroyed();
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
