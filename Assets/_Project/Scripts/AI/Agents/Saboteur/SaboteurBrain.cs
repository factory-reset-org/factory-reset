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
    /// AttackPlayer, and handles stun, graph changes and permanent destruction. The other
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
    public sealed class SaboteurBrain : IAgentBrain, IDropsItems
    {
        /// <summary>Seconds between two shots while attacking (provisional pacing, tuned in play).</summary>
        public const float AttackIntervalSeconds = 1.5f;

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

        // AttackPlayer: whether the body has been stopped for the current attack, and when it
        // may next ask for a shot.
        bool _attacking;
        float _nextShotTime;

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
        public SaboteurBrain(SaboteurIdentity identity, GridGraph grid, IPathfinder pathfinder,
            TargetClaims claims, IReadOnlyList<Vector3> patrolPoints, SelectorSettings selectorSettings = null,
            int keycardItemId = 0, ICandidateSource candidateSource = null)
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

            if (ctx.World != null && _selector.HasCurrent && _selector.Current.Kind == SaboteurActionKind.AttackPlayer
                && AttackPlayerSource.InRange(ctx.World.Player, ctx.Position, out _))
                return Attack(ctx);

            // Back to patrolling after an attack: the body was stopped, so route again.
            if (_attacking)
            {
                _attacking = false;
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
            _attacking = false;
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

        // Face the player and ask for a shot. The body is stopped once when the attack starts and
        // the weapon turns it to face the player while it aims, so the brain only paces the
        // requests; the weapon ignores a request while a shot is still in progress.
        AgentIntent Attack(in AgentContext ctx)
        {
            List<Vector3> path = null;
            if (!_attacking)
            {
                _attacking = true;
                path = new List<Vector3>();
            }

            AgentAction action = AgentAction.None;
            if (ctx.Time >= _nextShotTime)
            {
                action = AgentAction.Shoot;
                _nextShotTime = ctx.Time + AttackIntervalSeconds;
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
