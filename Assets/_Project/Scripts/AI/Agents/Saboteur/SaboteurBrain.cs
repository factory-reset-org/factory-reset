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
    /// One Saboteur instance's utility brain. This skeleton runs selection at 4 Hz through
    /// <see cref="ActionSelector"/> with Idle/Patrol as the only action, handles stun, graph
    /// changes and permanent destruction. Sabotage and combat actions are added on top of
    /// the same selection loop as their world facts become available.
    /// </summary>
    /// <remarks>
    /// Every dependency is passed in at construction, so the brain never looks anything up
    /// and can be tested on a plain <see cref="GridGraph"/>. Movement routes come from the
    /// shared <see cref="IPathfinder"/> (S2's A*); the brain never searches by itself.
    /// </remarks>
    public sealed class SaboteurBrain : IAgentBrain
    {
        /// <summary>Starting move speed in metres per second (plan Appendix A).</summary>
        public const float MoveSpeed = 4.3f;

        /// <summary>Seconds between selection passes (4 Hz).</summary>
        public const float DecisionInterval = 0.25f;

        /// <summary>Constant utility of Idle/Patrol, the fallback every other action must beat.</summary>
        public const float IdleScore = 0.1f;

        /// <summary>How far, in cells, a blocked patrol point may be snapped to a free cell.</summary>
        public const int PatrolSnapRadius = 2;

        static readonly ActionKey IdleKey = new ActionKey(SaboteurActionKind.Idle);

        readonly SaboteurIdentity _identity;
        readonly GridGraph _grid;
        readonly IPathfinder _pathfinder;
        readonly TargetClaims _claims;
        readonly ActionSelector _selector;
        readonly Vector2Int[] _patrolCells;
        readonly List<ActionCandidate> _candidates = new List<ActionCandidate>(8);

        float _nextDecisionTime = float.NegativeInfinity;
        int _patrolIndex;
        List<Vector2Int> _routeCells;
        bool _needsRoute = true;
        bool _holdSent;

        // Set after no patrol point could be reached, so A* is not rerun every tick while
        // holding; cleared by the next selection pass or any grid change.
        bool _routeRetryWaiting;

        float _pendingStun;
        float _stunnedUntil = float.NegativeInfinity;
        bool _stunStopSent;
        bool _destroyed;

        /// <summary>Creates a brain for one Saboteur instance.</summary>
        /// <param name="identity">Claim owner id and letter for this instance.</param>
        /// <param name="grid">The shared navigation grid.</param>
        /// <param name="pathfinder">The shared search over <paramref name="grid"/>.</param>
        /// <param name="claims">The squad's shared target claims.</param>
        /// <param name="patrolPoints">World positions to patrol in a loop; may be empty (the Saboteur holds).</param>
        /// <param name="selectorSettings">Stability tuning; the design defaults when null.</param>
        public SaboteurBrain(SaboteurIdentity identity, GridGraph grid, IPathfinder pathfinder,
            TargetClaims claims, IReadOnlyList<Vector3> patrolPoints, SelectorSettings selectorSettings = null)
        {
            _identity = identity;
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _claims = claims ?? throw new ArgumentNullException(nameof(claims));
            _selector = new ActionSelector(selectorSettings);

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
        }

        /// <summary>This instance's identity.</summary>
        public SaboteurIdentity Identity => _identity;

        /// <summary>The action-target pair currently selected, for the debug panel.</summary>
        public ActionKey CurrentAction => _selector.Current;

        /// <summary>True once <see cref="OnDestroyed"/> has run; the Saboteur never comes back.</summary>
        public bool IsDestroyed => _destroyed;

        /// <inheritdoc />
        public AgentIntent Tick(in AgentContext ctx)
        {
            if (_destroyed)
                return Stop("Destroyed");

            if (_pendingStun > 0f)
            {
                _stunnedUntil = ctx.Time + _pendingStun;
                _pendingStun = 0f;
            }

            if (ctx.Time < _stunnedUntil)
            {
                if (_stunStopSent)
                    return Keep("Stunned");

                _stunStopSent = true;
                return Stop("Stunned");
            }

            if (ctx.Time >= _nextDecisionTime)
            {
                _nextDecisionTime = ctx.Time + DecisionInterval;
                Decide(ctx.Time);
            }

            // Only Idle/Patrol exists so far; later actions branch here on CurrentAction.
            return Patrol(ctx.Cell);
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
            if (_destroyed || !(duration > 0f))
                return;

            // The stun starts on the next tick, where the game time is known.
            _pendingStun = Math.Max(_pendingStun, duration);
            _stunStopSent = false;
            _selector.CancelCurrent();
            _claims.Release(_identity.AgentId);
            _needsRoute = true;
            _holdSent = false;
        }

        /// <inheritdoc />
        public void OnDestroyed()
        {
            if (_destroyed)
                return;

            _destroyed = true;
            _claims.Release(_identity.AgentId);
            _selector.CancelCurrent();
            _routeCells = null;
        }

        void Decide(float now)
        {
            _routeRetryWaiting = false;
            _candidates.Clear();
            _candidates.Add(new ActionCandidate(IdleKey, IdleScore));
            _selector.Select(_candidates, now);
        }

        AgentIntent Patrol(Vector2Int currentCell)
        {
            if (_patrolCells.Length == 0)
                return Hold();

            if (_routeCells != null && currentCell == _routeCells[_routeCells.Count - 1])
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
