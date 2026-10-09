using System;
using System.Collections.Generic;
using Unity.Profiling;
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
    public sealed class AgentController : MonoBehaviour, IAgentState, IGameStateListener, INoiseListener, IDamageable
    {
        [Tooltip("Smooth the brain's grid paths before walking them: drop the waypoints the agent does not need, then round the corners. Only works with a level grid; untick to compare with the raw path.")]
        [SerializeField] bool smoothPaths = true;

        [Header("Taking hits")]
        [Tooltip("Hits from the player's blaster before this agent goes down.")]
        [SerializeField, Min(1)] int hitPoints = 3;

        [Tooltip("Seconds a downed agent stays knocked out before it reassembles (Tracker 7, Guard 8, Captain 6).")]
        [SerializeField, Min(0f)] float knockOutSeconds = 7f;

        [Tooltip("Scrap the agent for good when it goes down instead of knocking it out (the Saboteurs).")]
        [SerializeField] bool scrapWhenDown;

        [Header("Sabotage")]
        [Tooltip("How close (flat metres) the agent must be to a door, trap or battery to act on it. Above the Saboteur brain's own 1.5 m to the door cell, so a door registered at its hinge is still in reach.")]
        [SerializeField, Min(0.1f)] float sabotageReach = 2.5f;

        [Tooltip("Seconds a door, trap or battery request may stay out of reach before it fails.")]
        [SerializeField, Min(0f)] float sabotageGiveUpSeconds = 2f;

        // Reused for every new route, so smoothing allocates nothing once they have grown.
        readonly List<Vector3> _pulledPath = new List<Vector3>();
        readonly List<Vector3> _smoothedPath = new List<Vector3>();

        AgentPathFollower _follower;
        AgentWeapon _weapon;
        bool _facingLook;   // the body is turned to the brain's LookTarget

        // One marker per agent type around the brain's Tick, for the Profiler and the
        // four-agent stress test (AI time per frame, by brain). Indexed by AgentType.
        static readonly ProfilerMarker[] TickMarkers =
        {
            new ProfilerMarker("AI.Brain.Tick.Tracker"),
            new ProfilerMarker("AI.Brain.Tick.Guard"),
            new ProfilerMarker("AI.Brain.Tick.Saboteur"),
            new ProfilerMarker("AI.Brain.Tick.Captain"),
        };
        IAgentBrain _brain;
        IGoalPredictor _predictor;
        IActionFeedback _feedback;
        IHealthAware _health;
        WorldBlackboard _blackboard;
        GridGraph _grid;
        bool _warnedNotInitialised;
        float _rebootAt;

        // The loudest noise heard since the brain's last tick; passed in ctx.Senses, then cleared.
        SensorSnapshot _heard;
        IGameClock _clock;

        // Game time from the GameManager: it stops during cutscenes and the pause menu, so brain
        // timers and stun reboots stop with it. Test scenes without a GameManager use Unity's clock.
        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        /// <summary>True once a brain has been given to this agent.</summary>
        public bool IsInitialised => _brain != null;

        /// <summary>
        /// The brain, for debug tools only (the overlay draws what a brain is thinking). Game
        /// code must not use it: the journey and UI talk to agents through events and the
        /// blackboard, never through a brain.
        /// </summary>
        public IAgentBrain Brain => _brain;

        /// <summary>
        /// The shared blackboard, for body components that show the state of the world (the
        /// Captain's dormant kneel and dark visor read <c>CaptainAwake</c>). Null until
        /// <see cref="Initialise"/>. Body components only read it; only Runtime writers set it.
        /// </summary>
        public WorldBlackboard World => _blackboard;

        /// <summary>The body's path follower, for debug tools (the overlay draws the route left to walk).</summary>
        public AgentPathFollower Follower => _follower;

        /// <summary>The brain's current state name, for the debug overlay and "!"/"?" icons.</summary>
        public string DebugState { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public AgentIdentity Identity { get; private set; }

        /// <inheritdoc/>
        public AgentType Type => Identity.Type;

        /// <inheritdoc/>
        /// <remarks>
        /// The root's transform, read live: a scrapped body is switched off, not destroyed, so it
        /// still answers. Only if the body itself has been destroyed (its scene unloaded) does
        /// this fall back to the position saved in <c>OnDestroy</c>.
        /// </remarks>
        public Vector3 Position => this != null ? transform.position : _lastPosition;

        Vector3 _lastPosition;

        /// <inheritdoc/>
        public float Speed => _follower.CurrentSpeed;

        /// <inheritdoc/>
        public float TurnRate => _follower.TurnRate;

        /// <summary>
        /// True while attacking: aiming or firing with the <see cref="AgentWeapon"/>, or, for a body
        /// without one, while the brain's action is <see cref="AgentAction.Shoot"/>.
        /// </summary>
        public bool IsAttacking => _weapon != null ? _weapon.IsBusy : _shootRequested;

        bool _shootRequested;

        // The brain's action on its previous tick: a door, trap or battery request starts when
        // the (action, target) pair changes, so a brain that repeats it every tick asks once.
        AgentAction _lastAction;
        int _lastTargetId;

        // The request waiting to be carried out; None when there is none.
        AgentAction _sabotageAction;
        int _sabotageTarget;
        float _sabotageSince;

        /// <summary>Hits left before this agent goes down. Back to full when it reboots.</summary>
        public int HitPointsLeft { get; private set; }

        /// <summary>Hits this agent can take, for the HUD and tests.</summary>
        public int MaxHitPoints => hitPoints;

        /// <summary>
        /// Raised for each hit that counts (<see cref="TakeHit"/>), before the agent goes down
        /// if it was the last hit point. The body's hit effects listen.
        /// </summary>
        public event Action Hit;

        /// <summary>True once <see cref="Scrap"/> has been called. Never becomes false again.</summary>
        public bool IsDead { get; private set; }

        /// <summary>True while knocked out by <see cref="Disable"/>, until it reboots.</summary>
        public bool IsDisabled { get; private set; }

        /// <summary>Seconds of game time until a knocked-out agent reboots; 0 when it is not down.</summary>
        public float KnockOutTimeLeft => IsDisabled ? Mathf.Max(0f, _rebootAt - Now) : 0f;

        /// <summary>
        /// How aware the agent is of the player, for the "?"/"!" icon: the brain's
        /// <see cref="AgentIntent.Alert"/>, or worked out from its state name if the brain does
        /// not set one. None while down, scrapped or frozen.
        /// </summary>
        public AlertLevel Alert => IsDead || IsDisabled || IsFrozen ? AlertLevel.None : _alert;

        AlertLevel _alert;

        /// <summary>
        /// The brain's wind-up energy, for the key animation, or null if the brain has no
        /// key. Found once in <see cref="Initialise"/>, not looked up every frame.
        /// </summary>
        public IWindUpState WindUp { get; private set; }

        /// <inheritdoc/>
        public Vector3 HearingPosition => transform.position;

        /// <inheritdoc/>
        public bool CanHear => !IsDead && !IsDisabled;

        /// <summary>
        /// A noise reached this agent. Several noises between two brain ticks keep the loudest
        /// (<see cref="SensorSnapshot.Loudest"/>); the brain receives it in
        /// <see cref="AgentContext.Senses"/> on its next tick, once.
        /// </summary>
        public void Hear(in SensorSnapshot heard) => _heard = SensorSnapshot.Loudest(_heard, heard);

        void Awake()
        {
            _follower = GetComponent<AgentPathFollower>();
            _weapon = GetComponent<AgentWeapon>();
            HitPointsLeft = hitPoints;
        }

        /// <summary>
        /// One hit from the player's blaster (S2's <see cref="IDamageable"/>). Each hit costs a hit
        /// point; at 0 the agent goes down: scrapped for good if it is a Saboteur, otherwise knocked
        /// out for <c>knockOutSeconds</c>, after which it reboots with full hit points. Hits on an
        /// agent that is already down, frozen in a cutscene or scrapped are ignored.
        /// </summary>
        public void TakeHit()
        {
            if (IsDead || IsDisabled || IsFrozen || HitPointsLeft <= 0)
                return;

            HitPointsLeft--;
            Hit?.Invoke();
            _health?.OnHealthChanged(HitPointsLeft, hitPoints);
            if (HitPointsLeft > 0)
                return;

            if (scrapWhenDown)
                Scrap();
            else
                Disable(knockOutSeconds);
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
            _predictor = brain as IGoalPredictor;
            WindUp = brain as IWindUpState;
            _feedback = brain as IActionFeedback;
            _health = brain as IHealthAware;
            _lastAction = AgentAction.None;
            _sabotageAction = AgentAction.None;
            _health?.OnHealthChanged(HitPointsLeft, hitPoints);

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
            HitPointsLeft = 0;
            _shootRequested = false;
            _sabotageAction = AgentAction.None;   // the brain is released below; no answer
            _weapon?.Cancel();
            _follower.Stop();
            ReleaseLook();

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
            _shootRequested = false;
            _weapon?.Cancel();
            _heard = default; // a noise from before the knock-out is stale by the reboot
            _follower.Stop();
            ReleaseLook();   // a downed body does not turn
            ResolveSabotage(false);         // went down before it got there
            _lastAction = AgentAction.None; // a request repeated after the reboot is a new one
            _brain?.OnStunned(_rebootAt - Now);
            if (_predictor != null)
                _blackboard.SetPredictedGoal(_predictor.Prediction);   // not ticked until the reboot

            if (!wasDisabled)
                AgentEvents.RaiseDisabled(this);
        }

        void Reboot()
        {
            IsDisabled = false;
            HitPointsLeft = hitPoints;
            _health?.OnHealthChanged(HitPointsLeft, hitPoints);
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
                Now, _blackboard, _heard);
            _heard = default; // each noise reaches the brain once

            AgentIntent intent;
            int type = (int)Type;
            using (TickMarkers[type < TickMarkers.Length ? type : 0].Auto())
                intent = _brain.Tick(context);

            // Brains never write the blackboard: copy the Captain's goal prediction for the others.
            if (_predictor != null)
                _blackboard.SetPredictedGoal(_predictor.Prediction);

            ApplyPath(intent);
            ApplyLook(intent);
            ApplyAction(intent);
            DebugState = intent.DebugState ?? string.Empty;
            _alert = intent.Alert != AlertLevel.None ? intent.Alert : AlertFromState.For(DebugState);
        }

        void OnDestroy()
        {
            // A listener may still hold this agent as an IAgentState after its scene unloads.
            _lastPosition = transform.position;

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
            WindUp = null;
            _feedback = null;
            _health = null;
            brain.OnDestroyed();

            // A prediction from a brain that has gone must not outlive it.
            if (_predictor != null)
            {
                _predictor = null;
                _blackboard?.SetPredictedGoal(default);
            }
        }

        // The grid cell under the agent, for brains to start searches from. Without a grid it
        // stays (0, 0). A cell outside the grid is passed on as is; brains snap it to the
        // nearest walkable cell, which also covers an agent standing on a box.
        Vector2Int CurrentCell(Vector3 position) =>
            _grid != null ? _grid.WorldToCell(position) : Vector2Int.zero;

        // Carries out the brain's action. Shoot goes to the weapon, which aims for 0.3 s (the
        // telegraph) before the hitscan; a request while it is still busy is ignored. A door,
        // trap or battery action is a request that starts when the (action, target) pair changes
        // and stays open until it is carried out or given up, even if the brain moves on.
        void ApplyAction(in AgentIntent intent)
        {
            _shootRequested = intent.Action == AgentAction.Shoot;
            if (_shootRequested && _weapon != null)
                _weapon.RequestShot();

            bool fresh = intent.Action != _lastAction || intent.ActionTargetId != _lastTargetId;
            _lastAction = intent.Action;
            _lastTargetId = intent.ActionTargetId;
            if (fresh && TryKindOf(intent.Action, out _))
            {
                ResolveSabotage(false);   // a newer request replaces one still waiting
                _sabotageAction = intent.Action;
                _sabotageTarget = intent.ActionTargetId;
                _sabotageSince = Now;
            }
            TrySabotage();
        }

        // Every request gets exactly one answer: carried out within reach, or failed because the
        // target is not registered (now or any more), it stayed out of reach for
        // sabotageGiveUpSeconds, a newer request replaced it, or the agent went down.
        void TrySabotage()
        {
            if (!TryKindOf(_sabotageAction, out SabotageKind kind))
                return;

            if (!SabotageTargets.TryGet(kind, _sabotageTarget, out ISabotageable target, out Transform where))
            {
                ResolveSabotage(false);
                return;
            }

            Vector3 offset = where.position - transform.position;
            offset.y = 0f;   // a battery on a shelf is still in reach from the floor
            if (offset.sqrMagnitude <= sabotageReach * sabotageReach)
            {
                target.Execute();
                ResolveSabotage(true);
            }
            else if (Now - _sabotageSince > sabotageGiveUpSeconds)
            {
                ResolveSabotage(false);
            }
        }

        void ResolveSabotage(bool success)
        {
            if (_sabotageAction == AgentAction.None)
                return;

            // Cleared first, so a brain that asks again from inside the callback starts afresh.
            AgentAction action = _sabotageAction;
            _sabotageAction = AgentAction.None;
            _feedback?.OnActionResolved(action, _sabotageTarget, success);
        }

        static bool TryKindOf(AgentAction action, out SabotageKind kind)
        {
            switch (action)
            {
                case AgentAction.CloseDoor:
                    kind = SabotageKind.Door;
                    return true;
                case AgentAction.ArmTrap:
                    kind = SabotageKind.Trap;
                    return true;
                case AgentAction.StealBattery:
                    kind = SabotageKind.Battery;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        // The brain's LookTarget, honoured while the body stands still: a Captain in ambush
        // faces the way the player will come, a watching agent faces the player. Vision uses
        // the body's facing, so this is what lets them see what they are looking for. While
        // walking the body faces where it goes (no walking sideways), and while the weapon
        // aims it turns the body itself, so the weapon wins.
        void ReleaseLook()
        {
            if (_facingLook)
                _follower.StopFacing();
            _facingLook = false;
        }

        void ApplyLook(in AgentIntent intent)
        {
            if (_weapon != null && _weapon.IsBusy)
            {
                _facingLook = false;   // the weapon owns the facing and releases it when done
                return;
            }
            if (intent.LookTarget.HasValue && !_follower.HasPath)
            {
                _follower.FaceTowards(intent.LookTarget.Value);
                _facingLook = true;
            }
            else if (_facingLook)
            {
                _follower.StopFacing();
                _facingLook = false;
            }
        }

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
