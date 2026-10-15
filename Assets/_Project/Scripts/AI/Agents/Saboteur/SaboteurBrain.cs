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
    /// Every dependency is passed in at construction, so the brain never looks anything up
    /// and can be tested on a plain <see cref="GridGraph"/>. Movement routes come from the
    /// shared <see cref="IPathfinder"/> (S2's A*); the brain never searches by itself.
    /// </remarks>
    public sealed class SaboteurBrain : IAgentBrain, IDropsItems, IActionFeedback, IHealthAware
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

        /// <summary>How far, in cells, a blocked patrol point may be snapped to a free cell.</summary>
        public const int PatrolSnapRadius = 2;

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

        readonly SaboteurIdentity _identity;
        readonly GridGraph _grid;
        readonly IPathfinder _pathfinder;
        readonly SquadCoordinator _squad;
        readonly ICandidateSource _source;
        readonly DetourCache _detours;
        readonly ActionSelector _selector;
        readonly Vector2Int[] _patrolCells;
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

        // ArmTrap and StealBattery: the plan for the selected trap or battery, one at a time like a door.
        // Travel sends one route, then the Saboteur stops within reach and asks until the runtime answers.
        bool _factActive;
        bool _factResolved;
        bool _factRouteSent;
        bool _factInReach;
        float _factReachedAt;
        ActionKey _factKey;
        List<Vector2Int> _factRoute;

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
        public SaboteurBrain(SaboteurIdentity identity, GridGraph grid, IPathfinder pathfinder,
            TargetClaims claims, IReadOnlyList<Vector3> patrolPoints, SelectorSettings selectorSettings = null,
            int keycardItemId = 0, ICandidateSource candidateSource = null, DetourCache detourCache = null)
        {
            // default(SaboteurIdentity) would otherwise pass as "Saboteur A, the keycard carrier".
            if (!identity.IsAssigned)
                throw new ArgumentException("The Saboteur needs an identity from the spawner.", nameof(identity));

            _identity = identity;
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            if (claims == null)
                throw new ArgumentNullException(nameof(claims));

            _selector = new ActionSelector(selectorSettings);
            _keycardItemId = keycardItemId;
            _source = candidateSource;
            _detours = detourCache;

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

        /// <inheritdoc />
        public AgentIntent Tick(in AgentContext ctx)
        {
            if (_destroyed)
                return Stop("Destroyed");

            _lastCell = ctx.Cell;
            _hasLastCell = true;
            _lastTime = ctx.Time;

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
                return closeDoor;

            if (ctx.World != null && _selector.HasCurrent
                && (_selector.Current.Kind == SaboteurActionKind.ArmTrap || _selector.Current.Kind == SaboteurActionKind.StealBattery)
                && TryFactAction(ctx, out AgentIntent factAction))
                return factAction;

            if (ctx.World != null && _selector.HasCurrent && _selector.Current.Kind == SaboteurActionKind.Flee
                && TryFlee(ctx, out AgentIntent flee))
                return flee;

            // A Saboteur that wants to flee but has nowhere to run fights back instead.
            bool attackSelected = _selector.HasCurrent && (_selector.Current.Kind == SaboteurActionKind.AttackPlayer
                || (_selector.Current.Kind == SaboteurActionKind.Flee && _cornered));
            if (ctx.World != null && attackSelected && AttackPlayerSource.InRange(ctx.World.Player, ctx.Position, out float playerDistance))
                return Attack(ctx, playerDistance);

            // Back to patrolling after an attack, a flight or a door: the body was stopped or sent elsewhere, so route again.
            if (_attacking || _doorActive || _fleeing || _factActive)
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
                _selector.NotifyFailure(key, _lastTime);

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

                // The squad layer: other instances' claims and attacks change this instance's
                // scores. The trace keeps the source's score as the raw score and the adjusted
                // one as the base score, so a veto shows up as a base score of 0.
                for (int i = 1; i < _candidates.Count; i++)
                {
                    ActionCandidate candidate = _candidates[i];
                    float raw = candidate.BaseScore;
                    float adjusted = raw * _squad.NotClaimedFactor(id, candidate.Key);
                    if (candidate.Key.Kind == SaboteurActionKind.AttackPlayer)
                        adjusted *= _squad.AttackSaturation(id);

                    _candidates[i] = new ActionCandidate(candidate.Key, adjusted);
                    _lastDecision.AddConstant(candidate.Key, new ActionScore(raw, adjusted));
                }
            }

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
            if (_chasing && target == _chaseCell)
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
            var path = new List<Vector3>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
                path.Add(_grid.CellToWorld(cells[i]));
            return path;
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

                intent = Keep("CloseDoor");
                return true;
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

                intent = Keep(state);
                return true;
            }

            if (!TryRoute(ctx.Cell, cell, out List<Vector2Int> route))
            {
                EndFactPlan(key, ctx.Time, true);
                return false;
            }

            _factRoute = route;
            _factRouteSent = true;
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

        void EndFactPlan(ActionKey key, float now, bool failed)
        {
            if (failed)
                _selector.NotifyFailure(key, now);

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
                _selector.NotifyFailure(key, now);

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

        AgentIntent Patrol(Vector2Int currentCell, Vector3 position)
        {
            if (_patrolCells.Length == 0)
                return Hold();

            // Arrival is by distance: the body stops short of the last waypoint, often in the
            // previous cell, so comparing cells could wait forever. The cell only starts searches.
            if (_routeCells != null && HasArrived(position))
            {
                _patrolIndex = (_patrolIndex + 1) % _patrolCells.Length;
                _needsRoute = true;
            }

            if (!_needsRoute)
                return Keep("Patrol");
            if (_routeRetryWaiting)
                return Hold();

            // Try each patrol point once, starting with the current one; skip unreachable ones.
            for (int attempt = 0; attempt < _patrolCells.Length; attempt++)
            {
                int index = (_patrolIndex + attempt) % _patrolCells.Length;
                if (TryRoute(currentCell, _patrolCells[index], out List<Vector2Int> cells))
                {
                    _patrolIndex = index;
                    _routeCells = cells;
                    _needsRoute = false;
                    _holdSent = false;
                    return Move(cells, "Patrol");
                }
            }

            _routeCells = null;
            _routeRetryWaiting = true;
            return Hold();
        }

        bool HasArrived(Vector3 position)
        {
            Vector3 end = _grid.CellToWorld(_routeCells[_routeCells.Count - 1]);
            float dx = position.x - end.x;
            float dz = position.z - end.z;
            return dx * dx + dz * dz <= ArrivalRadius * ArrivalRadius;
        }

        bool TryRoute(Vector2Int from, Vector2Int target, out List<Vector2Int> cells)
        {
            cells = null;
            if (!_grid.IsTraversable(target) &&
                !_grid.TryFindNearestTraversable(target, PatrolSnapRadius, out target))
                return false;

            if (target == from)
                return false;

            PathResult result = _pathfinder.FindPath(from, target, BaseCostModel.Instance);
            if (!result.Found || result.Cells == null || result.Cells.Count == 0)
                return false;

            cells = result.Cells;
            return true;
        }

        AgentIntent Move(List<Vector2Int> cells, string state)
        {
            var path = new List<Vector3>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
                path.Add(_grid.CellToWorld(cells[i]));

            return new AgentIntent { Path = path, DesiredSpeed = MoveSpeed, Action = AgentAction.None, DebugState = state };
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
