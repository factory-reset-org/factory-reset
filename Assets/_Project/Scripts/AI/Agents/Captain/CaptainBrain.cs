using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.FSM;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>
    /// The Captain: a boss that predicts where the player is going and gets there first.
    /// A data-driven FSM (Dormant, Observe, Intercept, Ambush, Engage, Pursue, Reassess, Stunned) on
    /// the shared framework, driven by <see cref="GoalInference"/> (which goal the player is
    /// heading for) and <see cref="InterceptPlanner"/> (where to wait for them).
    /// </summary>
    /// <remarks>
    /// <para><b>Decisions at 2 Hz.</b> Every 0.5 s of game time the brain records the player's
    /// cell, re-runs goal inference and, when the most likely goal has a posterior of at
    /// least 0.5, plans an intercept. When the top two goals are within 0.1 it plans a
    /// chokepoint shared by both routes instead. The states only act on the result, so no
    /// state runs a search of its own except the A* walk to the chosen cell.</para>
    /// <para><b>Waking.</b> The Captain sleeps until the Chapter 3 cutscene's wake signal
    /// (<see cref="WorldBlackboard.CaptainAwake"/>). As a safety net it also wakes once
    /// Chapter 3 has started, so it can never stay asleep for the boss chapters.</para>
    /// <para><b>Prediction for the team.</b> The latest prediction is <see cref="Prediction"/>;
    /// the runtime copies it to <see cref="WorldBlackboard.PredictedGoal"/> for the Saboteurs.</para>
    /// <para><b>Time.</b> Every timer uses <c>ctx.Time</c> (game time), so cutscenes and pause
    /// freeze the 5 s history and the 2 Hz clock. Stuns are owned by the controller.</para>
    /// </remarks>
    public sealed partial class CaptainBrain : IAgentBrain, IGoalPredictor, IActionFeedback
    {
        // Speeds in m/s. 4.6 is the prototype's Captain speed, used by the intercept timing.
        public const float InterceptSpeed = 4.6f;
        public const float ObserveSpeed = 2.5f;

        // Decisions.
        public const float DecisionInterval = 0.5f;      // 2 Hz
        public const float ConfidenceThreshold = 0.5f;   // commit to an intercept at P(g*) >= 0.5
        public const float SharedGoalGap = 0.1f;         // top two goals this close: plan for both
        public const float DefaultPlayerSprint = 7f;     // if the player reports no sprint speed

        // Commitment (hysteresis, like the fight's): a confidence or a goal hovering at the edge
        // must not flip the Captain between intercepting and watching every 0.5 s. It commits
        // at 0.5 but keeps a plan it is already following down to 0.4, and it only switches to
        // another goal once that goal leads the committed one by 0.15.
        public const float KeepConfidence = 0.4f;
        public const float GoalSwitchMargin = 0.15f;

        // Progress: heading for a target but not moving 0.25 m in 2 s (pinned on a prop, a jam
        // of bodies, no route) gives the target up; its cell and neighbours are not chosen
        // again for 10 s.
        public const float StuckTime = 2f;
        public const float ProgressStep = 0.25f;
        public const float AvoidSeconds = 10f;

        // Doors: it opens a closed door on its route from 1.5 m (the Saboteur's reach to a door
        // cell; the body's reach to the door is 2.5 m), waits up to 3 s for it to swing open,
        // and gives up on a door that would not open for 10 s. Passing through, it shuts a door
        // behind it when that door is on the player's predicted route, at 1-2.3 m past it, if
        // the player is at least 3 m from it.
        public const float DoorReach = 1.5f;
        public const float DoorWaitTimeout = 3f;
        public const float CloseBehindMin = 1f;
        public const float CloseBehindMax = 2.3f;
        public const float CloseBehindPlayerClearance = 3f;

        // Vision: a fight starts when the player is in view within 10 m.
        public const float EngageRange = 10f;
        public const float VisionHalfAngle = 70f;
        public const float ProximityRange = 2.5f;        // felt all round at close range
        public const float SightEndInset = 0.8f;         // same grid line-of-sight rule as the Tracker

        // Staying in a fight (hysteresis, so the edge of the 10 m range cannot flip it on and
        // off): once fighting, the Captain keeps contact with the player in line of sight out
        // to 14 m in any direction, stays engaged for at least 2 s, and only after 0.7 s with
        // no contact goes to where it last saw them. It searches there briefly, then predicts
        // again.
        public const float KeepContactRange = 14f;
        public const float MinEngageTime = 2f;
        public const float LoseSightDelay = 0.7f;
        public const float PursueTimeout = 5f;
        public const float LookAroundTime = 1f;

        // Shooting: one shot every 1.2 s. The body's weapon adds the 0.3 s aim telegraph
        // before each, the same for every agent, so the brain only decides when to shoot.
        public const float FireInterval = 1.2f;

        // Distances, metres.
        public const float ArrivalRadius = 0.6f;
        public const float AmbushLeaveDistance = 1.5f;   // pushed this far off its cell, it re-plans
        public const float ObserveDistance = 8f;         // keeps at least this far while unsure
        public const float RetreatStep = 4f;
        public const float GoalReachedRadius = 1f;       // the player is at g*
        public const float GoalReleaseDistance = 4f;     // a reached goal counts again once the player leaves it
        public const int NearestCellRadius = 6;

        /// <summary>The chapter whose start wakes the Captain if the wake signal was missed.</summary>
        public const int WakeChapter = 3;

        const int NoGoal = int.MinValue;

        readonly GridGraph _grid;
        readonly WorldBlackboard _blackboard;
        readonly IPathfinder _pathfinder;
        readonly GoalInference _inference;
        readonly InterceptPlanner _planner;
        readonly PlayerTrack _track = new PlayerTrack();
        readonly List<CandidateGoal> _goals = new List<CandidateGoal>();
        readonly bool _startAwake;

        readonly StateMachine<CaptainBrain> _machine;
        readonly IState<CaptainBrain> _dormant;
        readonly IState<CaptainBrain> _observe;
        readonly IState<CaptainBrain> _intercept;
        readonly IState<CaptainBrain> _ambush;
        readonly IState<CaptainBrain> _engage;
        readonly IState<CaptainBrain> _pursue;
        readonly IState<CaptainBrain> _reassess;
        readonly IState<CaptainBrain> _stunned;
        readonly IState<CaptainBrain> _converge;
        readonly List<string> _labels = new List<string>();

        // Cells the Captain could not reach (see StuckTime), and until when they are avoided.
        readonly Dictionary<Vector2Int, float> _avoidUntil = new Dictionary<Vector2Int, float>();
        readonly Predicate<Vector2Int> _isAvoided;

        // Progress towards the current target, for the stuck check.
        bool _progressTracking;
        Vector3 _progressTarget;
        Vector3 _progressPoint;
        float _progressAt;

        // Converge: the player's cell it last routed to, and until when it is not tried again
        // after getting stuck on the way.
        Vector2Int _convergeCell;
        float _convergeBlockedUntil = float.NegativeInfinity;

        // Doors: the router, doors it failed to open and until when, the door it is waiting
        // at, and the door it opened and may shut behind it.
        readonly DoorRouter _doors;
        readonly Dictionary<int, float> _blockedDoorsUntil = new Dictionary<int, float>();
        readonly Predicate<int> _isDoorBlocked;
        const int NoDoor = int.MinValue;
        int _waitDoorId = NoDoor;
        Vector2Int _waitDoorCell;
        float _waitDoorSince;
        int _openedDoorId = NoDoor;
        Vector2Int _openedDoorCell;
        bool _closeRequested;

        // This tick's input.
        AgentContext _ctx;
        float Now => _ctx.Time;

        // Perception. Seeing starts a fight; contact (line of sight within 14 m, any
        // direction) keeps one going.
        bool _seesPlayer;
        bool _inContact;
        float _lastContactTime = float.NegativeInfinity;
        Vector3 _lastContactPosition;
        Vector3 _lastContactHeading;
        float _engagedAt;
        bool _pursueOver;

        // Prediction and plan, refreshed by Decide at 2 Hz.
        float _nextDecisionTime = float.NegativeInfinity;
        bool _decideNow;
        bool _decidedThisTick;
        PredictedGoal _prediction;
        InterceptPlan _plan;
        int _planGoalId = NoGoal;
        Vector2Int _approachCell;

        // The cell and goal the Captain committed to in Intercept and holds in Ambush.
        Vector2Int _targetCell;
        int _targetGoalId = NoGoal;
        int _ignoredGoalId = NoGoal;

        // Flags the states and callbacks raise for the transition table.
        bool _stunPending;
        bool _planInvalid;

        // Current route and this tick's output.
        List<Vector2Int> _routeCells;
        Vector2Int _routeGoal;
        bool _replanRequested;
        List<Vector3> _outPath;
        float _outSpeed;
        Vector3? _outLook;
        AgentAction _outAction;
        int _outTargetId;
        string _debugState = "Dormant";

        /// <param name="grid">The level grid.</param>
        /// <param name="pathfinder">The shared A* (one is created if null).</param>
        /// <param name="blackboard">The shared blackboard, read for the player, objectives, chapter and wake flag.</param>
        /// <param name="startAwake">Skip Dormant: for test scenes without the cutscene that wakes it.</param>
        public CaptainBrain(GridGraph grid, IPathfinder pathfinder, WorldBlackboard blackboard, bool startAwake = false)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            _pathfinder = pathfinder ?? new AStarSearch(grid);
            _inference = new GoalInference(grid);
            _planner = new InterceptPlanner(grid);
            _startAwake = startAwake;

            _dormant = new DormantState();
            _observe = new ObserveState();
            _intercept = new InterceptState();
            _ambush = new AmbushState();
            _engage = new EngageState();
            _pursue = new PursueState();
            _reassess = new ReassessState();
            _stunned = new StunnedState();
            _converge = new ConvergeState();
            _isAvoided = IsAvoided;
            _doors = new DoorRouter(grid);
            _isDoorBlocked = IsDoorBlocked;

            var rules = new List<Transition<CaptainBrain>>();
            Rule(rules, _dormant, _observe, 110, "wake signal (or Chapter 3 started, or spawned awake)", b => b.IsAwake);
            Rule(rules, null, _stunned, 100, "stunned (first tick after the reboot)",
                b => b._stunPending && b._machine.Current != b._stunned && b._machine.Current != b._dormant);
            Rule(rules, _stunned, _reassess, 90, "stun over: the old prediction is stale", b => !b._stunPending);
            Rule(rules, _observe, _engage, 80, "player visible within 10 m", b => b._seesPlayer);
            Rule(rules, _intercept, _engage, 80, "player visible within 10 m", b => b._seesPlayer);
            Rule(rules, _ambush, _engage, 80, "player visible within 10 m", b => b._seesPlayer);
            Rule(rules, _converge, _engage, 80, "player visible within 10 m", b => b._seesPlayer);
            Rule(rules, _pursue, _engage, 80, "contact again (line of sight within 14 m)", b => b._inContact);
            Rule(rules, _engage, _reassess, 75, "the player is gone (dead or missing)", b => !b.PlayerAvailable());
            Rule(rules, _engage, _pursue, 70, "no contact for 0.7 s, after at least 2 s engaged",
                b => b.EngagedFor(MinEngageTime) && b.LostContactFor(LoseSightDelay));
            Rule(rules, _pursue, _reassess, 65, "searched the last-seen spot, 5 s passed, or the player is gone",
                b => b._pursueOver || !b.PlayerAvailable());
            Rule(rules, _intercept, _reassess, 60, "plan invalid: g* changed, confidence < 0.5, player at g*, cell blocked",
                b => b._planInvalid);
            Rule(rules, _ambush, _reassess, 60, "plan invalid: g* changed, confidence < 0.4, player at g* or past the cell",
                b => b._planInvalid);
            Rule(rules, _converge, _reassess, 60, "the player left the goal, or the way to them is blocked",
                b => !b.PlayerBusyAtGoal);
            Rule(rules, _intercept, _ambush, 50, "reached the intercept cell", b => b.ArrivedAt(b._targetCell));
            Rule(rules, _reassess, _converge, 45, "the player is busy at a goal: close in", b => b.PlayerBusyAtGoal);
            Rule(rules, _reassess, _intercept, 40, "a plan exists (confidence >= 0.5)", b => b._plan.HasPlan);
            Rule(rules, _reassess, _observe, 30, "no plan (confidence < 0.5, or no shared chokepoint)", b => !b._plan.HasPlan);
            Rule(rules, _observe, _converge, 25, "the player is busy at a goal: close in", b => b.PlayerBusyAtGoal);
            Rule(rules, _observe, _intercept, 20, "a plan exists (confidence >= 0.5)", b => b._plan.HasPlan);

            _machine = new StateMachine<CaptainBrain>(startAwake ? _observe : _dormant, rules);
        }

        // ---- IAgentBrain ---------------------------------------------------------------

        public AgentIntent Tick(in AgentContext ctx)
        {
            _ctx = ctx;
            _outPath = null;
            _outLook = null;
            _outAction = AgentAction.None;
            _outTargetId = 0;
            _decidedThisTick = false;

            if (_machine.Current != _dormant)
            {
                Perceive();
                if (_decideNow || Now >= _nextDecisionTime)
                    Decide();
                else
                    // A frame with no decision: repair one goal field a grid change left
                    // stale, so a pushed box costs one repair per frame, not all at once.
                    _inference.RefreshOneStaleField();
            }

            _machine.Tick(this);
            // Stunned and Reassess are pass-through states: entered and left on the same tick,
            // so the Captain acts on its fresh decision straight away.
            for (int i = 0; i < 2 && (_machine.Current == _stunned || _machine.Current == _reassess); i++)
                _machine.Tick(this);

            if (_replanRequested && _outPath == null && _routeCells != null)
                MoveTo(_routeGoal);
            _replanRequested = false;

            HandleDoors();

            return new AgentIntent
            {
                Path = _outPath,
                DesiredSpeed = _outSpeed,
                LookTarget = _outLook,
                Action = _outAction,
                ActionTargetId = _outTargetId,
                DebugState = _debugState,
                Alert = CurrentAlert()
            };
        }

        // "!" once it has committed to the player (cutting them off, waiting, fighting);
        // "?" while it watches and re-predicts; nothing while asleep, down or with no player.
        AlertLevel CurrentAlert()
        {
            IState<CaptainBrain> state = _machine.Current;
            if (state == _engage || state == _pursue || state == _intercept || state == _ambush || state == _converge)
                return AlertLevel.Alert;
            if ((state == _observe || state == _reassess) && PlayerAvailable())
                return AlertLevel.Suspicious;
            return AlertLevel.None;
        }

        /// <summary>
        /// A door or box changed the grid. Goal fields go stale on their own (grid version)
        /// and are repaired one per frame, the predicted goal's first; the next decision is
        /// brought forward to this tick. The route is replanned if it crosses a changed cell,
        /// and the plan is dropped if the cell the Captain is heading for became blocked.
        /// </summary>
        public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells)
        {
            if (changedCells == null)
                return;
            _decideNow = true;
            for (int i = 0; i < changedCells.Count; i++)
            {
                Vector2Int cell = changedCells[i];
                if (_plan.HasPlan && cell == _plan.Cell && !_grid.IsTraversable(cell))
                    _planInvalid = true;
                if (_routeCells != null && _routeCells.Contains(cell))
                    _replanRequested = true;
            }
        }

        /// <summary>
        /// The controller owns the stun. Drop the plan and the prediction: by the reboot
        /// (6 s) the player has moved on, so the first tick after it reassesses from scratch.
        /// A dormant Captain cannot be stunned awake.
        /// </summary>
        public void OnStunned(float duration)
        {
            _routeCells = null;
            _plan = InterceptPlan.None;
            _prediction = default;
            _waitDoorId = NoDoor;
            _openedDoorId = NoDoor;
            if (_machine.Current != _dormant)
                _stunPending = true;
        }

        /// <summary>Nothing to release: the Captain holds no claims or reservations. It is never scrapped.</summary>
        public void OnDestroyed()
        {
            _prediction = default;
        }

        /// <inheritdoc/>
        public PredictedGoal Prediction => _prediction;

        // ---- Read-only state for the debug overlay and tests ---------------------------

        /// <summary>Current state name, e.g. "Ambush"; also the intent's DebugState.</summary>
        public string StateName => _debugState;

        /// <summary>True once woken (signal, Chapter 3, or spawned awake).</summary>
        public bool IsAwake => _startAwake || World.CaptainAwake || World.ChapterIndex >= WakeChapter;

        /// <summary>P(g*) from the last decision, 0 with no prediction.</summary>
        public float Confidence => _prediction.IsKnown ? _prediction.Confidence : 0f;

        /// <summary>The intercept from the last decision; <see cref="InterceptPlan.HasPlan"/> is false with none.</summary>
        public InterceptPlan Plan => _plan;

        /// <summary>Candidate goals of the last decision, for the debug overlay.</summary>
        public IReadOnlyList<CandidateGoal> Goals => _goals;

        /// <summary>
        /// P(g | movement) of the goal at <paramref name="index"/> in <see cref="Goals"/> from the
        /// last decision; 0 when there is no prediction.
        /// </summary>
        public float GoalProbability(int index) =>
            _prediction.IsKnown && index >= 0 && index < _inference.GoalCount && index < _goals.Count
                ? _inference.Posterior(index) : 0f;

        /// <summary>The player's predicted route (player first, g* last) from the last plan.</summary>
        public IReadOnlyList<Vector2Int> PredictedRoute => _planner.PredictedRoute;

        /// <summary>True while committed to a cell (Intercept or Ambush); the cell is <see cref="TargetCell"/>.</summary>
        public bool HasTarget => _targetGoalId != NoGoal && (_machine.Current == _intercept || _machine.Current == _ambush);

        /// <summary>The cell the Captain is heading for or holding, when <see cref="HasTarget"/>.</summary>
        public Vector2Int TargetCell => _targetCell;

        /// <summary>The full transition table, highest priority first: for the debug overlay and the viva.</summary>
        public string DescribeTransitions()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Priority | From -> To | When");
            foreach (string label in _labels)
                sb.AppendLine(label);
            return sb.ToString();
        }

        void Rule(List<Transition<CaptainBrain>> list, IState<CaptainBrain> from, IState<CaptainBrain> to,
            int priority, string when, Func<CaptainBrain, bool> condition)
        {
            list.Add(new Transition<CaptainBrain>(from, to, condition, priority));
            _labels.Add($"{priority} | {(from == null ? "any" : from.ToString())} -> {to} | {when}");
        }

        // The context's blackboard is the live one; the constructor's is the fallback.
        WorldBlackboard World => _ctx.World ?? _blackboard;
        PlayerSnapshot Player => World.Player;

        bool PlayerAvailable()
        {
            PlayerSnapshot player = Player;
            return player.IsKnown && player.IsAlive;
        }

        bool LostContactFor(float seconds) => !_inContact && Now - _lastContactTime >= seconds;

        bool EngagedFor(float seconds) => Now - _engagedAt >= seconds;

        // ---- Perception ----------------------------------------------------------------

        void Perceive()
        {
            _seesPlayer = CanSeePlayer();
            _inContact = _seesPlayer || InContact();
            if (!_inContact)
                return;
            PlayerSnapshot player = Player;
            _lastContactTime = Now;
            _lastContactPosition = player.Position;
            Vector3 heading = player.Velocity.sqrMagnitude > 0.01f ? player.Velocity : player.Forward;
            heading.y = 0f;
            _lastContactHeading = heading.sqrMagnitude > 1e-4f ? heading.normalized : Vector3.zero;
        }

        // Keeping track of a player already being fought: line of sight within 14 m, in any
        // direction (the Captain has turned to them), unlike the 10 m view cone that starts a fight.
        bool InContact()
        {
            if (!PlayerAvailable())
                return false;
            Vector3 eye = _ctx.Position;
            Vector3 target = Player.Position;
            return FlatDistance(eye, target) <= KeepContactRange && GridSight(eye, target);
        }

        bool CanSeePlayer()
        {
            if (!PlayerAvailable())
                return false;
            Vector3 eye = _ctx.Position;
            Vector3 target = Player.Position;
            if (FlatDistance(eye, target) > EngageRange || !GridSight(eye, target))
                return false;
            if (FlatDistance(eye, target) <= ProximityRange)
                return true;
            Vector3 forward = _ctx.Forward;
            forward.y = 0f;
            return VisionQuery.CanSee(eye, forward, new Vector3(target.x, eye.y, target.z),
                EngageRange, VisionHalfAngle, lineOfSightClear: true);
        }

        // Line of sight on the grid, stopping short of the target so a player standing in a
        // wall's clearance band is not hidden by it (see TrackerBrain.GridSight).
        bool GridSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= SightEndInset)
                return true;
            Vector3 end = from + delta * ((distance - SightEndInset) / distance);
            return GridLineCheck.IsWalkable(_grid, from, end);
        }

        // ---- Prediction and intercept (2 Hz) -------------------------------------------

        /// <summary>
        /// Records the player, re-runs goal inference and, if confident, plans the intercept.
        /// Called at 2 Hz, straight away after a grid change, and on entering Reassess.
        /// </summary>
        void Decide()
        {
            _decidedThisTick = true;
            _decideNow = false;
            _nextDecisionTime = Now + DecisionInterval;

            if (!PlayerAvailable())
            {
                ClearPrediction();
                return;
            }

            PlayerSnapshot player = Player;
            _track.Record(Now, player.Cell);
            CollectGoals(player);
            if (_goals.Count == 0 || !_track.TryGetPast(Now, out Vector2Int past) ||
                !_inference.Update(_goals, past, player.Cell, World.IsFinalChapter, player.AmmoFraction))
            {
                ClearPrediction();
                return;
            }

            int best = _inference.MostLikelyIndex;
            CandidateGoal goal = _goals[best];
            _prediction = new PredictedGoal(goal.Id, goal.Cell, _inference.Confidence, Now);

            // The player is at g*: it is not where they are going any more. Leave it out until
            // they walk away (or its task completes), and re-predict on the next tick.
            if (FlatDistance(player.Position, _grid.CellToWorld(goal.Cell)) <= GoalReachedRadius)
            {
                _ignoredGoalId = goal.Id;
                _plan = InterceptPlan.None;
                _planInvalid = true;
                _decideNow = true;
                return;
            }

            // Hysteresis: while committed to a goal, keep planning for it unless another goal
            // clearly leads, and keep the plan down to the lower confidence. The prediction
            // above stays the true most likely goal, for the Saboteurs.
            int planIndex = best;
            float threshold = ConfidenceThreshold;
            int committed = IsCommitted ? IndexOfGoal(_targetGoalId) : -1;
            if (committed >= 0)
            {
                threshold = KeepConfidence;
                if (_inference.Posterior(committed) >= _inference.Confidence - GoalSwitchMargin)
                    planIndex = committed;
            }
            CandidateGoal planGoal = _goals[planIndex];
            float planConfidence = _inference.Posterior(planIndex);

            if (planConfidence < threshold ||
                !_inference.TryGetGoalField(planGoal.Id, out DijkstraField field))
            {
                _plan = InterceptPlan.None;
                return;
            }

            float playerSpeed = player.SprintSpeed > 0f ? player.SprintSpeed : DefaultPlayerSprint;
            int second = SecondMostLikelyIndex(planIndex);
            if (second >= 0 && planConfidence - _inference.Posterior(second) <= SharedGoalGap &&
                _inference.TryGetGoalField(_goals[second].Id, out DijkstraField otherField))
                _plan = _planner.PlanShared(field, otherField, player.Cell, _ctx.Cell, playerSpeed, InterceptSpeed, _isAvoided);
            else
                _plan = _planner.Plan(field, player.Cell, _ctx.Cell, playerSpeed, InterceptSpeed, _isAvoided);

            // Shut out by a closed door: the planner only walks open cells, but the Captain can
            // open doors, so defend g* by way of the door instead of standing and watching.
            if (!_plan.HasPlan)
                _plan = PlanThroughDoors(field, playerSpeed);

            _planGoalId = planGoal.Id;
            if (_plan.HasPlan)
            {
                // Face the cell the player will come from.
                IReadOnlyList<Vector2Int> route = _planner.PredictedRoute;
                int from = Mathf.Clamp(_plan.RouteIndex - 1, 0, route.Count - 1);
                _approachCell = route.Count > 0 ? route[from] : player.Cell;
            }
        }

        void ClearPrediction()
        {
            _prediction = default;
            _plan = InterceptPlan.None;
        }

        // The current objectives from the blackboard, minus a goal the player is standing at.
        void CollectGoals(in PlayerSnapshot player)
        {
            _goals.Clear();
            IReadOnlyList<ObjectiveTarget> targets = World.ObjectiveTargets;
            bool ignoredStillActive = false;
            for (int i = 0; i < targets.Count; i++)
            {
                ObjectiveTarget target = targets[i];
                if (target.Id == _ignoredGoalId)
                {
                    ignoredStillActive = true;
                    if (FlatDistance(player.Position, _grid.CellToWorld(target.Cell)) < GoalReleaseDistance)
                        continue;
                    _ignoredGoalId = NoGoal;   // the player walked away: it is a candidate again
                }
                _goals.Add(new CandidateGoal(target.Id, target.Cell, ToCategory(target.Kind)));
            }
            if (!ignoredStillActive)
                _ignoredGoalId = NoGoal;
        }

        static GoalCategory ToCategory(ObjectiveTargetKind kind)
        {
            switch (kind)
            {
                case ObjectiveTargetKind.Switch: return GoalCategory.Switch;
                case ObjectiveTargetKind.Console: return GoalCategory.Console;
                case ObjectiveTargetKind.Battery: return GoalCategory.Battery;
                default: return GoalCategory.Task;
            }
        }

        // Defend g* through a closed door, only when a door is what stands in the way: the route
        // to g* must cross one. Any other reason for no plan (two goals with no shared
        // chokepoint, a goal behind a wall) keeps the Captain watching.
        InterceptPlan PlanThroughDoors(DijkstraField goalField, float playerSpeed)
        {
            IReadOnlyList<Vector2Int> route = _planner.PredictedRoute;
            if (route.Count == 0 || !TryWalkable(_ctx.Cell, out Vector2Int start))
                return InterceptPlan.None;
            Vector2Int goalCell = route[route.Count - 1];
            if (IsAvoided(goalCell))
                return InterceptPlan.None;

            List<Vector2Int> path = _doors.FindPath(start, goalCell, _isDoorBlocked);
            if (path == null || !CrossesClosedDoor(path))
                return InterceptPlan.None;

            float playerArrival = goalField.Cost(route[0]) * GridGraph.CellSize / playerSpeed;
            float captainArrival = _doors.LastCost * GridGraph.CellSize / InterceptSpeed;
            return new InterceptPlan(InterceptKind.DefendGoal, goalCell, route.Count - 1, playerArrival, captainArrival);
        }

        static float RouteCost(List<Vector2Int> cells)
        {
            float cost = 0f;
            for (int i = 1; i < cells.Count; i++)
                cost += BaseCostModel.Instance.StepCost(cells[i - 1], cells[i]);
            return cost;
        }

        bool CrossesClosedDoor(List<Vector2Int> path)
        {
            for (int i = 0; i < path.Count; i++)
                if (_grid.GetNode(path[i]).IsDoorClosed)
                    return true;
            return false;
        }

        // ---- Doors -------------------------------------------------------------------

        /// <summary>
        /// Every tick: if a closed door is just ahead on the route, stop at it and ask the body
        /// to open it (the route is kept, and resumes when the door's cells open in the grid);
        /// give up on it after <see cref="DoorWaitTimeout"/>. Once through a door it opened,
        /// shut it behind if it is on the player's predicted route.
        /// </summary>
        void HandleDoors()
        {
            IState<CaptainBrain> state = _machine.Current;
            bool moving = state == _intercept || state == _pursue || state == _converge || state == _observe;
            if (!moving || _routeCells == null)
            {
                _waitDoorId = NoDoor;
                return;
            }

            int here = NearestRouteIndex();
            if (_waitDoorId != NoDoor)
            {
                if (!_grid.GetNode(_waitDoorCell).IsDoorClosed)
                {
                    // It opened: the grid change replans the route through it. Remember the door
                    // so it can be shut behind.
                    _openedDoorId = _waitDoorId;
                    _openedDoorCell = _waitDoorCell;
                    _closeRequested = false;
                    _waitDoorId = NoDoor;
                }
                else if (Now - _waitDoorSince > DoorWaitTimeout)
                {
                    BlockDoor(_waitDoorId);
                    _waitDoorId = NoDoor;
                    StopMoving();   // the stuck check and the next decision take it from here
                    return;
                }
                else
                {
                    HoldAtDoor();
                    return;
                }
            }

            // A closed door just ahead (the next few cells) within reach.
            for (int i = here; i < _routeCells.Count && i <= here + 6; i++)
            {
                Vector2Int cell = _routeCells[i];
                GridNode node = _grid.GetNode(cell);
                if (!node.IsDoorClosed || !node.DoorId.HasValue)
                    continue;
                if (FlatDistance(_ctx.Position, _grid.CellToWorld(cell)) > DoorReach)
                    break;
                _waitDoorId = node.DoorId.Value;
                _waitDoorCell = cell;
                _waitDoorSince = Now;
                HoldAtDoor();
                return;
            }

            CloseBehind(here);
        }

        // Stand at the door, facing it, asking the body to open it. The stuck check is paused:
        // waiting for a door to swing open is not being stuck.
        void HoldAtDoor()
        {
            if (_outPath == null || _outPath.Count > 0)
                _outPath = new List<Vector3>();   // stop, but keep _routeCells to carry on
            _outSpeed = 0f;
            _outLook = _grid.CellToWorld(_waitDoorCell);
            _outAction = AgentAction.OpenDoor;
            _outTargetId = _waitDoorId;
            ResetProgress();
        }

        // Shuts the door it came through when that door is on the player's predicted route: the
        // Captain gets through, and the player is slowed, as a Saboteur would want. Only a door
        // it opened itself (_openedDoorId is set only after waiting at a closed door): a door
        // that was already open is left as it was.
        void CloseBehind(int here)
        {
            if (_openedDoorId == NoDoor || _closeRequested || _outAction != AgentAction.None)
                return;
            Vector3 door = _grid.CellToWorld(_openedDoorCell);
            float distance = FlatDistance(_ctx.Position, door);
            if (distance > CloseBehindMax + 0.5f || _grid.GetNode(_openedDoorCell).IsDoorClosed)
            {
                _openedDoorId = NoDoor;   // walked on, or someone shut it already
                return;
            }
            if (distance < CloseBehindMin || distance > CloseBehindMax || IsAheadOnRoute(_openedDoorCell, here))
                return;
            if (!DoorOnPlayerRoute(_openedDoorId) ||
                (PlayerAvailable() && FlatDistance(Player.Position, door) < CloseBehindPlayerClearance))
                return;

            _outAction = AgentAction.CloseDoor;
            _outTargetId = _openedDoorId;
            _closeRequested = true;
        }

        bool IsAheadOnRoute(Vector2Int cell, int here)
        {
            for (int i = here; i < _routeCells.Count; i++)
                if (_routeCells[i] == cell)
                    return true;
            return false;
        }

        bool DoorOnPlayerRoute(int doorId)
        {
            if (!_prediction.IsKnown)
                return false;
            IReadOnlyList<Vector2Int> route = _planner.PredictedRoute;
            for (int i = 0; i < route.Count; i++)
            {
                GridNode node = _grid.GetNode(route[i]);
                if (node.DoorId.HasValue && node.DoorId.Value == doorId)
                    return true;
            }
            return false;
        }

        // The route cell nearest the Captain: where it is along the route.
        int NearestRouteIndex()
        {
            int best = 0;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _routeCells.Count; i++)
            {
                Vector3 world = _grid.CellToWorld(_routeCells[i]);
                float dx = world.x - _ctx.Position.x, dz = world.z - _ctx.Position.z;
                float sqr = dx * dx + dz * dz;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }
            return best;
        }

        void BlockDoor(int doorId) => _blockedDoorsUntil[doorId] = Now + AvoidSeconds;

        bool IsDoorBlocked(int doorId) => _blockedDoorsUntil.TryGetValue(doorId, out float until) && until > Now;

        /// <summary>True while the Captain is stopped at a closed door, asking for it to open.</summary>
        public bool IsWaitingAtDoor => _waitDoorId != NoDoor;

        /// <summary>True while the Captain treats <paramref name="doorId"/> as a wall (it would not open).</summary>
        public bool IsDoorGivenUp(int doorId) => IsDoorBlocked(doorId);

        /// <summary>
        /// How the body carried out a door request: a door that would not open (out of reach,
        /// not registered) is given up for a while; a shut-behind is done either way.
        /// </summary>
        public void OnActionResolved(AgentAction action, int targetId, bool success)
        {
            if (action == AgentAction.OpenDoor && !success)
            {
                BlockDoor(targetId);
                if (_waitDoorId == targetId)
                    _waitDoorId = NoDoor;
            }
            else if (action == AgentAction.CloseDoor && targetId == _openedDoorId)
            {
                _openedDoorId = NoDoor;
            }
        }

        // Following a plan to a goal: in Intercept or Ambush with a committed goal.
        bool IsCommitted =>
            _targetGoalId != NoGoal && (_machine.Current == _intercept || _machine.Current == _ambush);

        int IndexOfGoal(int id)
        {
            for (int i = 0; i < _goals.Count && i < _inference.GoalCount; i++)
                if (_goals[i].Id == id)
                    return i;
            return -1;
        }

        /// <summary>
        /// True while the player stands at a goal they reached (doing a task, holding the
        /// console) and the way to them is not known to be blocked: the Captain closes in.
        /// </summary>
        bool PlayerBusyAtGoal => _ignoredGoalId != NoGoal && PlayerAvailable() && Now >= _convergeBlockedUntil;

        // ---- Progress and avoided cells -------------------------------------------------

        /// <summary>
        /// True once the Captain, heading for <paramref name="target"/>, has not moved
        /// <see cref="ProgressStep"/> for <see cref="StuckTime"/>: pinned on a prop, in a jam,
        /// or with no route. Movement, not distance to the target, is what counts, because a
        /// route round a shelf row can lead away from the target for a while. A new target
        /// starts the clock again.
        /// </summary>
        bool NoProgressTowards(Vector3 target)
        {
            if (!_progressTracking || FlatDistance(_progressTarget, target) > 0.3f ||
                FlatDistance(_ctx.Position, _progressPoint) >= ProgressStep)
            {
                _progressTracking = true;
                _progressTarget = target;
                _progressPoint = _ctx.Position;
                _progressAt = Now;
                return false;
            }
            return Now - _progressAt >= StuckTime;
        }

        void ResetProgress() => _progressTracking = false;

        // The cell and its eight neighbours: a prop in the way usually covers more than one cell.
        void Avoid(Vector2Int cell)
        {
            if (_avoidUntil.Count > 64)
                _avoidUntil.Clear();   // all long expired in practice; keeps the table small
            float until = Now + AvoidSeconds;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    _avoidUntil[new Vector2Int(cell.x + dx, cell.y + dy)] = until;
        }

        bool IsAvoided(Vector2Int cell) => _avoidUntil.TryGetValue(cell, out float until) && until > Now;

        /// <summary>True if the planner will not choose <paramref name="cell"/> right now (the Captain got stuck going there).</summary>
        public bool IsCellAvoided(Vector2Int cell) => IsAvoided(cell);

        int SecondMostLikelyIndex(int best)
        {
            int second = -1;
            float secondP = 0f;
            for (int i = 0; i < _inference.GoalCount; i++)
            {
                if (i == best)
                    continue;
                float p = _inference.Posterior(i);
                if (p > secondP)
                {
                    secondP = p;
                    second = i;
                }
            }
            return second;
        }

        /// <summary>True if <paramref name="cell"/> is still on the player's predicted route, ahead of them.</summary>
        bool IsAheadOfPlayer(Vector2Int cell)
        {
            IReadOnlyList<Vector2Int> route = _planner.PredictedRoute;
            for (int i = 1; i < route.Count; i++)
                if (route[i] == cell)
                    return true;
            return false;
        }

        // ---- Movement ------------------------------------------------------------------

        /// <summary>Plans an A* route to <paramref name="goal"/> (or the nearest walkable cell) and outputs it.</summary>
        bool MoveTo(Vector2Int goal)
        {
            _routeGoal = goal;
            if (!TryWalkable(_ctx.Cell, out Vector2Int start) || !TryWalkable(goal, out Vector2Int end))
            {
                StopMoving();
                return false;
            }
            _routeGoal = end;   // the walkable cell actually routed to

            PathResult result = _pathfinder.FindPath(start, end, BaseCostModel.Instance);
            List<Vector2Int> cells = result.Found ? result.Cells : null;
            // The Captain can open doors: go through a closed one when that is the only way, or
            // cheaper than walking round even after the time it takes to open it.
            List<Vector2Int> viaDoor = _doors.FindPath(start, end, _isDoorBlocked);
            if (viaDoor != null && CrossesClosedDoor(viaDoor) &&
                (cells == null || _doors.LastCost + 0.01f < RouteCost(cells)))
                cells = viaDoor;
            if (cells == null)
            {
                StopMoving();
                return false;
            }

            _routeCells = cells;
            var world = new List<Vector3>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
                world.Add(_grid.CellToWorld(cells[i]));
            _outPath = world;
            return true;
        }

        void StopMoving()
        {
            _routeCells = null;
            _outPath = new List<Vector3>();   // empty = stop where you are
        }

        bool TryWalkable(Vector2Int cell, out Vector2Int walkable) =>
            _grid.TryFindNearestTraversable(cell, NearestCellRadius, out walkable);

        /// <summary>The cell under a world point, clamped onto the grid (a retreat may point off its edge).</summary>
        Vector2Int ClampedCell(Vector3 world)
        {
            Vector2Int cell = _grid.WorldToCell(world);
            return new Vector2Int(Mathf.Clamp(cell.x, 0, _grid.Width - 1), Mathf.Clamp(cell.y, 0, _grid.Height - 1));
        }

        /// <summary>Arrival by distance, never by cell: the body stops short of the last waypoint.</summary>
        bool ArrivedAt(Vector2Int cell) =>
            FlatDistance(_ctx.Position, _grid.CellToWorld(cell)) <= ArrivalRadius;

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
