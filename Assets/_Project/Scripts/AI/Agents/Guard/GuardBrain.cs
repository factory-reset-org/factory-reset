using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.FSM;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Guard
{
    /// <summary>
    /// The Guard Bot's brain: a defensive shooter that picks cover with
    /// <see cref="CoverEvaluator"/>, walks to it with tactical A* (exposure-weighted),
    /// peeks out to shoot and relocates when its cover stops protecting it. Pure C#: the
    /// only view of the physics world is <see cref="ICoverVisibility"/>.
    /// </summary>
    /// <remarks>
    /// Each tick runs in three steps. Perceive: is the player in range, which battery tier
    /// applies, is the cover still valid. Select: cover is re-evaluated at 1 Hz, or at once
    /// when the tier changes, the player moves 2 m, the cover is exposed or the grid
    /// changes. Act: the state machine picks a state from the transition table, and the
    /// state writes this tick's route, look target and action.
    /// </remarks>
    public sealed partial class GuardBrain : IAgentBrain
    {
        // Speeds, m/s.
        public const float WalkSpeed = 3.5f;
        public const float AdvanceSpeed = 4.5f;
        public const float PatrolSpeed = 2f;

        // Engagement: the Guard is alerted the moment the player steps into its room. A player
        // just outside it (within HomeMargin, such as in a doorway) is fought only if they are
        // within AlertRange and the Guard sees them or hears them fire. Once alerted it stays so
        // until the player leaves the room (plus the margin) or gets farther than DisengageRange.
        public const float AlertRange = 20f;
        public const float DisengageRange = 30f;

        // How far outside its room a player can stand and still be fought: a few metres, so a
        // player in a doorway is a target but one on the other side of a wall is not.
        public const float HomeMargin = 3f;

        // Cover is only taken inside the room, with a metre of slack for cells in the doorway.
        public const float CoverMargin = 1f;

        // A shot the Guard heard counts only if it is loud enough here and recent. The player's
        // own sounds carry this source id (see NoiseEvent).
        public const int PlayerNoiseSourceId = -1;
        public const float ShotAlertLevel = 40f;
        public const float HearingWindow = 1f;
        public const float ShootRange = 15f;
        public const float FireInterval = 1.2f;

        // Cover selection.
        public const float CoverInterval = 1f;           // 1 Hz
        public const float ReevaluateDistance = 2f;      // or when the player moves this far
        public const int TopCandidates = 3;              // candidates that get a real A*
        public const float MaxPathCost = 60f;            // grid units, about 30 m of hidden walking
        public const int CostSearchCells = 400;          // effort allowed to cost one cover; past it the cover counts as far
        public const float CoverHysteresis = 0.1f;       // a new cover must beat the current one by this
        public const float Lambda = 3f;                  // an exposed step costs 4x a hidden one

        // Battery tiers: ideal engagement distance in metres.
        public const float OverchargeRange = 12f;
        public const float HighBatteryRange = 10f;
        public const float MidBatteryRange = 7f;
        public const float LowBatteryRange = 4f;
        public const float HighBatteryThreshold = 0.5f;
        public const float LowBatteryThreshold = 0.25f;

        // Peek rhythm, seconds.
        public const float HoldTime = 1.3f;
        public const float AggressiveHoldTime = 0.7f;
        public const float PeekDuration = 2f;
        public const float QuietTimeBeforePeek = 1.5f;   // high battery: peek only after the player stops firing

        // Distances.
        public const float ArrivalRadius = 0.5f;
        public const int NearestCellRadius = 6;
        public const int RetreatSearchRadius = 16;       // cells
        public const int RetreatSearchStep = 2;
        public const float ChaseRepathInterval = 0.5f;
        public const float PatrolRetryInterval = 1f;

        enum BatteryTier { High, Mid, Low, Overcharge }

        readonly struct RankedCandidate
        {
            public readonly CoverCandidate Candidate;
            public readonly float Score;

            public RankedCandidate(CoverCandidate candidate, float score)
            {
                Candidate = candidate;
                Score = score;
            }
        }

        static readonly Comparison<RankedCandidate> ByScoreDescending = (a, b) => b.Score.CompareTo(a.Score);
        static readonly Vector2Int[] NoRoute = new Vector2Int[0];

        // Where a tick's time goes, for the profiler and the performance log.
        static readonly ProfilerMarker PerceiveMarker = new ProfilerMarker("AI.Guard.Perceive");
        static readonly ProfilerMarker EvaluateMarker = new ProfilerMarker("AI.Guard.EvaluateCover");
        static readonly ProfilerMarker FindBestMarker = new ProfilerMarker("AI.Guard.FindBest");
        static readonly ProfilerMarker PathCostMarker = new ProfilerMarker("AI.Guard.PathCost");
        static readonly ProfilerMarker StatesMarker = new ProfilerMarker("AI.Guard.States");
        static readonly ProfilerMarker MoveToMarker = new ProfilerMarker("AI.Guard.MoveTo");
        static readonly ProfilerMarker RetreatMarker = new ProfilerMarker("AI.Guard.Retreat");

        // Cardinals first, so the Guard peeks sideways before it peeks diagonally.
        static readonly Vector2Int[] PeekOffsets =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        readonly GridGraph _grid;
        readonly IPathfinder _pathfinder;
        readonly WorldBlackboard _blackboard;
        readonly ICoverVisibility _visibility;
        readonly CoverEvaluator _evaluator;
        readonly TacticalCostModel _tactical;
        readonly OneToOneCost _costSearch;
        readonly GridRegions _regions;
        readonly int _agentId;
        readonly IReadOnlyList<Vector3> _patrolPoints;
        readonly IGuardHome _home;

        readonly List<RankedCover> _best = new List<RankedCover>(TopCandidates);
        readonly Predicate<Vector2Int> _isAvailable;
        readonly List<RankedCandidate> _retreatOptions = new List<RankedCandidate>();

        // What the latest evaluation costed with a real path, kept for the debug overlay.
        readonly List<ScoredCover> _scored = new List<ScoredCover>(TopCandidates + 1);
        // Line of sight remembered per cell while the player stays in one cell and the grid
        // does not change. The cover search, the peek checks and the tactical A* all read it.
        readonly CachedCoverVisibility _sight;
        Vector2Int _sightCell;
        bool _sightCellKnown;

        readonly StateMachine<GuardBrain> _machine;
        readonly IState<GuardBrain> _patrol;
        readonly IState<GuardBrain> _takeCover;
        readonly IState<GuardBrain> _inCover;
        readonly IState<GuardBrain> _peek;
        readonly IState<GuardBrain> _relocate;
        readonly IState<GuardBrain> _advance;
        readonly IState<GuardBrain> _retreat;
        readonly IState<GuardBrain> _stunned;
        readonly List<string> _labels = new List<string>();

        // This tick's input.
        AgentContext _ctx;
        float Now => _ctx.Time;

        // Perception.
        bool _engaged;
        bool _seesPlayer;

        // Tactics from the player's battery.
        BatteryTier _tier = BatteryTier.High;
        float _idealRange = HighBatteryRange;
        bool _fullCoverOnly;
        bool _aggressive;
        float _holdTime = HoldTime;

        // Cover.
        bool _hasCover;
        Vector2Int _coverCell;
        bool _coverCanPeek;
        float _nextCoverTime = float.NegativeInfinity;
        bool _evaluateNow;
        Vector3 _lastEvalPlayerPosition;

        // Flags and timers the transition table reads.
        bool _stunPending;
        bool _reconsider;
        float _holdUntil;
        float _peekUntil;
        float _lastShotAt = float.NegativeInfinity;
        float _nextChaseTime = float.NegativeInfinity;
        float _nextPatrolTime = float.NegativeInfinity;
        int _patrolIndex;

        // Current route and this tick's output.
        List<Vector2Int> _routeCells;
        Vector2Int _routeGoal;
        ICostModel _routeCost;
        bool _replanRequested;
        List<Vector3> _outPath;
        float _outSpeed;
        Vector3? _outLook;
        AgentAction _outAction;
        string _debugState = "Patrol";

        /// <param name="grid">The level grid.</param>
        /// <param name="pathfinder">The shared A* (one is created if null).</param>
        /// <param name="blackboard">The shared blackboard: the player and the cover reservations.</param>
        /// <param name="visibility">Line of sight from the player's eye, implemented by Runtime.</param>
        /// <param name="agentId">This agent's unique id, the owner of its cover reservation.</param>
        /// <param name="patrolPoints">Points walked while no player is in range; may be null or empty.</param>
        /// <param name="home">The room this Guard holds. Null: it has no room and fights wherever the player is in range.</param>
        public GuardBrain(GridGraph grid, IPathfinder pathfinder, WorldBlackboard blackboard,
            ICoverVisibility visibility, int agentId, IReadOnlyList<Vector3> patrolPoints = null,
            IGuardHome home = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
            _pathfinder = pathfinder ?? new AStarSearch(grid);
            _agentId = agentId;
            _patrolPoints = patrolPoints ?? Array.Empty<Vector3>();
            _home = home;
            _sight = new CachedCoverVisibility(visibility, grid);
            _evaluator = new CoverEvaluator(grid, _sight, GridGraph.CellSize);
            _isAvailable = IsAvailable;
            _tactical = new TacticalCostModel(IsExposed, Lambda);
            _costSearch = new OneToOneCost(grid);
            _regions = GridRegions.For(grid);

            _patrol = new PatrolState();
            _takeCover = new TakeCoverState();
            _inCover = new InCoverState();
            _peek = new PeekAndShootState();
            _relocate = new RelocateState();
            _advance = new AdvanceState();
            _retreat = new RetreatState();
            _stunned = new StunnedState();

            var rules = new List<Transition<GuardBrain>>();
            Rule(rules, null, _stunned, 100, "stunned (first tick after the reboot)",
                b => b._stunPending && b._machine.Current != b._stunned);
            Rule(rules, null, _patrol, 95, "no player, player dead, or out of range",
                b => !b._engaged && b._machine.Current != b._patrol);
            Rule(rules, _stunned, _relocate, 90, "reboot: plan a fresh route", b => !b._stunPending);
            Rule(rules, _patrol, _relocate, 85, "a living player within alert range", b => b._engaged);
            Rule(rules, _takeCover, _inCover, 80, "reached the reserved cover cell", b => b.AtCover());
            Rule(rules, _advance, _inCover, 80, "reached the reserved cover cell", b => b.AtCover());
            Rule(rules, _inCover, _peek, 70, "hold timer elapsed and peeking is allowed",
                b => !b._reconsider && b.Now >= b._holdUntil && b.PeekAllowed());
            Rule(rules, _peek, _inCover, 65, "overcharge active: no peeking until it ends",
                b => b._tier == BatteryTier.Overcharge);
            Rule(rules, _takeCover, _relocate, 60, "cover changed, lost or exposed", b => b._reconsider);
            Rule(rules, _inCover, _relocate, 60, "cover changed, lost or exposed", b => b._reconsider);
            Rule(rules, _peek, _relocate, 60, "cover changed, lost or exposed", b => b._reconsider);
            Rule(rules, _advance, _relocate, 60, "cover changed, lost or found", b => b._reconsider);
            Rule(rules, _retreat, _relocate, 60, "cover found, or the battery tier changed", b => b._reconsider);
            Rule(rules, _peek, _inCover, 50, "peek timer elapsed", b => b.Now >= b._peekUntil);
            Rule(rules, _relocate, _takeCover, 40, "a cover cell is reserved", b => b._hasCover && !b._aggressive);
            Rule(rules, _relocate, _advance, 30, "player battery below 25% or reloading", b => b._aggressive);
            Rule(rules, _relocate, _retreat, 20, "no valid cover", b => true);

            _machine = new StateMachine<GuardBrain>(_patrol, rules);
        }

        // ---- IAgentBrain ---------------------------------------------------------------

        public AgentIntent Tick(in AgentContext ctx)
        {
            _ctx = ctx;
            _outPath = null;
            _outLook = null;
            _outAction = AgentAction.None;

            using (PerceiveMarker.Auto())
                Perceive();

            using (StatesMarker.Auto())
            {
                _machine.Tick(this);
                // Stunned and Relocate are pass-through states: entered and left on the same
                // tick, so the Guard acts on its fresh cover choice straight away.
                for (int i = 0; i < 3 && (_machine.Current == _stunned || _machine.Current == _relocate); i++)
                    _machine.Tick(this);
            }

            if (_replanRequested && _outPath == null && _routeCells != null)
                MoveTo(_routeGoal, _routeCost);
            _replanRequested = false;

            return new AgentIntent
            {
                Path = _outPath,
                DesiredSpeed = _outSpeed,
                LookTarget = _outLook,
                Action = _outAction,
                ActionTargetId = 0,
                DebugState = _debugState
            };
        }

        /// <summary>
        /// A door or box changed the grid. The route is replanned only if it crosses a
        /// changed cell. Cover is re-evaluated on the next tick: a settled box is new cover,
        /// and a blocked cover cell stops being a candidate.
        /// </summary>
        public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells)
        {
            if (changedCells == null)
                return;
            _evaluateNow = true;
            _sight.Invalidate();
            if (_routeCells == null)
                return;
            for (int i = 0; i < changedCells.Count; i++)
            {
                if (_routeCells.Contains(changedCells[i]))
                {
                    _replanRequested = true;
                    return;
                }
            }
        }

        /// <summary>
        /// The controller owns the stun. Give up the cover so another agent can use it and
        /// drop the route; the first tick after the reboot picks cover and plans afresh.
        /// </summary>
        public void OnStunned(float duration)
        {
            ReleaseCover();
            _routeCells = null;
            _stunPending = true;
            _evaluateNow = true;
            _sight.Invalidate();
            _debugState = "Stunned";
        }

        /// <summary>Leaves the game for good: a dead Guard must never hold a cover cell.</summary>
        public void OnDestroyed()
        {
            ReleaseCover();
            _routeCells = null;
        }

        // ---- Read-only state for the debug overlay and tests ---------------------------

        /// <summary>Current state name, e.g. "InCover"; also the intent's DebugState.</summary>
        public string StateName => _debugState;

        /// <summary>True while a cover cell is chosen and reserved.</summary>
        public bool HasCover => _hasCover;

        /// <summary>The reserved cover cell; only meaningful while <see cref="HasCover"/>.</summary>
        public Vector2Int CoverCell => _coverCell;

        /// <summary>The ideal distance to the player for the current battery tier, in metres.</summary>
        public float IdealRange => _idealRange;

        /// <summary>The player's battery tier the tactics follow: High, Mid, Low or Overcharge.</summary>
        public string BatteryTierName => _tier.ToString();

        /// <summary>True if the Guard can step out of its cover to shoot; only meaningful while <see cref="HasCover"/>.</summary>
        public bool CoverCanPeek => _coverCanPeek;

        /// <summary>True while a living player is in range and the Guard is fighting them.</summary>
        public bool IsEngaged => _engaged;

        /// <summary>Where the Guard believes the player is; only meaningful while <see cref="IsEngaged"/>.</summary>
        public Vector3 PlayerPosition => Player.Position;

        /// <summary>Line-of-sight checks passed on so far (in the game, physics raycasts). For the performance log.</summary>
        public int SightChecksMade => _sight.InnerQueries;

        /// <summary>Cells the latest cover search had to sight-test. For the performance log.</summary>
        public int CoverCellsTested => _evaluator.CellsTested;

        /// <summary>The cells of the route being walked, start to goal. Empty when there is none.</summary>
        public IReadOnlyList<Vector2Int> RouteCells => _routeCells != null ? _routeCells : (IReadOnlyList<Vector2Int>)NoRoute;

        /// <summary>
        /// Appends the cover cells the latest evaluation costed with a real path (the top
        /// candidates and the cover it already held), with their final scores. Empty while not engaged.
        /// </summary>
        public void GetScoredCover(List<ScoredCover> into)
        {
            for (int i = 0; i < _scored.Count; i++)
                into.Add(_scored[i]);
        }

        /// <summary>The full transition table, highest priority first: for the debug overlay and the viva.</summary>
        public string DescribeTransitions()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Priority | From -> To | When");
            foreach (string label in _labels)
                sb.AppendLine(label);
            return sb.ToString();
        }

        void Rule(List<Transition<GuardBrain>> list, IState<GuardBrain> from, IState<GuardBrain> to,
            int priority, string when, Func<GuardBrain, bool> condition)
        {
            list.Add(new Transition<GuardBrain>(from, to, condition, priority));
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

        // ---- Perception ----------------------------------------------------------------

        void Perceive()
        {
            _engaged = ComputeEngaged();
            if (!_engaged)
            {
                // Nobody to fight: give the cover back and start from scratch next time.
                _seesPlayer = false;
                if (_hasCover)
                    ReleaseCover();
                _scored.Clear();
                _sightCellKnown = false;
                _reconsider = false;
                _evaluateNow = true;
                return;
            }

            PlayerSnapshot player = Player;
            if (!_sightCellKnown || player.Cell != _sightCell)
            {
                _sight.Invalidate();
                _sightCell = player.Cell;
                _sightCellKnown = true;
            }

            bool wasAggressive = _aggressive;
            bool tierChanged = UpdateTactic(player);
            // Asked afresh every tick, not from memory: it is what notices that the player
            // has flanked the cover without leaving their cell. If so, nothing remembered holds.
            bool exposed = _hasCover && !_visibility.IsBlocked(_coverCell, CoverEvaluator.LowCoverHeight);
            if (exposed)
                _sight.Invalidate();
            bool playerMoved = FlatDistance(player.Position, _lastEvalPlayerPosition) > ReevaluateDistance;

            if (_evaluateNow || tierChanged || exposed || playerMoved || Now >= _nextCoverTime)
                EvaluateCover(player);

            // With no cover the choice is Advance or Retreat, which depends on the tier.
            if (wasAggressive != _aggressive && !_hasCover)
                _reconsider = true;

            _seesPlayer = FlatDistance(_ctx.Position, player.Position) <= ShootRange
                && !_visibility.IsBlocked(_ctx.Cell, CoverEvaluator.ChestHeight);
        }

        // Distance alone is not a reason to fight: a player on the far side of a wall is not a
        // threat. Stepping into the Guard's room is (it is the Guard's territory), and so is
        // being seen or heard near it.
        bool ComputeEngaged()
        {
            if (!PlayerAvailable())
                return false;

            // The leash holds an alerted Guard to its room too: it does not follow the player out.
            if (_home != null && !_home.Contains(Player.Position, HomeMargin))
                return false;

            float distance = FlatDistance(_ctx.Position, Player.Position);
            if (_engaged)
                return distance <= DisengageRange;

            // In the room: detected, whether or not the Guard has a line to them.
            if (_home != null && _home.Contains(Player.Position, 0f))
                return true;

            if (distance > AlertRange)
                return false;
            return CanSeePlayer() || HeardPlayerShoot();
        }

        // The player's eye has a clear line to the Guard's chest.
        bool CanSeePlayer() => !_visibility.IsBlocked(_ctx.Cell, CoverEvaluator.ChestHeight);

        // A loud noise the player made, in the last second.
        bool HeardPlayerShoot()
        {
            SensorSnapshot senses = _ctx.Senses;
            return senses.HasNoise
                && senses.NoiseSourceId == PlayerNoiseSourceId
                && senses.NoiseLevel >= ShotAlertLevel
                && Now - senses.NoiseTime <= HearingWindow;
        }

        /// <summary>Reads the battery tier off the player. Returns true if the tier changed.</summary>
        bool UpdateTactic(in PlayerSnapshot player)
        {
            BatteryTier tier;
            if (player.OverchargeTimeLeft > 0f)
                tier = BatteryTier.Overcharge;
            else if (player.AmmoFraction < LowBatteryThreshold || player.IsReloading)
                tier = BatteryTier.Low;
            else if (player.AmmoFraction <= HighBatteryThreshold)
                tier = BatteryTier.Mid;
            else
                tier = BatteryTier.High;

            bool changed = tier != _tier;
            _tier = tier;
            _fullCoverOnly = tier == BatteryTier.Overcharge;
            _aggressive = tier == BatteryTier.Low;
            _holdTime = _aggressive ? AggressiveHoldTime : HoldTime;
            switch (tier)
            {
                case BatteryTier.Overcharge: _idealRange = OverchargeRange; break;
                case BatteryTier.Low: _idealRange = LowBatteryRange; break;
                case BatteryTier.Mid: _idealRange = MidBatteryRange; break;
                default: _idealRange = HighBatteryRange; break;
            }
            return changed;
        }

        bool PeekAllowed()
        {
            if (!_hasCover || !_coverCanPeek || _tier == BatteryTier.Overcharge)
                return false;
            if (_tier != BatteryTier.High)
                return true;
            float lastShot = Player.LastShotTime;
            return lastShot < 0f || Now - lastShot >= QuietTimeBeforePeek;
        }

        /// <summary>True if the player can see <paramref name="cell"/>. Cached until the player changes cell.</summary>
        bool IsExposed(Vector2Int cell) => !_sight.IsBlocked(cell, CoverEvaluator.ChestHeight);

        // A cover cell another agent has reserved is not on offer, nor is one outside the room.
        bool IsAvailable(Vector2Int cell)
        {
            int? holder = World.Reservations.ReservedBy(cell);
            if (holder.HasValue && holder.Value != _agentId)
                return false;
            return _home == null || _home.Contains(_grid.CellToWorld(cell), CoverMargin);
        }

        // ---- Cover selection -----------------------------------------------------------

        /// <summary>
        /// Takes the three best candidates by a cheap path cost (octile distance), runs tactical
        /// A* on them, rescores those with the true cost and reserves the best. The
        /// current cover is kept unless it stopped being valid or a new one beats it by
        /// <see cref="CoverHysteresis"/>.
        /// </summary>
        void EvaluateCover(in PlayerSnapshot player)
        {
            using ProfilerMarker.AutoScope timed = EvaluateMarker.Auto();
            _evaluateNow = false;
            _nextCoverTime = Now + CoverInterval;
            _lastEvalPlayerPosition = player.Position;

            Vector2Int playerCell = player.Cell;
            _scored.Clear();
            CellReservations reservations = World.Reservations;
            using (FindBestMarker.Auto())
                _evaluator.FindBest(playerCell, _ctx.Cell, _idealRange, MaxPathCost, _fullCoverOnly,
                    _isAvailable, TopCandidates, _best);

            // The cover already held is judged on its own: it need not be among the best.
            CoverCandidate current = default;
            bool currentValid = _hasCover && IsAvailable(_coverCell)
                && _evaluator.TryEvaluate(_coverCell, playerCell, out current)
                && (!_fullCoverOnly || current.Protection >= 1f);

            bool found = false;
            CoverCandidate best = default;
            float bestScore = 0f;
            for (int i = 0; i < _best.Count; i++)
            {
                CoverCandidate candidate = _best[i].Candidate;
                if (!TryPathCost(candidate.Cell, out float cost))
                    continue;
                float score = _evaluator.Score(candidate, playerCell, _idealRange, cost, MaxPathCost);
                _scored.Add(new ScoredCover(candidate, cost, score));
                if (!found || score > bestScore)
                {
                    found = true;
                    best = candidate;
                    bestScore = score;
                }
            }

            if (currentValid)
            {
                // Already costed if it was one of the best; otherwise cost it now.
                bool costed = TryGetScored(current.Cell, out float currentScore);
                if (!costed && TryPathCost(current.Cell, out float currentCost))
                {
                    currentScore = _evaluator.Score(current, playerCell, _idealRange, currentCost, MaxPathCost);
                    _scored.Add(new ScoredCover(current, currentCost, currentScore));
                    costed = true;
                }
                if (costed && (!found || best.Cell == current.Cell || bestScore <= currentScore + CoverHysteresis))
                {
                    _coverCanPeek = current.CanPeek;
                    return;
                }
            }

            if (found && reservations.TryReserve(best.Cell, _agentId))
            {
                if (!_hasCover || best.Cell != _coverCell)
                    _reconsider = true;
                _hasCover = true;
                _coverCell = best.Cell;
                _coverCanPeek = best.CanPeek;
                return;
            }

            if (_hasCover)
                _reconsider = true;
            ReleaseCover();
        }

        bool TryGetScored(Vector2Int cell, out float score)
        {
            for (int i = 0; i < _scored.Count; i++)
            {
                if (_scored[i].Cell == cell)
                {
                    score = _scored[i].Score;
                    return true;
                }
            }
            score = 0f;
            return false;
        }

        /// <summary>
        /// The tactical cost of walking from here to <paramref name="goal"/>, capped at
        /// <see cref="MaxPathCost"/>, or false if it cannot be reached.
        /// </summary>
        /// <remarks>
        /// Only the cost is needed here, not the route, and only up to MaxPathCost: beyond it
        /// the travel part of the cover score is already zero. So the search is cost-only, gives
        /// up on anything dearer and is allowed <see cref="CostSearchCells"/> cells of effort.
        /// Out of the player's sight every step is cheap, so without the effort limit a search
        /// for a cover in another room spreads over most of the level before the cost limit
        /// stops it. A cover that cannot be costed within the limit counts as far, and whether
        /// it can be reached at all is read from the grid's connected areas.
        /// </remarks>
        bool TryPathCost(Vector2Int goal, out float cost)
        {
            using ProfilerMarker.AutoScope timed = PathCostMarker.Auto();
            cost = 0f;
            if (!TryWalkable(_ctx.Cell, out Vector2Int start))
                return false;

            cost = _costSearch.Compute(start, goal, _tactical, MaxPathCost, CostSearchCells);
            if (!float.IsPositiveInfinity(cost))
                return true;

            cost = MaxPathCost;
            return _regions.Connected(start, goal);
        }

        void ReleaseCover()
        {
            World.Reservations.Release(_agentId);
            _hasCover = false;
        }

        bool AtCover() => _hasCover && !_reconsider && ArrivedAt(_coverCell);

        /// <summary>A cell next to the cover from which the player's chest is visible.</summary>
        bool TryFindPeekCell(out Vector2Int peekCell)
        {
            for (int i = 0; i < PeekOffsets.Length; i++)
            {
                Vector2Int cell = _coverCell + PeekOffsets[i];
                if (_grid.IsTraversable(cell) && !_visibility.IsBlocked(cell, CoverEvaluator.ChestHeight))
                {
                    peekCell = cell;
                    return true;
                }
            }
            peekCell = _coverCell;
            return false;
        }

        /// <summary>
        /// The reachable hidden cell farthest from the player, for when no cover exists.
        /// False if every nearby cell is exposed: the Guard then fights from where it stands.
        /// </summary>
        bool TryFindRetreatCell(out Vector2Int retreatCell)
        {
            using ProfilerMarker.AutoScope timed = RetreatMarker.Auto();
            _retreatOptions.Clear();
            Vector2Int playerCell = Player.Cell;
            for (int dy = -RetreatSearchRadius; dy <= RetreatSearchRadius; dy += RetreatSearchStep)
            {
                for (int dx = -RetreatSearchRadius; dx <= RetreatSearchRadius; dx += RetreatSearchStep)
                {
                    Vector2Int cell = new Vector2Int(_ctx.Cell.x + dx, _ctx.Cell.y + dy);
                    if (!_grid.IsTraversable(cell) || IsExposed(cell))
                        continue;
                    if (_home != null && !_home.Contains(_grid.CellToWorld(cell), CoverMargin))
                        continue;
                    _retreatOptions.Add(new RankedCandidate(new CoverCandidate(cell, 0f, false),
                        Vector2Int.Distance(cell, playerCell)));
                }
            }
            _retreatOptions.Sort(ByScoreDescending);

            for (int i = 0; i < _retreatOptions.Count && i < TopCandidates; i++)
            {
                Vector2Int cell = _retreatOptions[i].Candidate.Cell;
                if (TryPathCost(cell, out _))
                {
                    retreatCell = cell;
                    return true;
                }
            }
            retreatCell = _ctx.Cell;
            return false;
        }

        // ---- Shooting ------------------------------------------------------------------

        /// <summary>
        /// Requests a shot if the player is visible and the fire interval has passed. The
        /// controller adds the 0.3 s telegraph, the hitscan and the damage.
        /// </summary>
        void TryShoot()
        {
            if (!_seesPlayer || Now - _lastShotAt < FireInterval)
                return;
            _outLook = Player.Position;
            _outAction = AgentAction.Shoot;
            _lastShotAt = Now;
        }

        // ---- Movement ------------------------------------------------------------------

        /// <summary>Plans an A* route to <paramref name="goal"/> under <paramref name="cost"/> and outputs it.</summary>
        bool MoveTo(Vector2Int goal, ICostModel cost)
        {
            using ProfilerMarker.AutoScope timed = MoveToMarker.Auto();
            _routeGoal = goal;
            _routeCost = cost;
            if (!TryWalkable(_ctx.Cell, out Vector2Int start) || !TryWalkable(goal, out Vector2Int end))
            {
                StopMoving();
                return false;
            }
            _routeGoal = end;

            PathResult result = _pathfinder.FindPath(start, end, cost);
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
