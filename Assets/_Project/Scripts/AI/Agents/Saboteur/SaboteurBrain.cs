using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// One Saboteur instance's utility brain. It runs selection at 4 Hz through
    /// <see cref="ActionSelector"/> with Idle/Patrol and, when a candidate source is given,
    /// AttackPlayer and Flee, and handles stun, graph changes and permanent destruction. The other
    /// sabotage and combat actions are added on top of
    /// the same selection loop as their world facts become available: an
    /// <see cref="ICandidateSource"/> supplies them, and the brain then applies the squad layer
    /// (the "not claimed" veto and attack saturation), claims its target on commit and staggers its
    /// decisions by letter, all through the <see cref="SquadCoordinator"/> shared by every brain
    /// built over the same <see cref="TargetClaims"/>. Saboteur A reports the chapter keycard
    /// through <see cref="IDropsItems"/> once it has been destroyed.
    /// </summary>
    /// <remarks>
    /// <para>Every dependency is passed in at construction, so the brain never looks anything up
    /// and can be tested on a plain <see cref="GridGraph"/>. Movement routes come from the
    /// shared <see cref="IPathfinder"/> (S2's A*); the brain never searches by itself.</para>
    /// <para><b>Awareness.</b> The brain keeps one <see cref="SaboteurAwareness"/> (vision cone, short
    /// all-round sense, line of sight, memory of the last sighting, the last player noise) and binds it to
    /// its action sources, so attacking, fleeing, stealing, trapping and door closing all react only to a
    /// player this Saboteur could have seen or heard. The line-of-sight query is the one its attack or flee
    /// source was built with; a brain with neither has no sight, and its sources keep their own rules.</para>
    /// <para><b>Search.</b> A player seen out of attack range is approached; one lost from sight is searched
    /// for where they were last seen (a little ahead, the way they were moving), and a player noise is
    /// investigated. The Saboteur walks there, looks round, checks one spot further on and goes back to its
    /// patrol. A sighting or noise that has been searched is not searched again.</para>
    /// <para><b>Patrol.</b> With <see cref="SaboteurOptions.RoomPatrol"/> the given patrol points are the
    /// seed of a loop that covers the Saboteur's room (<see cref="RoomPatrolPlanner"/>), shared out between
    /// squad mates who start in the same room.</para>
    /// </remarks>
    public sealed class SaboteurBrain : IAgentBrain, IDropsItems, IActionFeedback, IHealthAware, ICrowdAware
    {
        /// <summary>Ground distance, in metres, from a door within which the Saboteur can close it.</summary>
        public const float DoorReachMetres = 1.5f;

        /// <summary>Seconds the Saboteur keeps asking to close a door that stays open before it gives up on it.</summary>
        public const float CloseDoorTimeoutSeconds = 3f;

        /// <summary>
        /// Ground distance, in metres, from the (walkable) cell of a trap or battery within which the
        /// Saboteur can arm or take it. The controller then acts within its own, larger reach of the object itself.
        /// </summary>
        public const float FactReachMetres = 1.5f;

        /// <summary>Seconds the Saboteur keeps asking to arm a trap or take a battery before it gives up on it.</summary>
        public const float FactTimeoutSeconds = 3f;

        /// <summary>
        /// Game seconds a Saboteur walking to a door, trap or battery may go without getting
        /// <see cref="TravelProgressMetres"/> closer before its route is sent again; stuck again for as long, and
        /// it gives the target up. Shoved aside, carried off, teleported or wedged against something the grid
        /// does not know about, it would otherwise hold its claim and keep "walking" for ever.
        /// </summary>
        public const float TravelStuckSeconds = 4f;

        /// <summary>Metres closer to the target that count as progress for <see cref="TravelStuckSeconds"/>.</summary>
        public const float TravelProgressMetres = 0.5f;

        /// <summary>
        /// Seconds between two claw swipes while attacking: the swipe interval on the Saboteur
        /// prefab (<c>AgentClawSwipe</c>, 1.2 s start to start), so a request never lands on a swipe
        /// that is still playing.
        /// </summary>
        public const float AttackIntervalSeconds = 1.2f;

        /// <summary>
        /// Ground distance, in metres, at which the Saboteur's body holds in front of the player
        /// (<c>PlayerStandOff</c> on the Saboteur prefab). The claw swipe is measured from here.
        /// </summary>
        public const float MeleeStandOffMetres = 1.6f;

        /// <summary>
        /// Ground distance, in metres, within which the Saboteur asks for a swipe. The swipe lands
        /// up to the stand-off plus its 0.6 m lunge (2.2 m); asking from 2.0 m leaves slack for the
        /// body's travel between two ticks. Farther away the Saboteur walks at the player instead.
        /// </summary>
        public const float MeleeReachMetres = 2f;

        /// <summary>
        /// Seconds between two route searches toward a moving player while closing in. A search per
        /// tick would cost four A* runs per frame for the squad; the player moves slowly compared with it.
        /// </summary>
        public const float ChaseReplanSeconds = 0.5f;

        /// <summary>Seconds between two choices of where to run while fleeing.</summary>
        public const float FleeReplanSeconds = 1f;

        /// <summary>Seconds a cornered Saboteur (no retreat found) fights back before it looks for a way out again.</summary>
        public const float FleeRetryDelaySeconds = 1f;

        /// <summary>Starting move speed in metres per second (plan Appendix A).</summary>
        public const float MoveSpeed = 4.3f;

        /// <summary>Seconds between selection passes (4 Hz).</summary>
        public const float DecisionInterval = 0.25f;

        /// <summary>Constant utility of Idle/Patrol, the fallback every other action must beat.</summary>
        public const float IdleScore = 0.1f;

        /// <summary>Search score for a fresh sighting; it falls to <see cref="SearchSightingMinScore"/> as the memory ages.</summary>
        public const float SearchSightingScore = 0.5f;

        /// <summary>Search score for a sighting about to be forgotten.</summary>
        public const float SearchSightingMinScore = 0.15f;

        /// <summary>Search score for the loudest fresh player noise; quieter or older noises score less.</summary>
        public const float SearchNoiseScore = 0.3f;

        /// <summary>Search score for any player noise heard, before loudness and age.</summary>
        public const float SearchNoiseMinScore = 0.12f;

        /// <summary>Score for closing in on a player seen beyond attack range: just above Idle, below any attack worth making.</summary>
        public const float ApproachScore = 0.12f;

        /// <summary>Seconds ahead, along the player's last velocity, at which a lost player is looked for.</summary>
        public const float SearchLeadSeconds = 1f;

        /// <summary>The furthest, in metres, that lead may move the search point.</summary>
        public const float SearchLeadMaxMetres = 4f;

        /// <summary>Metres from the search point within which the Saboteur counts as there.</summary>
        public const float SearchArrivalMetres = 1f;

        /// <summary>Seconds the Saboteur turns and looks round at a search point.</summary>
        public const float LookAroundSeconds = 2.4f;

        /// <summary>Metres beyond the search point, the way the player went, of the one further spot checked.</summary>
        public const float ProbeMetres = 3f;

        /// <summary>Seconds after which a search is given up wherever it has got to.</summary>
        public const float SearchTimeoutSeconds = 14f;

        /// <summary>Others already searching at which a Search score is halved, so the squad does not all converge.</summary>
        public const int SearchSaturation = 2;

        /// <summary>Seconds after a flight during which an attack scores less, so a Saboteur that ran does not turn straight back.</summary>
        public const float FleeRecoverSeconds = 2f;

        /// <summary>The attack multiplier during <see cref="FleeRecoverSeconds"/>.</summary>
        public const float FleeRecoverAttackFactor = 0.5f;

        /// <summary>
        /// Lowest score of a door that shuts the player in or holds them up. Sabotage is scored in bands above
        /// what a held-back attack can reach (doors, then traps, then batteries), and the top of the door band
        /// plus the selector's momentum stays under its emergency threshold, so a badly hurt Saboteur can
        /// still break off to flee.
        /// </summary>
        public const float DoorPriorityFloor = 0.66f;

        /// <summary>Lowest score of a trap worth arming; see <see cref="DoorPriorityFloor"/>.</summary>
        public const float TrapPriorityFloor = 0.58f;

        /// <summary>Lowest score of a battery worth stealing; see <see cref="DoorPriorityFloor"/>.</summary>
        public const float BatteryPriorityFloor = 0.5f;

        /// <summary>
        /// Lowest source score at which a sabotage counts as worth doing: it is then scored in its band and holds the
        /// attack back. Below it (a door that barely lengthens the route, a battery at the edge of reach) it keeps
        /// its own score and competes as before.
        /// </summary>
        public const float SabotageMinScore = 0.15f;

        /// <summary>Width of each sabotage band: the source's score, 0 to 1, is spread over it.</summary>
        public const float PriorityBandWidth = 0.08f;

        /// <summary>
        /// The attack multiplier while this Saboteur has sabotage on offer: the best attack (0.88 at melee range)
        /// with momentum stays under the battery band, so it attacks only when no sabotage is worth doing.
        /// </summary>
        public const float AttackDeferFactor = 0.25f;

        /// <summary>Cells (3 m) of a route ahead of the body watched for a standing body in the way.</summary>
        public const int BodyLookAheadCells = 6;

        /// <summary>The most often (s) a standing body on the route ahead makes the Saboteur route again.</summary>
        public const float BodyReplanSeconds = 1f;

        /// <summary>Within this (m) of the end of a leg round a body, the next leg is sent.</summary>
        public const float LegArrivalMetres = 0.5f;

        /// <summary>Cells (1.5 m) of the route ahead priced as blocked when a walk gets nowhere.</summary>
        public const int BlockAheadCells = 3;

        /// <summary>Seconds a door, trap or battery that one Saboteur failed at is left alone by the whole squad.</summary>
        public const float SquadFailureSeconds = 10f;

        /// <summary>How far, in cells, a blocked patrol point may be snapped to a free cell.</summary>
        public const int PatrolSnapRadius = 2;

        /// <summary>
        /// How far, in cells, the Saboteur's own cell may be snapped to a free one when it plans a route. The
        /// controller passes the cell under the body, which can be a blocked cell: against a wall, a box or a
        /// corner it was wedged into. Without this it could plan nothing and would hold there for ever.
        /// </summary>
        public const int StartSnapRadius = 4;

        /// <summary>
        /// Ground-plane distance, in metres, at which the last waypoint counts as reached.
        /// The body stops within 0.3 m of it, so a slightly larger radius never misses it.
        /// </summary>
        public const float ArrivalRadius = 0.5f;

        /// <summary>
        /// How far, in cells (4 m), the keycard may be moved from where Saboteur A fell to land
        /// on a traversable cell. If nothing walkable is that close, the search widens to the
        /// whole grid, so the keycard is never dropped on a blocked cell or lost.
        /// </summary>
        public const int DropSearchRadius = 8;

        static readonly ActionKey IdleKey = new ActionKey(SaboteurActionKind.Idle);
        static readonly ActionKey SearchKey = new ActionKey(SaboteurActionKind.Search);

        enum SearchPhase { Walk, Look }

        enum TravelCheck { Fine, SendAgain, GiveUp }

        // Watches a walk to a target for progress, in game time, so a pause never counts as being stuck. The
        // distance it is given is what is left of the route (SaboteurPaths.RemainingMetres), not a straight
        // line, so a route that first bends away from the target still counts as progress.
        struct TravelWatch
        {
            bool _started;
            bool _sentAgain;
            float _best;
            float _improvedAt;

            public TravelCheck Check(float distance, float now)
            {
                if (!_started || distance <= _best - TravelProgressMetres + 0.001f)
                {
                    _started = true;
                    _best = distance;
                    _improvedAt = now;
                    return TravelCheck.Fine;
                }

                if (now - _improvedAt < TravelStuckSeconds)
                    return TravelCheck.Fine;
                if (_sentAgain)
                    return TravelCheck.GiveUp;

                _sentAgain = true;
                _best = distance;
                _improvedAt = now;
                return TravelCheck.SendAgain;
            }
        }

        readonly SaboteurIdentity _identity;
        readonly GridGraph _grid;
        readonly IPathfinder _pathfinder;
        readonly SquadCoordinator _squad;
        readonly ICandidateSource _source;
        readonly DetourCache _detours;
        readonly ActionSelector _selector;
        readonly SaboteurOptions _options;
        readonly SaboteurAwareness _awareness;

        // Every route is priced round bodies in the way and spots where a walk got nowhere (see SaboteurRouteCost).
        readonly SaboteurRouteCost _routeCost;
        float _roundedAt = float.NegativeInfinity;

        // Set by TryRoute when the only route went through a body; the patrol then waits and looks again
        // (bodies are not grid changes, so the usual wait for one would never end).
        bool _routeBlockedByBody;
        float _bodyRetryAt = float.NegativeInfinity;

        // Near a standing body a route is sent a leg at a time (see Steer): where the leg sent last ends, if one was.
        bool _hasLeg;
        Vector3 _legEnd;
        Vector3 _position;
        Vector2Int[] _patrolCells;
        bool _patrolPlanned;

        // Shut out of its own room (a door it or a squad mate closed, which agents cannot open): it patrols the
        // room it is in, and goes back to its own loop at the next change to the grid, such as the door opening.
        bool _stranded;
        Vector2Int[] _homeCells;
        TravelWatch _patrolTravel;
        int _patrolRouteIndex;
        bool _patrolResending;
        readonly List<ActionCandidate> _candidates = new List<ActionCandidate>(8);
        readonly int _keycardItemId;
        readonly UtilityDecisionTrace _lastDecision = new UtilityDecisionTrace();

        // Decisions run on a fixed phase of the 4 Hz grid, offset by this instance's letter, so
        // the four instances never decide in the same frame and never drift back together.
        float _anchorTime;
        float _nextDecisionTime;
        bool _scheduleStarted;

        int _patrolIndex;
        List<Vector2Int> _routeCells;
        bool _needsRoute = true;
        bool _holdSent;

        // Set after no patrol point could be reached, so A* is not rerun every tick while
        // holding; cleared by the next selection pass or any grid change.
        bool _routeRetryWaiting;

        // CloseDoor: the plan for the selected door. Travel sends one route, then the Saboteur stops
        // within reach of the door and asks for the close until the door shuts or the time is up.
        bool _doorActive;
        bool _doorResolved;
        ActionKey _doorKey;
        bool _doorRouteSent;
        bool _doorEmitted;
        bool _doorInReach;
        float _doorReachedAt;
        List<Vector2Int> _doorRoute;
        TravelWatch _doorTravel;
        int _doorRouteIndex;

        // ArmTrap and StealBattery: the plan for the selected trap or battery, one at a time like a door.
        // Travel sends one route, then the Saboteur stops within reach and asks until the runtime answers.
        bool _factActive;
        bool _factResolved;
        bool _factRouteSent;
        bool _factInReach;
        float _factReachedAt;
        ActionKey _factKey;
        List<Vector2Int> _factRoute;
        TravelWatch _factTravel;
        int _factRouteIndex;

        // Search: where to go (chosen at the last decision), the route there, and what happens on arrival.
        bool _searchActive;
        bool _searchApproach;
        Vector3 _searchGoal;
        Vector3 _searchOrigin;
        Vector3 _searchDirection;
        bool _searchHasCandidate;
        bool _searchCandidateApproach;
        Vector3 _searchCandidateGoal;
        Vector3 _searchCandidateDirection;
        SearchPhase _searchPhase;
        float _searchStartedAt;
        float _lookStartedAt;
        bool _probed;
        bool _searchRouteSent;
        Vector3 _searchRouteGoal;
        float _searchReplanAt;
        List<Vector2Int> _searchRoute;
        TravelWatch _searchTravel;
        int _searchRouteIndex;

        // AttackPlayer: whether an attack is under way (so the patrol is routed again afterwards),
        // whether the body has been stopped inside melee reach, the chase route toward the player
        // (its target cell and when it may be searched again), and when a swipe may next be asked for.
        bool _attacking;
        bool _attackStopped;
        bool _chasing;
        Vector2Int _chaseCell;
        float _nextChaseTime = float.NegativeInfinity;
        float _nextShotTime;

        // Flee: the route being run, when to choose again, and the pause after finding no way out. A
        // cornered Saboteur fights (Attack) until the pause is over.
        bool _fleeing;
        bool _cornered;
        List<Vector2Int> _fleeRoute;
        float _fleeReplanAt;
        float _fleeBlockedUntil;
        float _lastFleeingAt = float.NegativeInfinity;

        // The game time of the latest tick, for answers that arrive between ticks, and the Saboteur's
        // own health as the controller last reported it.
        float _lastTime;
        float _healthFraction = 1f;

        bool _destroyed;

        // The cell of the latest tick, the starting point for placing the keycard on destruction.
        Vector2Int _lastCell;
        bool _hasLastCell;
        ItemDrop _keycardDrop;

        /// <summary>Creates a brain for one Saboteur instance.</summary>
        /// <param name="identity">Claim owner id and letter for this instance.</param>
        /// <param name="grid">The shared navigation grid.</param>
        /// <param name="pathfinder">The shared search over <paramref name="grid"/>.</param>
        /// <param name="claims">
        /// The squad's shared target claims. Brains built over the same instance form one squad
        /// and must have different agent ids.
        /// </param>
        /// <param name="patrolPoints">World positions to patrol in a loop; may be empty (the Saboteur holds).</param>
        /// <param name="selectorSettings">Stability tuning; the design defaults when null.</param>
        /// <param name="keycardItemId">
        /// The <see cref="ItemDrop.ItemId"/> of the keycard Saboteur A drops. Provisional until
        /// the controller and pickup owners agree what the id means.
        /// </param>
        /// <param name="candidateSource">
        /// Supplies the sabotage and combat candidates; null until those actions exist, in which
        /// case Idle/Patrol is the only candidate.
        /// </param>
        /// <param name="detourCache">
        /// The squad's shared detour cache. Needed to carry out CloseDoor (it knows the door's cells);
        /// without it a selected CloseDoor is ignored and the Saboteur keeps patrolling.
        /// </param>
        /// <param name="options">How it patrols; <see cref="SaboteurOptions.Default"/> when null.</param>
        public SaboteurBrain(SaboteurIdentity identity, GridGraph grid, IPathfinder pathfinder,
            TargetClaims claims, IReadOnlyList<Vector3> patrolPoints, SelectorSettings selectorSettings = null,
            int keycardItemId = 0, ICandidateSource candidateSource = null, DetourCache detourCache = null,
            SaboteurOptions options = null)
        {
            // default(SaboteurIdentity) would otherwise pass as "Saboteur A, the keycard carrier".
            if (!identity.IsAssigned)
                throw new ArgumentException("The Saboteur needs an identity from the spawner.", nameof(identity));

            _identity = identity;
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _routeCost = new SaboteurRouteCost(grid);
            if (claims == null)
                throw new ArgumentNullException(nameof(claims));

            _selector = new ActionSelector(selectorSettings);
            _options = options ?? SaboteurOptions.Default;
            _keycardItemId = keycardItemId;
            _source = candidateSource;
            _detours = detourCache;

            // One awareness for the brain and every source, built on the sight the attack or flee source has.
            IPlayerSight sight = FindSight(candidateSource);
            _awareness = new SaboteurAwareness(sight);
            if (sight != null && candidateSource is ISaboteurAwarenessUser user)
                user.Bind(_awareness, _pathfinder);

            var cells = new List<Vector2Int>();
            if (patrolPoints != null)
            {
                for (int i = 0; i < patrolPoints.Count; i++)
                {
                    // Points outside the grid cannot be walked to, so they are left out.
                    if (_grid.TryWorldToCell(patrolPoints[i], out Vector2Int cell))
                        cells.Add(cell);
                }
            }
            _patrolCells = cells.ToArray();

            // Last, so a brain that fails validation never joins the squad.
            _squad = SquadCoordinator.For(claims);
            _squad.Register(identity);
            if (_patrolCells.Length > 0)
                _squad.SetHome(identity.AgentId, _patrolCells[0]);
        }

        static IPlayerSight FindSight(ICandidateSource source)
        {
            switch (source)
            {
                case CompositeCandidateSource composite:
                    return composite.FindSight();
                case AttackPlayerSource attack:
                    return attack.Sight;
                case FleeSource flee:
                    return flee.Sight;
                default:
                    return null;
            }
        }

        /// <summary>This instance's identity.</summary>
        public SaboteurIdentity Identity => _identity;

        /// <summary>The action-target pair currently selected, for the debug panel.</summary>
        public ActionKey CurrentAction => _selector.Current;

        /// <summary>
        /// The scores and outcome of the latest selection pass, for the debug panel. It is one
        /// instance overwritten at 4 Hz: read it, do not keep it.
        /// </summary>
        public UtilityDecisionTrace LastDecision => _lastDecision;

        /// <summary>True once <see cref="OnDestroyed"/> has run; the Saboteur never comes back.</summary>
        public bool IsDestroyed => _destroyed;

        /// <summary>What this Saboteur knows about the player, for the debug panel and tests.</summary>
        public SaboteurAwareness Awareness => _awareness;

        /// <summary>
        /// The patrol points in use: the planned room loop once it has been planned, else the given points. While the
        /// Saboteur is shut out of its own room (<see cref="IsStranded"/>), a loop over the room it stands in.
        /// </summary>
        public IReadOnlyList<Vector2Int> PatrolCells => _patrolCells;

        /// <summary>How this Saboteur prices its routes: bodies in the way and spots where it got nowhere.</summary>
        public SaboteurRouteCost RouteCost => _routeCost;

        /// <inheritdoc />
        public void OnCrowd(IReadOnlyList<CrowdMember> others) => _routeCost.SetCrowd(others);

        /// <summary>True while no point of its own patrol loop can be reached and it patrols the room it stands in.</summary>
        public bool IsStranded => _stranded;

        /// <summary>Where the current search is headed, for the debug panel and tests; meaningful while searching.</summary>
        public Vector3 SearchGoal => _searchGoal;

        /// <inheritdoc />
        public AgentIntent Tick(in AgentContext ctx)
        {
            if (_destroyed)
                return Stop("Destroyed");

            _lastCell = ctx.Cell;
            _hasLastCell = true;
            _lastTime = ctx.Time;
            _position = ctx.Position;
            _awareness.Update(ctx);

            if (!_scheduleStarted)
            {
                _scheduleStarted = true;
                _anchorTime = ctx.Time + SquadCoordinator.DecisionOffset(_identity.Letter);
                _nextDecisionTime = _anchorTime;
            }

            // Another instance may have taken the target this one committed to.
            if (_squad.CheckOutscored(_identity.AgentId))
                _selector.CancelCurrent();

            if (ctx.Time >= _nextDecisionTime)
            {
                Decide(ctx);
                _nextDecisionTime = NextSlotAfter(ctx.Time);
            }

            if (_detours != null && _selector.HasCurrent && _selector.Current.Kind == SaboteurActionKind.CloseDoor
                && TryCloseDoor(ctx, out AgentIntent closeDoor))
                return OnAnErrand(closeDoor);

            if (ctx.World != null && _selector.HasCurrent
                && (_selector.Current.Kind == SaboteurActionKind.ArmTrap || _selector.Current.Kind == SaboteurActionKind.StealBattery)
                && TryFactAction(ctx, out AgentIntent factAction))
                return OnAnErrand(factAction);

            if (ctx.World != null && _selector.HasCurrent && _selector.Current.Kind == SaboteurActionKind.Flee
                && TryFlee(ctx, out AgentIntent flee))
                return flee;

            // A Saboteur that wants to flee but has nowhere to run fights back instead. An attack under way
            // carries on out to the keep range, so a player at the edge of 8 m does not make it stop and start.
            bool attackSelected = _selector.HasCurrent && (_selector.Current.Kind == SaboteurActionKind.AttackPlayer
                || (_selector.Current.Kind == SaboteurActionKind.Flee && _cornered));
            if (ctx.World != null && attackSelected && AttackPlayerSource.InKeepRange(ctx.World.Player, ctx.Position, out float playerDistance))
                return Attack(ctx, playerDistance);

            if (_selector.HasCurrent && _selector.Current.Kind == SaboteurActionKind.Search && TrySearch(ctx, out AgentIntent search))
                return search;

            // Back to patrolling after an attack, a flight, a search or a door: the body was stopped or sent elsewhere, so route again.
            if (_attacking || _doorActive || _fleeing || _factActive || _searchActive)
            {
                // The selection can end before TryCloseDoor sees the door shut, because a closed door
                // stops being a candidate. A close this Saboteur had asked for still earns the cooldown.
                if (_doorActive && _doorEmitted && _detours.TryGetCells(_doorKey.TargetId, out IReadOnlyList<Vector2Int> doorCells)
                    && doorCells.Count > 0 && _grid.GetNode(doorCells[0]).IsDoorClosed)
                    _selector.NotifySuccess(_doorKey, ctx.Time);

                ResetAttackPlan();
                ResetFleePlan();
                ResetDoorPlan();
                ResetFactPlan();
                ResetSearchPlan();
                _needsRoute = true;
                _holdSent = false;
            }

            // The other actions branch here on CurrentAction as they are added.
            return Patrol(ctx.Cell, ctx.Position);
        }

        /// <inheritdoc />
        public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells)
        {
            if (changedCells == null || changedCells.Count == 0)
                return;

            // A door opening or closing may have cut the route to the player; search again (still rationed).
            _chasing = false;

            // Stranded outside its own room: any change may have opened the way back, so try its own loop again.
            if (_stranded)
            {
                _stranded = false;
                _patrolCells = _homeCells;
                _patrolIndex = 0;
                _routeCells = null;
                _needsRoute = true;
                _routeRetryWaiting = false;
            }

            if (_doorRoute != null)
            {
                for (int i = 0; i < changedCells.Count; i++)
                {
                    if (_doorRoute.Contains(changedCells[i]))
                    {
                        _doorRouteSent = false;
                        break;
                    }
                }
            }

            if (_factRoute != null)
            {
                for (int i = 0; i < changedCells.Count; i++)
                {
                    if (_factRoute.Contains(changedCells[i]))
                    {
                        _factRouteSent = false;
                        break;
                    }
                }
            }

            if (_searchRoute != null)
            {
                for (int i = 0; i < changedCells.Count; i++)
                {
                    if (_searchRoute.Contains(changedCells[i]))
                    {
                        _searchRouteSent = false;
                        break;
                    }
                }
            }

            if (_routeCells == null)
            {
                // Holding with no reachable patrol point: any change may have opened one.
                _routeRetryWaiting = false;
                return;
            }

            for (int i = 0; i < changedCells.Count; i++)
            {
                if (_routeCells.Contains(changedCells[i]))
                {
                    _needsRoute = true;
                    return;
                }
            }
        }

        /// <inheritdoc />
        public void OnStunned(float duration)
        {
            if (_destroyed)
                return;

            // The controller has stopped the body and owns the stun timing: it does not tick
            // this brain until the reboot. So only drop the plan here; the first tick after
            // the reboot selects and routes afresh.
            _selector.CancelCurrent();
            _squad.EndPlan(_identity.AgentId);
            _needsRoute = true;
            _holdSent = false;
            ResetAttackPlan();
            ResetFleePlan();
            ResetDoorPlan();
            ResetFactPlan();
            ResetSearchPlan();
        }

        /// <inheritdoc />
        public void OnDestroyed()
        {
            if (_destroyed)
                return;

            _destroyed = true;
            _squad.OnDestroyed(_identity.AgentId);
            _selector.CancelCurrent();
            _routeCells = null;

            if (_identity.CarriesKeycard)
                _keycardDrop = new ItemDrop(ItemDropKind.Keycard, _keycardItemId, FindKeycardCell());
        }

        /// <summary>This Saboteur's own health as last reported, 0 to 1.</summary>
        public float HealthFraction => _healthFraction;

        /// <inheritdoc />
        public void OnHealthChanged(int hitPointsLeft, int maxHitPoints)
        {
            _healthFraction = maxHitPoints > 0 ? Mathf.Clamp01(hitPointsLeft / (float)maxHitPoints) : 1f;
            (_source as IHealthAware)?.OnHealthChanged(hitPointsLeft, maxHitPoints);
        }

        /// <summary>
        /// The runtime's answer to a door request, exactly once per request: success when the door was
        /// closed, failure when it could not be reached or registered, or this Saboteur was knocked out
        /// first. Either answer starts the door's cooldown, so the Saboteur neither re-closes a door it
        /// just shut nor stands at one it cannot close; the plan ends on the next tick.
        /// </summary>
        public void OnActionResolved(AgentAction action, int targetId, bool success)
        {
            if (_destroyed)
                return;

            SaboteurActionKind kind;
            switch (action)
            {
                case AgentAction.CloseDoor:
                    kind = SaboteurActionKind.CloseDoor;
                    break;
                case AgentAction.ArmTrap:
                    kind = SaboteurActionKind.ArmTrap;
                    break;
                case AgentAction.StealBattery:
                    kind = SaboteurActionKind.StealBattery;
                    break;
                default:
                    return;
            }

            var key = new ActionKey(kind, targetId);
            if (success)
                _selector.NotifySuccess(key, _lastTime);
            else
                Fail(key, _lastTime);

            if (kind == SaboteurActionKind.CloseDoor)
            {
                if (_doorActive && _doorKey.Equals(key))
                    _doorResolved = true;
            }
            else if (_factActive && _factKey.Equals(key))
            {
                _factResolved = true;
            }
        }

        /// <inheritdoc />
        public int GetDrops(List<ItemDrop> buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (!_destroyed || !_identity.CarriesKeycard)
                return 0;

            buffer.Add(_keycardDrop);
            return 1;
        }

        // The nearest traversable cell to where the Saboteur fell. The controller passes the
        // cell under the body, which can be a blocked cell or one inside a box, so it is only
        // a starting point. The keycard is the Chapter 3 task item, so it must always land
        // somewhere reachable: widen to the whole grid before giving up, and if there is no
        // traversable cell at all, report the start clamped onto the grid rather than lose it.
        Vector2Int FindKeycardCell()
        {
            Vector2Int start = _hasLastCell
                ? _lastCell
                : _patrolCells.Length > 0 ? _patrolCells[0] : new Vector2Int(_grid.Width / 2, _grid.Height / 2);

            if (_grid.TryFindNearestTraversable(start, DropSearchRadius, out Vector2Int cell))
                return cell;

            // Far enough to reach every cell from a start that may lie outside the grid.
            long reachX = Math.Max(Math.Abs((long)start.x), Math.Abs((long)start.x - (_grid.Width - 1)));
            long reachY = Math.Max(Math.Abs((long)start.y), Math.Abs((long)start.y - (_grid.Height - 1)));
            int wholeGrid = (int)Math.Min(Math.Max(reachX, reachY) * 2 + 1, int.MaxValue / 4);
            if (_grid.TryFindNearestTraversable(start, wholeGrid, out cell))
                return cell;

            return new Vector2Int(Mathf.Clamp(start.x, 0, _grid.Width - 1), Mathf.Clamp(start.y, 0, _grid.Height - 1));
        }

        // The first decision slot after t on this instance's fixed phase.
        float NextSlotAfter(float t) =>
            _anchorTime + (Mathf.Floor((t - _anchorTime) / DecisionInterval) + 1f) * DecisionInterval;

        void Decide(in AgentContext ctx)
        {
            float now = ctx.Time;
            int id = _identity.AgentId;
            _routeRetryWaiting = false;
            _candidates.Clear();
            _lastDecision.Begin(now);

            // Each action is added to the trace in the same order as the candidate list, so the
            // selector can fill in the outcome of candidate i from index i.
            _candidates.Add(new ActionCandidate(IdleKey, IdleScore));
            _lastDecision.AddConstant(IdleKey, new ActionScore(IdleScore, IdleScore));

            if (_source != null)
            {
                _source.AddCandidates(ctx, _identity, _candidates);

                // Sabotage comes before combat: while this Saboteur has a door, trap or battery it may go
                // for, the attack is held back (AttackDeferFactor) and the sabotage is scored in its band.
                bool sabotageOnOffer = false;
                for (int i = 1; i < _candidates.Count && !sabotageOnOffer; i++)
                    sabotageOnOffer = IsSabotage(_candidates[i].Key.Kind) && SquadScore(_candidates[i], now) >= SabotageMinScore;

                // The squad layer: other instances' claims and attacks change this instance's
                // scores. The trace keeps the source's score as the raw score and the adjusted
                // one as the base score, so a veto shows up as a base score of 0.
                for (int i = 1; i < _candidates.Count; i++)
                {
                    ActionCandidate candidate = _candidates[i];
                    float raw = candidate.BaseScore;
                    float adjusted = SquadScore(candidate, now);

                    if (IsSabotage(candidate.Key.Kind) && adjusted >= SabotageMinScore)
                        adjusted = PriorityScore(candidate.Key.Kind, adjusted);

                    if (candidate.Key.Kind == SaboteurActionKind.AttackPlayer)
                    {
                        adjusted *= _squad.AttackSaturation(id);
                        // Just after a flight, turning straight back to fight is less attractive.
                        if (now - _lastFleeingAt < FleeRecoverSeconds && now >= _lastFleeingAt)
                            adjusted *= FleeRecoverAttackFactor;
                        if (sabotageOnOffer)
                            adjusted *= AttackDeferFactor;
                    }

                    _candidates[i] = new ActionCandidate(candidate.Key, adjusted);
                    _lastDecision.AddConstant(candidate.Key, new ActionScore(raw, adjusted));
                }
            }

            AddSearchCandidate(ctx);

            SelectionResult result = _selector.Select(_candidates, now, _lastDecision);
            if (!result.HasSelection)
            {
                _squad.EndPlan(id);
                return;
            }

            // Commit to the pair and claim its target. The "not claimed" veto already removed
            // targets held by others, so a lost claim is rare (a same-frame race); drop the
            // plan and let the next decision see the new holder.
            if (!_squad.TryCommit(id, result.Key, result.BaseScore))
                _selector.CancelCurrent();
        }

        // The source's score after the squad's vetoes: a target another Saboteur has claimed, or one a squad
        // mate has just failed at, which everyone leaves alone for a while.
        float SquadScore(in ActionCandidate candidate, float now) =>
            _squad.IsFailing(candidate.Key, now) ? 0f : candidate.BaseScore * _squad.NotClaimedFactor(_identity.AgentId, candidate.Key);

        /// <summary>True for the sabotage actions: CloseDoor, ArmTrap and StealBattery.</summary>
        public static bool IsSabotage(SaboteurActionKind kind) =>
            kind == SaboteurActionKind.CloseDoor || kind == SaboteurActionKind.ArmTrap || kind == SaboteurActionKind.StealBattery;

        /// <summary>
        /// Maps a sabotage score in (0, 1] into its kind's priority band: doors from <see cref="DoorPriorityFloor"/>,
        /// traps from <see cref="TrapPriorityFloor"/>, batteries from <see cref="BatteryPriorityFloor"/>, each
        /// <see cref="PriorityBandWidth"/> wide, so the source's score still orders targets of one kind. Other
        /// kinds, and a score of 0 or less, are returned as they are.
        /// </summary>
        public static float PriorityScore(SaboteurActionKind kind, float score)
        {
            if (score <= 0f)
                return score;

            float floor;
            switch (kind)
            {
                case SaboteurActionKind.CloseDoor:
                    floor = DoorPriorityFloor;
                    break;
                case SaboteurActionKind.ArmTrap:
                    floor = TrapPriorityFloor;
                    break;
                case SaboteurActionKind.StealBattery:
                    floor = BatteryPriorityFloor;
                    break;
                default:
                    return score;
            }

            return floor + PriorityBandWidth * Mathf.Clamp01(score);
        }

        void ResetAttackPlan()
        {
            _attacking = false;
            _attackStopped = false;
            _chasing = false;
            _nextChaseTime = float.NegativeInfinity;
        }

        // The route toward the player while out of melee reach: a new one when there is none yet or
        // the player has moved to another cell (and the search delay is over), otherwise null so the
        // body keeps walking the route it has. With no route to the player the body is stopped once
        // and the search is tried again after the delay.
        List<Vector3> ChaseRoute(in AgentContext ctx)
        {
            Vector2Int target = ctx.World.Player.Cell;
            if (_chasing && target == _chaseCell && !LegReached(ctx.Position))
                return null;
            if (ctx.Time < _nextChaseTime)
                return null;

            _nextChaseTime = ctx.Time + ChaseReplanSeconds;
            if (!TryRoute(ctx.Cell, target, out List<Vector2Int> cells))
            {
                _chasing = false;
                if (_attackStopped)
                    return null;

                _attackStopped = true;
                return new List<Vector3>();
            }

            _chasing = true;
            _chaseCell = target;
            _attackStopped = false;
            return Steer(cells);
        }

        // The Saboteur's attack is a claw swipe that only lands within about 2.2 m, so it closes in
        // first: beyond MeleeReachMetres it walks at the player (a route to the player's cell, searched
        // again at most every ChaseReplanSeconds once the player has moved to another cell), and inside
        // it stops the body once and asks for a swipe, paced by AttackIntervalSeconds across both
        // phases so stepping in and out of reach cannot beat the swipe's recovery. The runtime's
        // stand-off holds the body in front of the player, so the route need not stop short itself.
        AgentIntent Attack(in AgentContext ctx, float playerDistance)
        {
            _attacking = true;
            List<Vector3> path = null;
            AgentAction action = AgentAction.None;

            if (playerDistance <= MeleeReachMetres)
            {
                // Stepping back out of reach searches again at once, whatever the delay was.
                _chasing = false;
                _nextChaseTime = float.NegativeInfinity;
                if (!_attackStopped)
                {
                    _attackStopped = true;
                    path = new List<Vector3>();
                }

                if (ctx.Time >= _nextShotTime)
                {
                    action = AgentAction.Shoot;
                    _nextShotTime = ctx.Time + AttackIntervalSeconds;
                }
            }
            else
            {
                path = ChaseRoute(ctx);
            }

            return new AgentIntent
            {
                Path = path,
                DesiredSpeed = MoveSpeed,
                LookTarget = ctx.World.Player.Position,
                Action = action,
                DebugState = "AttackPlayer"
            };
        }

        void ResetFleePlan()
        {
            _fleeing = false;
            _cornered = false;
            _fleeRoute = null;
        }

        // Carries out the selected Flee: run to a refuge away from the player, and choose again once a
        // second or on arrival, since the player moves. Returns false when there is nothing to run from
        // or nowhere to run; the caller then fights (cornered) or falls back to patrolling.
        bool TryFlee(in AgentContext ctx, out AgentIntent intent)
        {
            intent = default;
            PlayerSnapshot player = ctx.World.Player;
            if (!player.IsKnown || !player.IsAlive)
            {
                ResetFleePlan();
                _selector.CancelCurrent();
                _squad.EndPlan(_identity.AgentId);
                return false;
            }

            if (ctx.Time < _fleeBlockedUntil)
            {
                _cornered = true;
                return false;
            }

            _cornered = false;
            bool arrived = _fleeRoute != null && HasArrivedAt(_fleeRoute, ctx.Position);
            if (_fleeing && !arrived && ctx.Time < _fleeReplanAt)
            {
                _lastFleeingAt = ctx.Time;
                intent = Keep("Flee");
                return true;
            }

            if (!FleePlanner.TryChooseRetreat(_grid, _pathfinder, ctx.Cell, ctx.Position, player.Position, _patrolCells, out List<Vector2Int> route))
            {
                ResetFleePlan();
                _cornered = true;
                _fleeBlockedUntil = ctx.Time + FleeRetryDelaySeconds;
                return false;
            }

            _fleeing = true;
            _lastFleeingAt = ctx.Time;
            ResetAttackPlan();
            _fleeRoute = route;
            _fleeReplanAt = ctx.Time + FleeReplanSeconds;
            intent = Move(route, "Flee");
            return true;
        }

        void ResetDoorPlan()
        {
            _doorActive = false;
            _doorResolved = false;
            _doorRouteSent = false;
            _doorEmitted = false;
            _doorInReach = false;
            _doorRoute = null;
            _doorTravel = default;
            _doorRouteIndex = 0;
        }

        // Carries out the selected CloseDoor: walk to the door, stop within reach and ask the runtime
        // to close it. Returns false when the plan has ended (the door shut, the Saboteur could not
        // get there, or the door stayed open), so the caller falls back to patrolling.
        bool TryCloseDoor(in AgentContext ctx, out AgentIntent intent)
        {
            intent = default;
            ActionKey key = _selector.Current;

            // The runtime has already answered this request (the cooldown is set); end the plan.
            if (_doorResolved)
            {
                EndDoorPlan(key, ctx.Time, false, false);
                return false;
            }

            if (!_detours.TryGetCells(key.TargetId, out IReadOnlyList<Vector2Int> cells) || cells.Count == 0)
            {
                EndDoorPlan(key, ctx.Time, false, false);
                return false;
            }

            if (_grid.GetNode(cells[0]).IsDoorClosed)
            {
                // Only a close this Saboteur asked for is a success worth a cooldown; if someone
                // else shut it, the door simply stopped being a candidate.
                EndDoorPlan(key, ctx.Time, _doorEmitted, false);
                return false;
            }

            // The player may step into the doorway between two decisions (4 Hz): never close a door on
            // them. This is checked on every tick, not only when a decision is made, and it ends the plan
            // with no cooldown, so the door is offered again once they have moved on.
            if (ctx.World != null && ctx.World.Player.IsKnown && ctx.World.Player.IsAlive
                && CloseDoorSource.IsInDoorway(ctx.World.Player.Cell, cells))
            {
                EndDoorPlan(key, ctx.Time, false, false);
                return false;
            }

            // The player has left the room, or the Saboteur has (it was pushed through the doorway): the door would
            // now shut it out of the player's room, or shut nobody in. End the plan with no cooldown; it is offered
            // again once they share a room.
            if (!_detours.IsInPlayerRoom(ctx.Cell) || !_detours.IsPlayerRoomExit(key.TargetId))
            {
                EndDoorPlan(key, ctx.Time, false, false);
                return false;
            }

            _doorActive = true;
            _doorKey = key;
            Vector3 nearest = _grid.CellToWorld(cells[0]);
            float nearestSqr = float.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                Vector3 world = _grid.CellToWorld(cells[i]);
                float dx = world.x - ctx.Position.x;
                float dz = world.z - ctx.Position.z;
                float sqr = dx * dx + dz * dz;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = world;
                }
            }

            if (nearestSqr <= DoorReachMetres * DoorReachMetres)
            {
                // No lockout check here: the player can open any door a Saboteur shuts, so shutting them in
                // is the point. The doorway check above still keeps a door from coming down on them.
                List<Vector3> stop = null;
                if (!_doorInReach)
                {
                    _doorInReach = true;
                    _doorReachedAt = ctx.Time;
                    stop = new List<Vector3>();
                }
                else if (ctx.Time - _doorReachedAt > CloseDoorTimeoutSeconds)
                {
                    EndDoorPlan(key, ctx.Time, false, true);
                    return false;
                }

                _doorEmitted = true;
                intent = new AgentIntent
                {
                    Path = stop,
                    DesiredSpeed = MoveSpeed,
                    LookTarget = nearest,
                    Action = AgentAction.CloseDoor,
                    ActionTargetId = key.TargetId,
                    DebugState = "CloseDoor"
                };
                return true;
            }

            _doorInReach = false;
            if (_doorRouteSent)
            {
                // The body stops short of the last waypoint, so a finished route that is still out of
                // reach means the door cannot be got to; do not stand there.
                if (HasArrivedAt(_doorRoute, ctx.Position))
                {
                    EndDoorPlan(key, ctx.Time, false, true);
                    return false;
                }

                TravelCheck progress = Progress(ref _doorTravel, _doorRoute, ref _doorRouteIndex, ctx.Position, ctx.Time);
                if (progress == TravelCheck.GiveUp)
                {
                    EndDoorPlan(key, ctx.Time, false, true);
                    return false;
                }

                if (progress == TravelCheck.Fine)
                {
                    intent = Keep("CloseDoor");
                    return true;
                }

                _doorRouteSent = false;   // no progress: search again from where the body is now
            }

            Vector2Int target = cells[0];
            float best = float.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                float distance = BaseCostModel.OctileDistance(ctx.Cell, cells[i]);
                if (distance < best)
                {
                    best = distance;
                    target = cells[i];
                }
            }

            if (!TryRoute(ctx.Cell, target, out List<Vector2Int> route))
            {
                EndDoorPlan(key, ctx.Time, false, true);
                return false;
            }

            _doorRoute = route;
            _doorRouteSent = true;
            _doorRouteIndex = 0;
            intent = Move(route, "CloseDoor");
            return true;
        }

        void ResetFactPlan()
        {
            _factActive = false;
            _factResolved = false;
            _factRouteSent = false;
            _factInReach = false;
            _factRoute = null;
            _factTravel = default;
            _factRouteIndex = 0;
        }

        // Carries out the selected ArmTrap or StealBattery: walk to the object's walkable cell, stop
        // within reach and ask the runtime to act on it until it answers or the time is up. Returns false
        // when the plan has ended (the runtime answered, the object is gone or taken, the Saboteur could
        // not get there), so the caller falls back to patrolling. A target that vanished simply stops
        // being a candidate; one the Saboteur could not reach starts the failure cooldown.
        bool TryFactAction(in AgentContext ctx, out AgentIntent intent)
        {
            intent = default;
            ActionKey key = _selector.Current;

            // The runtime has already answered this request (the cooldown is set); end the plan.
            if (_factResolved)
            {
                EndFactPlan(key, ctx.Time, false);
                return false;
            }

            if (!TryFindFact(ctx.World, key, out SabotageFact fact) || !fact.Available
                || !SabotageTargetCells.TrySnap(_grid, fact.Cell, out Vector2Int cell))
            {
                EndFactPlan(key, ctx.Time, false);
                return false;
            }

            _factActive = true;
            _factKey = key;
            bool arming = key.Kind == SaboteurActionKind.ArmTrap;
            string state = arming ? "ArmTrap" : "StealBattery";
            Vector3 target = _grid.CellToWorld(cell);
            float dx = target.x - ctx.Position.x;
            float dz = target.z - ctx.Position.z;

            if (dx * dx + dz * dz <= FactReachMetres * FactReachMetres)
            {
                List<Vector3> stop = null;
                if (!_factInReach)
                {
                    _factInReach = true;
                    _factReachedAt = ctx.Time;
                    stop = new List<Vector3>();
                }
                else if (ctx.Time - _factReachedAt > FactTimeoutSeconds)
                {
                    EndFactPlan(key, ctx.Time, true);
                    return false;
                }

                intent = new AgentIntent
                {
                    Path = stop,
                    DesiredSpeed = MoveSpeed,
                    LookTarget = target,
                    Action = arming ? AgentAction.ArmTrap : AgentAction.StealBattery,
                    ActionTargetId = key.TargetId,
                    DebugState = state
                };
                return true;
            }

            _factInReach = false;
            if (_factRouteSent)
            {
                // The body stops short of the last waypoint, so a finished route that is still out of
                // reach means the object cannot be got to; do not stand there.
                if (HasArrivedAt(_factRoute, ctx.Position))
                {
                    EndFactPlan(key, ctx.Time, true);
                    return false;
                }

                TravelCheck progress = Progress(ref _factTravel, _factRoute, ref _factRouteIndex, ctx.Position, ctx.Time);
                if (progress == TravelCheck.GiveUp)
                {
                    EndFactPlan(key, ctx.Time, true);
                    return false;
                }

                if (progress == TravelCheck.Fine)
                {
                    intent = Keep(state);
                    return true;
                }

                _factRouteSent = false;   // no progress: search again from where the body is now
            }

            if (!TryRoute(ctx.Cell, cell, out List<Vector2Int> route))
            {
                EndFactPlan(key, ctx.Time, true);
                return false;
            }

            _factRoute = route;
            _factRouteSent = true;
            _factRouteIndex = 0;
            intent = Move(route, state);
            return true;
        }

        static bool TryFindFact(WorldBlackboard world, ActionKey key, out SabotageFact fact)
        {
            SabotageFactKind kind = key.Kind == SaboteurActionKind.ArmTrap ? SabotageFactKind.Trap : SabotageFactKind.Battery;
            IReadOnlyList<SabotageFact> facts = world.SabotageFacts;
            for (int i = 0; i < facts.Count; i++)
            {
                if (facts[i].Id == key.TargetId && facts[i].Kind == kind)
                {
                    fact = facts[i];
                    return true;
                }
            }

            fact = default;
            return false;
        }

        // A failure starts this Saboteur's own cooldown and tells the squad, so no squad mate makes the same trip.
        void Fail(ActionKey key, float now)
        {
            _selector.NotifyFailure(key, now);
            _squad.NoteFailure(key, now + SquadFailureSeconds);
        }

        void EndFactPlan(ActionKey key, float now, bool failed)
        {
            if (failed)
                Fail(key, now);

            _selector.CancelCurrent();
            _squad.EndPlan(_identity.AgentId);
            ResetFactPlan();
            _needsRoute = true;
            _holdSent = false;
        }

        void EndDoorPlan(ActionKey key, float now, bool succeeded, bool failed)
        {
            if (succeeded)
                _selector.NotifySuccess(key, now);
            else if (failed)
                Fail(key, now);

            _selector.CancelCurrent();
            _squad.EndPlan(_identity.AgentId);
            ResetDoorPlan();
            _needsRoute = true;
            _holdSent = false;
        }

        bool HasArrivedAt(List<Vector2Int> route, Vector3 position)
        {
            Vector3 end = _grid.CellToWorld(route[route.Count - 1]);
            float dx = position.x - end.x;
            float dz = position.z - end.z;
            return dx * dx + dz * dz <= ArrivalRadius * ArrivalRadius;
        }

        // ---- Search

        // One Search candidate at most: closing in on a player seen out of attack range, else the last
        // sighting (fresh ones score higher), else the last player noise (louder and fresher scores higher).
        void AddSearchCandidate(in AgentContext ctx)
        {
            _searchHasCandidate = false;
            if (ctx.World == null)
                return;

            float now = ctx.Time;
            float score = 0f;
            PlayerSnapshot player = ctx.World.Player;
            if (_awareness.Sees)
            {
                if (_awareness.Distance <= MeleeReachMetres)
                    return;
                score = ApproachScore;
                _searchCandidateApproach = true;
                _searchCandidateGoal = player.Position;
                _searchCandidateDirection = Flat(player.Position - ctx.Position);
            }
            else if (_awareness.HasLastSeen)
            {
                float age = Mathf.Clamp01((now - _awareness.LastSeenTime) / SaboteurAwareness.MemorySeconds);
                score = Mathf.Lerp(SearchSightingScore, SearchSightingMinScore, age);
                Vector3 lead = Flat(_awareness.LastSeenVelocity) * SearchLeadSeconds;
                if (lead.magnitude > SearchLeadMaxMetres)
                    lead = lead.normalized * SearchLeadMaxMetres;
                _searchCandidateApproach = false;
                _searchCandidateGoal = _awareness.LastSeenPosition + lead;
                Vector3 heading = Flat(_awareness.LastSeenVelocity);
                _searchCandidateDirection = heading.sqrMagnitude > 0.25f ? heading : Flat(_awareness.LastSeenPosition - ctx.Position);
            }
            else if (_awareness.HasNoise)
            {
                float age = Mathf.Clamp01((now - _awareness.NoiseTime) / SaboteurAwareness.NoiseMemorySeconds);
                float loud = Mathf.Clamp01(_awareness.NoiseLevel / 100f);
                score = SearchNoiseMinScore + (SearchNoiseScore - SearchNoiseMinScore) * loud * (1f - age);
                _searchCandidateApproach = false;
                _searchCandidateGoal = _awareness.NoisePosition;
                _searchCandidateDirection = Flat(_awareness.NoisePosition - ctx.Position);
            }

            if (score <= 0f)
                return;

            if (_squad.CountOthers(_identity.AgentId, SaboteurActionKind.Search) >= SearchSaturation)
                score *= 0.5f;

            // Closing in leads to an attack, so it is held back the same way when two squad mates already attack.
            if (_searchCandidateApproach)
                score *= _squad.AttackSaturation(_identity.AgentId);

            _searchHasCandidate = true;
            _candidates.Add(new ActionCandidate(SearchKey, score));
            _lastDecision.AddConstant(SearchKey, new ActionScore(score, score));
        }

        void ResetSearchPlan()
        {
            _searchActive = false;
            _searchRouteSent = false;
            _searchRoute = null;
            _searchTravel = default;
            _searchRouteIndex = 0;
            _probed = false;
        }

        // The search is over: what it looked for is marked as searched (unless it was a player in sight),
        // the plan ends and the Saboteur goes back to its patrol.
        void EndSearch(float now)
        {
            if (!_searchApproach)
                _awareness.MarkSearched(now);
            _selector.CancelCurrent();
            _squad.EndPlan(_identity.AgentId);
            ResetSearchPlan();
            _needsRoute = true;
            _holdSent = false;
        }

        // Carries out the selected Search: walk to the point, look round, check one spot further on, done.
        bool TrySearch(in AgentContext ctx, out AgentIntent intent)
        {
            intent = default;
            float now = ctx.Time;

            // The decision chose where to go. A player in sight moves the goal along with them; a new
            // sighting or noise somewhere else (more than a metre from what this search started from) starts
            // the search again. The probe a few metres on is part of the same search, not a new one.
            if (_searchHasCandidate && (!_searchActive || _searchCandidateApproach != _searchApproach
                || (_searchCandidateGoal - _searchOrigin).sqrMagnitude > 1f))
            {
                bool restart = !_searchActive || _searchCandidateApproach != _searchApproach || !_searchCandidateApproach;
                if (restart)
                {
                    ResetSearchPlan();
                    _searchStartedAt = now;
                    _searchPhase = SearchPhase.Walk;
                }

                _searchOrigin = _searchCandidateGoal;
                _searchGoal = _searchCandidateGoal;
                _searchDirection = _searchCandidateDirection;
                _searchApproach = _searchCandidateApproach;
                _searchActive = true;
            }

            if (!_searchActive)
                return false;

            if (now - _searchStartedAt > SearchTimeoutSeconds || now < _searchStartedAt)
            {
                EndSearch(now);
                return false;
            }

            if (_searchPhase == SearchPhase.Look)
            {
                float t = now - _lookStartedAt;
                if (t >= LookAroundSeconds)
                {
                    if (!_probed && !_searchApproach && TryStartProbe(ctx))
                        return TrySearch(ctx, out intent);

                    EndSearch(now);
                    return false;
                }

                // Turn the head round in a full circle, starting the way the player went.
                float angle = Mathf.Atan2(_searchDirection.z, _searchDirection.x) + t / LookAroundSeconds * Mathf.PI * 2f;
                Vector3 look = ctx.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3f;
                intent = new AgentIntent { Path = null, DesiredSpeed = MoveSpeed, LookTarget = look, Action = AgentAction.None, DebugState = "Search" };
                return true;
            }

            Vector3 flat = Flat(_searchGoal - ctx.Position);
            if (flat.magnitude <= SearchArrivalMetres || (_searchRoute != null && HasArrivedAt(_searchRoute, ctx.Position)))
            {
                _searchPhase = SearchPhase.Look;
                _lookStartedAt = now;
                intent = new AgentIntent { Path = new List<Vector3>(), DesiredSpeed = MoveSpeed, LookTarget = ctx.Position + _searchDirection, Action = AgentAction.None, DebugState = "Search" };
                return true;
            }

            // A player in sight keeps moving: their cell is routed to again, at most every ChaseReplanSeconds.
            bool goalMoved = _searchRouteSent && (_searchGoal - _searchRouteGoal).sqrMagnitude > 2.25f && now >= _searchReplanAt;
            if (_searchRouteSent && !goalMoved)
            {
                TravelCheck progress = Progress(ref _searchTravel, _searchRoute, ref _searchRouteIndex, ctx.Position, now);
                if (progress == TravelCheck.GiveUp)
                {
                    EndSearch(now);
                    return false;
                }

                if (progress == TravelCheck.Fine)
                {
                    intent = Keep(_searchApproach ? "Approach" : "Search");
                    return true;
                }
            }

            if (!_grid.TryWorldToCell(_searchGoal, out Vector2Int goalCell) || !TryRoute(ctx.Cell, goalCell, out List<Vector2Int> route))
            {
                // Nowhere to go (behind a closed door, off the floor): look round from here instead.
                _searchPhase = SearchPhase.Look;
                _lookStartedAt = now;
                intent = new AgentIntent { Path = new List<Vector3>(), DesiredSpeed = MoveSpeed, LookTarget = ctx.Position + _searchDirection, Action = AgentAction.None, DebugState = "Search" };
                return true;
            }

            _searchRoute = route;
            _searchRouteSent = true;
            _searchRouteGoal = _searchGoal;
            _searchRouteIndex = 0;
            _searchReplanAt = now + ChaseReplanSeconds;
            intent = Move(route, _searchApproach ? "Approach" : "Search");
            return true;
        }

        // After looking round, one more spot a few metres on, the way the player was going.
        bool TryStartProbe(in AgentContext ctx)
        {
            _probed = true;
            if (_searchDirection.sqrMagnitude < 1e-4f)
                return false;

            Vector3 probe = _searchGoal + _searchDirection.normalized * ProbeMetres;
            if (!_grid.TryWorldToCell(probe, out Vector2Int cell) || !TryRoute(ctx.Cell, cell, out _))
                return false;

            _searchGoal = probe;
            _searchPhase = SearchPhase.Walk;
            _searchRouteSent = false;
            _searchRoute = null;
            _searchTravel = default;
            return true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        AgentIntent Patrol(Vector2Int currentCell, Vector3 position)
        {
            if (_patrolCells.Length == 0)
                return Hold();

            // The room loop is planned at the first patrol, once every squad mate has said where it starts.
            if (_options.RoomPatrol && !_patrolPlanned)
            {
                _patrolPlanned = true;
                _patrolCells = RoomPatrolPlanner.Plan(_grid, _patrolCells, _squad.OtherHomes(_identity.AgentId));
                _patrolIndex = 0;
                _needsRoute = true;
                if (_patrolCells.Length == 0)
                    return Hold();
            }

            // Arrival is by distance: the body stops short of the last waypoint, often in the
            // previous cell, so comparing cells could wait forever. The cell only starts searches.
            if (_routeCells != null && HasArrived(position))
            {
                _patrolIndex = (_patrolIndex + 1) % _patrolCells.Length;
                _needsRoute = true;
            }

            // Stuck on the way (shoved, wedged against something the grid does not know): route again from
            // here, and if that does not help either, go on to the next patrol point.
            if (!_needsRoute && _routeCells != null)
            {
                TravelCheck progress = Progress(ref _patrolTravel, _routeCells, ref _patrolRouteIndex, position, _lastTime);
                if (progress == TravelCheck.SendAgain)
                {
                    _needsRoute = true;
                    _patrolResending = true;
                }
                else if (progress == TravelCheck.GiveUp)
                {
                    _patrolIndex = (_patrolIndex + 1) % _patrolCells.Length;
                    _needsRoute = true;
                }
            }

            if (!_needsRoute)
                return Keep("Patrol");
            if (_routeRetryWaiting || _lastTime < _bodyRetryAt)
                return Hold();

            // Try each patrol point once, starting with the current one; skip unreachable ones.
            bool bodyInTheWay = false;
            for (int attempt = 0; attempt < _patrolCells.Length; attempt++)
            {
                int index = (_patrolIndex + attempt) % _patrolCells.Length;
                bool routed = TryRoute(currentCell, _patrolCells[index], out List<Vector2Int> cells);
                bodyInTheWay |= _routeBlockedByBody;
                if (routed)
                {
                    // A new walk starts a new watch; only the one re-send after no progress keeps it.
                    if (!_patrolResending)
                        _patrolTravel = default;
                    _patrolResending = false;
                    _patrolIndex = index;
                    _routeCells = cells;
                    _patrolRouteIndex = 0;
                    _needsRoute = false;
                    _holdSent = false;
                    return Move(cells, "Patrol");
                }
            }

            // Every way on goes through a body lying in an aisle: wait where it is and look again shortly, since the
            // body will get up or sink away. Not a stranding: the grid has not changed.
            if (bodyInTheWay)
            {
                _routeCells = null;
                _bodyRetryAt = _lastTime + BodyReplanSeconds;
                return Hold();
            }

            // No point of its own loop can be reached: patrol the room it stands in rather than stand still.
            if (TryStrand(currentCell))
                return Patrol(currentCell, position);

            _routeCells = null;
            _routeRetryWaiting = true;
            return Hold();
        }

        // Swaps in a loop over the room the Saboteur stands in, keeping its own loop to go back to. Only with the
        // room patrol on, once (a stranded Saboteur that cannot move either holds), and only for a room with a loop.
        bool TryStrand(Vector2Int currentCell)
        {
            if (!_options.RoomPatrol || _stranded)
                return false;
            if (!_grid.IsTraversable(currentCell) && !_grid.TryFindNearestTraversable(currentCell, StartSnapRadius, out currentCell))
                return false;

            Vector2Int[] local = RoomPatrolPlanner.Plan(_grid, new[] { currentCell }, _squad.OtherHomes(_identity.AgentId));
            if (local.Length < 2)
                return false;

            _homeCells = _patrolCells;
            _patrolCells = local;
            _stranded = true;
            _patrolIndex = 0;
            _routeCells = null;
            _needsRoute = true;
            return true;
        }

        bool HasArrived(Vector3 position)
        {
            Vector3 end = _grid.CellToWorld(_routeCells[_routeCells.Count - 1]);
            float dx = position.x - end.x;
            float dz = position.z - end.z;
            return dx * dx + dz * dz <= ArrivalRadius * ArrivalRadius;
        }

        // A route through a standing body (no way round it) counts as no route, with _routeBlockedByBody set: the
        // body would only be pushed into. Every plan treats it as it treats a closed door.
        bool TryRoute(Vector2Int from, Vector2Int target, out List<Vector2Int> cells)
        {
            cells = null;
            _routeBlockedByBody = false;
            if (!_grid.IsTraversable(target) &&
                !_grid.TryFindNearestTraversable(target, PatrolSnapRadius, out target))
                return false;

            if (!_grid.IsTraversable(from) &&
                !_grid.TryFindNearestTraversable(from, StartSnapRadius, out from))
                return false;

            if (target == from)
                return false;

            _routeCost.SetTime(_lastTime);
            PathResult result = _pathfinder.FindPath(from, target, _routeCost);
            if (!result.Found || result.Cells == null || result.Cells.Count == 0)
                return false;
            if (_routeCost.GoesThroughBody(result.Cells, _grid.CellToWorld(from)))
            {
                _routeBlockedByBody = true;
                return false;
            }

            cells = result.Cells;
            return true;
        }

        // The progress check every walk uses. No progress for a while: the next cells of the route are priced as
        // blocked, so the route sent again goes round whatever is there (a scrapped body the runtime no longer
        // lists, a crate the grid does not know). A standing body on the route ahead (an agent knocked down
        // there, a squad mate standing still) asks for a new route at once, at most every BodyReplanSeconds,
        // without spending the watch's one re-send.
        TravelCheck Progress(ref TravelWatch watch, List<Vector2Int> route, ref int index, Vector3 position, float now)
        {
            TravelCheck check = watch.Check(SaboteurPaths.RemainingMetres(_grid, route, position, ref index), now);
            if (check == TravelCheck.SendAgain)
            {
                BlockAhead(route, index, now);
            }
            else if (check == TravelCheck.Fine && LegReached(position))
            {
                return TravelCheck.SendAgain;   // the next leg round a body
            }
            else if (check == TravelCheck.Fine && now - _roundedAt >= BodyReplanSeconds
                && _routeCost.BodyOnRoute(route, index, BodyLookAheadCells))
            {
                _roundedAt = now;
                return TravelCheck.SendAgain;
            }

            return check;
        }

        // Prices the next cells of the route as blocked for a while, never its last cell (the target itself).
        void BlockAhead(List<Vector2Int> route, int index, float now)
        {
            int last = Math.Min(route.Count - 2, index + BlockAheadCells);
            for (int i = index + 1; i <= last; i++)
                _routeCost.Block(route[i], now + SaboteurRouteCost.BlockedSeconds);
        }

        // A door, trap or battery takes the Saboteur where it is going even with the player beside it: the body does not
        // hold facing the player (the runtime's stand-off) or swing at them on the way. Without this, a Saboteur that
        // came into the player's room could not walk back past them to the door it means to shut.
        static AgentIntent OnAnErrand(AgentIntent intent)
        {
            intent.IgnorePlayer = true;
            return intent;
        }

        AgentIntent Move(List<Vector2Int> cells, string state) =>
            new AgentIntent { Path = Steer(cells), DesiredSpeed = MoveSpeed, Action = AgentAction.None, DebugState = state };

        // The path the body is sent for a route. The runtime string-pulls a path of more than two waypoints over the
        // grid, which does not know about bodies, so a route that goes round a standing body would be pulled straight
        // back through it. A route that passes near one is therefore sent a leg at a time: two waypoints, which are
        // walked as they are, from here to the furthest cell of the route the body can walk to in a straight line
        // clear of walls and bodies. Reaching the end of the leg sends the next (Progress, ChaseRoute).
        List<Vector3> Steer(List<Vector2Int> cells)
        {
            _hasLeg = false;
            var path = new List<Vector3>(cells.Count);
            if (cells.Count > 2 && _routeCost.PassesNearBody(cells))
            {
                int furthest = 1;
                for (int i = cells.Count - 1; i > 1; i--)
                {
                    if (_routeCost.ClearLine(_position, _grid.CellToWorld(cells[i])))
                    {
                        furthest = i;
                        break;
                    }
                }

                if (furthest < cells.Count - 1)
                {
                    _hasLeg = true;
                    _legEnd = _grid.CellToWorld(cells[furthest]);
                    path.Add(_grid.CellToWorld(cells[0]));
                    path.Add(_legEnd);
                    return path;
                }
            }

            for (int i = 0; i < cells.Count; i++)
                path.Add(_grid.CellToWorld(cells[i]));
            return path;
        }

        // True once the body has reached the end of the leg it was sent (and clears it): time for the next leg.
        bool LegReached(Vector3 position)
        {
            if (!_hasLeg)
                return false;
            float dx = position.x - _legEnd.x;
            float dz = position.z - _legEnd.z;
            if (dx * dx + dz * dz > LegArrivalMetres * LegArrivalMetres)
                return false;
            _hasLeg = false;
            return true;
        }

        AgentIntent Hold()
        {
            _needsRoute = true;
            if (_holdSent)
                return Keep("Hold");

            _holdSent = true;
            return Stop("Hold");
        }

        static AgentIntent Keep(string state) =>
            new AgentIntent { Path = null, DesiredSpeed = MoveSpeed, Action = AgentAction.None, DebugState = state };

        static AgentIntent Stop(string state) =>
            new AgentIntent { Path = new List<Vector3>(), DesiredSpeed = 0f, Action = AgentAction.None, DebugState = state };
    }
}
