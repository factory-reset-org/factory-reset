using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;
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
    public sealed class AgentController : MonoBehaviour, IAgentState, IGameStateListener
    {
        [Tooltip("Smooth the brain's grid paths before walking them: drop the waypoints the agent does not need, then round the corners. Only works with a level grid; untick to compare with the raw path.")]
        [SerializeField] bool smoothPaths = true;

        // Reused for every new route, so smoothing allocates nothing once they have grown.
        readonly List<Vector3> _pulledPath = new List<Vector3>();
        readonly List<Vector3> _smoothedPath = new List<Vector3>();

        AgentPathFollower _follower;
        IAgentBrain _brain;
        WorldBlackboard _blackboard;
        GridGraph _grid;
        bool _warnedNotInitialised;
        float _rebootAt;
        IGameClock _clock;

        // Game time from the GameManager: it stops during cutscenes and the pause menu, so brain
        // timers and stun reboots stop with it. Test scenes without a GameManager use Unity's clock.
        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        /// <summary>True once a brain has been given to this agent.</summary>
        public bool IsInitialised => _brain != null;

        /// <summary>The brain's current state name, for the debug overlay and "!"/"?" icons.</summary>
        public string DebugState { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public AgentIdentity Identity { get; private set; }

        /// <inheritdoc/>
        public AgentType Type => Identity.Type;

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
        /// Gives this agent its identity, its brain and the shared world blackboard. Called
        /// once by the spawner, so nothing has to be looked up at runtime.
        /// </summary>
        public void Initialise(AgentIdentity identity, IAgentBrain brain, WorldBlackboard blackboard,
            GridGraph grid = null)
        {
            Identity = identity;
            _brain = brain ?? throw new ArgumentNullException(nameof(brain));
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));

            if (_grid != null)
                _grid.Changed -= HandleGridChanged;
            _grid = grid;
            if (_grid != null)
                _grid.Changed += HandleGridChanged;

            // Follow the game state, so the agent freezes in cutscenes and the pause menu.
            // Test scenes without a GameManager have no clock and simply always play.
            if (_clock == null && GameClock.Current != null)
            {
                _clock = GameClock.Current;
                _clock.AddListener(this);
                SetFrozen(_clock.State != GameState.Playing);
            }
        }

        /// <summary>True while the game is not in the Playing state: the brain is not ticked and the body holds still.</summary>
        public bool IsFrozen { get; private set; }

        /// <inheritdoc/>
        public void OnGameStateChanged(GameState previous, GameState current)
        {
            SetFrozen(current != GameState.Playing);
        }

        // Pausing the follower component (rather than stopping it) keeps its route, so the agent
        // carries on along the same path when play resumes, without asking the brain again.
        void SetFrozen(bool frozen)
        {
            IsFrozen = frozen;
            _follower.enabled = !frozen;
        }

        /// <summary>
        /// Scraps this agent for good: it stops where it is, its brain is told through
        /// <see cref="IAgentBrain.OnDestroyed"/> and never ticked again, and
        /// <see cref="AgentEvents.OnDestroyed"/> is raised once. Later calls do nothing.
        /// </summary>
        public void Scrap()
        {
            if (IsDead)
                return;

            IsDead = true;
            IsAttacking = false;
            _follower.Stop();

            // Release the brain's claims before the event, so listeners such as the
            // Saboteur squad already see the freed targets when they react.
            ReleaseBrain();
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
            _rebootAt = wasDisabled ? Mathf.Max(_rebootAt, Now + duration) : Now + duration;
            IsDisabled = true;
            IsAttacking = false;
            _follower.Stop();
            _brain?.OnStunned(_rebootAt - Now);

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
            if (IsDead || IsFrozen)
                return;

            if (IsDisabled)
            {
                if (Now < _rebootAt)
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

            Vector3 position = transform.position;
            var context = new AgentContext(CurrentCell(position), position, transform.forward,
                Now, _blackboard, new SensorSnapshot());

            AgentIntent intent = _brain.Tick(context);

            ApplyPath(intent);
            IsAttacking = intent.Action == AgentAction.Shoot;
            DebugState = intent.DebugState ?? string.Empty;
        }

        void OnDestroy()
        {
            // The game clock outlives agents too.
            _clock?.RemoveListener(this);

            // The grid outlives this agent, so stop listening or it would keep calling a destroyed object.
            if (_grid != null)
                _grid.Changed -= HandleGridChanged;
            ReleaseBrain();
        }

        // A door opened or closed, or a box moved: tell the brain which cells changed so it can
        // replan if its route crosses them. Also sent while knocked out, so the brain's next plan
        // after the reboot already knows. A scrapped agent's brain has been released and hears nothing.
        void HandleGridChanged(GridChange change)
        {
            if (_brain == null || IsDead)
                return;
            _brain.OnGraphChanged(change.ChangedCells);
        }

        void ReleaseBrain()
        {
            if (_brain == null)
                return;

            // Clear the reference first so the brain is told exactly once and never ticked again.
            IAgentBrain brain = _brain;
            _brain = null;
            brain.OnDestroyed();
        }

        // The grid cell under the agent, for brains to start searches from. Without a grid it
        // stays (0, 0). A cell outside the grid is passed on as is; brains snap it to the
        // nearest walkable cell, which also covers an agent standing on a box.
        Vector2Int CurrentCell(Vector3 position) =>
            _grid != null ? _grid.WorldToCell(position) : Vector2Int.zero;

        void ApplyPath(in AgentIntent intent)
        {
            // Null means "keep following the current path": nothing to do.
            if (intent.Path == null)
                return;

            if (intent.Path.Count == 0)
            {
                _follower.Stop();
            }
            else if (smoothPaths && _grid != null && intent.Path.Count > 2)
            {
                // Both stages keep the first and last waypoints and never cross a cell the
                // brain's path avoided, so the brain's route is still respected.
                PathSmoother.StringPull(_grid, intent.Path, _pulledPath);
                PathSmoother.CatmullRom(_grid, _pulledPath, _smoothedPath);
                _follower.SetPath(_smoothedPath, intent.DesiredSpeed);
            }
            else
            {
                _follower.SetPath(intent.Path, intent.DesiredSpeed);
            }
        }
    }
}
