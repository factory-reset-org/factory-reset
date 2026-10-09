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
    public sealed partial class CaptainBrain : IAgentBrain, IGoalPredictor
    {
        // Speeds in m/s. 4.6 is the prototype's Captain speed, used by the intercept timing.
        public const float InterceptSpeed = 4.6f;
        public const float ObserveSpeed = 2.5f;

        // Decisions.
        public const float DecisionInterval = 0.5f;      // 2 Hz
        public const float ConfidenceThreshold = 0.5f;   // commit to an intercept at P(g*) >= 0.5
        public const float SharedGoalGap = 0.1f;         // top two goals this close: plan for both
        public const float DefaultPlayerSprint = 7f;     // if the player reports no sprint speed

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
        readonly List<string> _labels = new List<string>();

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

            var rules = new List<Transition<CaptainBrain>>();
            Rule(rules, _dormant, _observe, 110, "wake signal (or Chapter 3 started, or spawned awake)", b => b.IsAwake);
            Rule(rules, null, _stunned, 100, "stunned (first tick after the reboot)",
                b => b._stunPending && b._machine.Current != b._stunned && b._machine.Current != b._dormant);
            Rule(rules, _stunned, _reassess, 90, "stun over: the old prediction is stale", b => !b._stunPending);
            Rule(rules, _observe, _engage, 80, "player visible within 10 m", b => b._seesPlayer);
            Rule(rules, _intercept, _engage, 80, "player visible within 10 m", b => b._seesPlayer);
            Rule(rules, _ambush, _engage, 80, "player visible within 10 m", b => b._seesPlayer);
            Rule(rules, _pursue, _engage, 80, "contact again (line of sight within 14 m)", b => b._inContact);
            Rule(rules, _engage, _reassess, 75, "the player is gone (dead or missing)", b => !b.PlayerAvailable());
            Rule(rules, _engage, _pursue, 70, "no contact for 0.7 s, after at least 2 s engaged",
                b => b.EngagedFor(MinEngageTime) && b.LostContactFor(LoseSightDelay));
            Rule(rules, _pursue, _reassess, 65, "searched the last-seen spot, 5 s passed, or the player is gone",
                b => b._pursueOver || !b.PlayerAvailable());
            Rule(rules, _intercept, _reassess, 60, "plan invalid: g* changed, confidence < 0.5, player at g*, cell blocked",
                b => b._planInvalid);
            Rule(rules, _ambush, _reassess, 60, "plan invalid: g* changed, confidence < 0.5, player at g* or past the cell",
                b => b._planInvalid);
            Rule(rules, _intercept, _ambush, 50, "reached the intercept cell", b => b.ArrivedAt(b._targetCell));
            Rule(rules, _reassess, _intercept, 40, "a plan exists (confidence >= 0.5)", b => b._plan.HasPlan);
            Rule(rules, _reassess, _observe, 30, "no plan (confidence < 0.5, or no shared chokepoint)", b => !b._plan.HasPlan);
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

            return new AgentIntent
            {
                Path = _outPath,
                DesiredSpeed = _outSpeed,
                LookTarget = _outLook,
                Action = _outAction,
                ActionTargetId = 0,
                DebugState = _debugState,
                Alert = CurrentAlert()
            };
        }

        // "!" once it has committed to the player (cutting them off, waiting, fighting);
        // "?" while it watches and re-predicts; nothing while asleep, down or with no player.
        AlertLevel CurrentAlert()
        {
            IState<CaptainBrain> state = _machine.Current;
            if (state == _engage || state == _pursue || state == _intercept || state == _ambush)
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

            if (_inference.Confidence < ConfidenceThreshold ||
                !_inference.TryGetGoalField(goal.Id, out DijkstraField field))
            {
                _plan = InterceptPlan.None;
                return;
            }

            float playerSpeed = player.SprintSpeed > 0f ? player.SprintSpeed : DefaultPlayerSprint;
            int second = SecondMostLikelyIndex(best);
            if (second >= 0 && _inference.Confidence - _inference.Posterior(second) <= SharedGoalGap &&
                _inference.TryGetGoalField(_goals[second].Id, out DijkstraField otherField))
                _plan = _planner.PlanShared(field, otherField, player.Cell, _ctx.Cell, playerSpeed, InterceptSpeed);
            else
                _plan = _planner.Plan(field, player.Cell, _ctx.Cell, playerSpeed, InterceptSpeed);

            _planGoalId = goal.Id;
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
            if (!result.Found)
            {
                StopMoving();
                return false;
            }

            _routeCells = result.Cells;
            var world = new List<Vector3>(result.Cells.Count);
            for (int i = 0; i < result.Cells.Count; i++)
                world.Add(_grid.CellToWorld(result.Cells[i]));
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
