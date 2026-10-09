using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>
    /// Chooses where the Captain waits for the player. Given the field of the predicted goal
    /// g*, it predicts the player's route by walking down that field from the player's cell,
    /// then times both sides at every route cell:
    /// <code>
    /// t_player(i)  = [C(x → g*) − C(r_i → g*)] / v_player    distance still to walk, at sprint speed
    /// t_captain(i) = C(captain → r_i) / v_captain           from one field rooted at the Captain
    /// qualifies    when t_captain(i) + margin ≤ t_player(i)
    /// </code>
    /// The first qualifying chokepoint wins; if there is none, the first qualifying route
    /// cell; if no cell qualifies, the Captain defends g* itself, provided it can reach g*
    /// at all (otherwise there is no plan). See Docs/AI/CaptainBot.md
    /// for why the first cell, why a 1 s margin and why the sprint speed.
    /// </summary>
    /// <remarks>
    /// The route and the timing are field lookups, O(L) for a route of L cells. The only
    /// search is the Captain's own field, and it is bounded: a cell further than the
    /// player's whole walk to g* (minus the margin) can never qualify, so the field stops
    /// spreading there. Lists and fields are reused, so planning allocates nothing once warm.
    /// </remarks>
    public sealed class InterceptPlanner
    {
        /// <summary>Seconds the Captain must arrive before the player.</summary>
        public const float DefaultMarginSeconds = 1f;

        /// <summary>How far (grid units) to search for a walkable cell when the player or Captain is on a blocked cell.</summary>
        public const int SnapRadius = GoalInference.SnapRadius;

        readonly GridGraph _grid;
        readonly float _margin;
        readonly DijkstraField _fromCaptain;
        readonly OneToOneCost _toGoal;
        readonly List<Vector2Int> _route = new List<Vector2Int>(64);
        readonly List<Vector2Int> _otherRoute = new List<Vector2Int>(64);
        readonly HashSet<Vector2Int> _otherRouteCells = new HashSet<Vector2Int>();
        readonly Vector2Int[] _neighbours = new Vector2Int[8];

        /// <summary>The player's predicted route from the last plan: the player's cell first, g* last.</summary>
        public IReadOnlyList<Vector2Int> PredictedRoute => _route;

        /// <summary>Cells expanded by the Captain's field in the last plan, for the performance log.</summary>
        public int LastFieldNodesExpanded => _fromCaptain.NodesExpanded;

        public InterceptPlanner(GridGraph grid, float marginSeconds = DefaultMarginSeconds)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (!(marginSeconds >= 0f) || float.IsInfinity(marginSeconds))
                throw new ArgumentOutOfRangeException(nameof(marginSeconds), "The margin must be a finite number of seconds, zero or more.");
            _margin = marginSeconds;
            _fromCaptain = new DijkstraField(grid);
            _toGoal = new OneToOneCost(grid);
        }

        /// <summary>
        /// Plans an intercept against the player heading for the goal whose field is
        /// <paramref name="goalField"/>.
        /// </summary>
        /// <param name="goalField">The predicted goal's field (<see cref="GoalInference.TryGetGoalField"/>).</param>
        /// <param name="playerCell">The player's cell now.</param>
        /// <param name="captainCell">The Captain's cell now.</param>
        /// <param name="playerSpeed">The player's sprint speed in m/s (the worst case).</param>
        /// <param name="captainSpeed">The Captain's speed in m/s.</param>
        /// <param name="isReserved">Optional: true for cells another agent holds, which are skipped.</param>
        public InterceptPlan Plan(DijkstraField goalField, Vector2Int playerCell, Vector2Int captainCell,
            float playerSpeed, float captainSpeed, Predicate<Vector2Int> isReserved = null)
        {
            if (goalField == null) throw new ArgumentNullException(nameof(goalField));
            CheckSpeed(playerSpeed, nameof(playerSpeed));
            CheckSpeed(captainSpeed, nameof(captainSpeed));

            _route.Clear();
            if (!TraceRoute(goalField, playerCell, _route) ||
                !_grid.TryFindNearestTraversable(captainCell, SnapRadius, out Vector2Int captain))
                return InterceptPlan.None;

            float playerTotal = goalField.Cost(_route[0]);
            ComputeCaptainField(captain, playerTotal, playerSpeed, captainSpeed);

            int firstCell = -1;
            for (int i = 0; i < _route.Count; i++)
            {
                if (!Qualifies(goalField, playerTotal, _route[i], playerSpeed, captainSpeed, isReserved))
                    continue;
                if (_grid.GetNode(_route[i]).IsChokepoint)
                    return MakePlan(InterceptKind.Chokepoint, goalField, playerTotal, i, playerSpeed, captainSpeed);
                if (firstCell < 0)
                    firstCell = i;
            }

            if (firstCell >= 0)
                return MakePlan(InterceptKind.RouteCell, goalField, playerTotal, firstCell, playerSpeed, captainSpeed);

            // No cell gives the margin: the player is too close to g*, so guard g* itself.
            // g* may lie beyond the bounded field, so time the Captain's walk to it with one
            // A* query (a single pair of cells) instead of reporting an arrival of infinity.
            // An unbounded field here cost the whole level for that one number.
            int last = _route.Count - 1;
            Vector2Int goalCell = _route[last];
            if (isReserved != null && isReserved(goalCell))
                return InterceptPlan.None;
            float captainCost = _toGoal.Compute(captain, goalCell, BaseCostModel.Instance);
            // The Captain cannot get there at all (a shut door, a box): there is nothing to
            // defend from here, so no plan. A plan it cannot walk would leave it standing
            // still in Intercept.
            if (float.IsPositiveInfinity(captainCost))
                return InterceptPlan.None;
            return new InterceptPlan(InterceptKind.DefendGoal, goalCell, last,
                PlayerArrival(goalField, playerTotal, goalCell, playerSpeed),
                captainCost * GridGraph.CellSize / captainSpeed);
        }

        /// <summary>
        /// Two goals are nearly equally likely: looks for a chokepoint on both predicted
        /// routes that the Captain reaches with the margin to spare on both, and returns the
        /// first one along the route to <paramref name="fieldA"/>. Returns
        /// <see cref="InterceptPlan.None"/> if there is none, which tells the brain to keep
        /// observing rather than gamble on one goal.
        /// </summary>
        public InterceptPlan PlanShared(DijkstraField fieldA, DijkstraField fieldB, Vector2Int playerCell,
            Vector2Int captainCell, float playerSpeed, float captainSpeed, Predicate<Vector2Int> isReserved = null)
        {
            if (fieldA == null) throw new ArgumentNullException(nameof(fieldA));
            if (fieldB == null) throw new ArgumentNullException(nameof(fieldB));
            CheckSpeed(playerSpeed, nameof(playerSpeed));
            CheckSpeed(captainSpeed, nameof(captainSpeed));

            _route.Clear();
            _otherRoute.Clear();
            if (!TraceRoute(fieldA, playerCell, _route) ||
                !TraceRoute(fieldB, playerCell, _otherRoute) ||
                !_grid.TryFindNearestTraversable(captainCell, SnapRadius, out Vector2Int captain))
                return InterceptPlan.None;

            float totalA = fieldA.Cost(_route[0]);
            float totalB = fieldB.Cost(_otherRoute[0]);
            ComputeCaptainField(captain, Mathf.Max(totalA, totalB), playerSpeed, captainSpeed);

            _otherRouteCells.Clear();
            for (int i = 0; i < _otherRoute.Count; i++)
                _otherRouteCells.Add(_otherRoute[i]);

            for (int i = 0; i < _route.Count; i++)
            {
                Vector2Int cell = _route[i];
                if (!_grid.GetNode(cell).IsChokepoint || !_otherRouteCells.Contains(cell))
                    continue;
                if (Qualifies(fieldA, totalA, cell, playerSpeed, captainSpeed, isReserved) &&
                    Qualifies(fieldB, totalB, cell, playerSpeed, captainSpeed, isReserved))
                {
                    // Report the earlier of the two arrivals: the player could be on either route.
                    float arrivalA = PlayerArrival(fieldA, totalA, cell, playerSpeed);
                    float arrivalB = PlayerArrival(fieldB, totalB, cell, playerSpeed);
                    return new InterceptPlan(InterceptKind.Chokepoint, cell, i, Mathf.Min(arrivalA, arrivalB),
                        CaptainArrival(cell, captainSpeed));
                }
            }
            return InterceptPlan.None;
        }

        // Walks down the goal field from the player's cell. At each cell it steps to the
        // neighbour n with the smallest step(x, n) + C(n): on a true shortest-path field that
        // sum equals C(x) exactly for the next cell of a shortest route, so this follows the
        // player's optimal route to the goal. Ties keep the first neighbour in grid order, so
        // the route is deterministic. Returns false if the player cannot reach the goal.
        bool TraceRoute(DijkstraField field, Vector2Int playerCell, List<Vector2Int> route)
        {
            if (!_grid.TryFindNearestTraversable(playerCell, SnapRadius, out Vector2Int current))
                return false;
            float currentCost = field.Cost(current);
            if (float.IsPositiveInfinity(currentCost))
                return false;

            route.Add(current);
            int limit = _grid.CellCount;
            while (currentCost > 0f && route.Count <= limit)
            {
                int count = _grid.GetNeighboursNonAlloc(current, _neighbours);
                Vector2Int best = current;
                float bestTotal = float.PositiveInfinity;
                float bestCost = currentCost;
                for (int i = 0; i < count; i++)
                {
                    Vector2Int next = _neighbours[i];
                    float nextCost = field.Cost(next);
                    float total = BaseCostModel.Instance.StepCost(current, next) + nextCost;
                    if (total < bestTotal)
                    {
                        bestTotal = total;
                        best = next;
                        bestCost = nextCost;
                    }
                }

                // Every step must go downhill; anything else means the field is stale for
                // this grid, so stop rather than loop.
                if (best == current || !(bestCost < currentCost))
                    break;

                route.Add(best);
                current = best;
                currentCost = bestCost;
            }
            return true;
        }

        // A cell further from the Captain than the player's whole walk (minus the margin)
        // can never qualify, so the Captain's field stops spreading there.
        void ComputeCaptainField(Vector2Int captain, float playerTotalCost, float playerSpeed, float captainSpeed)
        {
            float playerSeconds = playerTotalCost * GridGraph.CellSize / playerSpeed;
            float reachSeconds = Mathf.Max(0f, playerSeconds - _margin);
            float bound = reachSeconds * captainSpeed / GridGraph.CellSize;
            _fromCaptain.Compute(captain, BaseCostModel.Instance, bound);
        }

        bool Qualifies(DijkstraField goalField, float playerTotalCost, Vector2Int cell, float playerSpeed,
            float captainSpeed, Predicate<Vector2Int> isReserved)
        {
            float captainArrival = CaptainArrival(cell, captainSpeed);
            if (float.IsPositiveInfinity(captainArrival))
                return false;
            if (captainArrival + _margin > PlayerArrival(goalField, playerTotalCost, cell, playerSpeed))
                return false;
            return isReserved == null || !isReserved(cell);
        }

        static float PlayerArrival(DijkstraField goalField, float playerTotalCost, Vector2Int cell, float playerSpeed) =>
            (playerTotalCost - goalField.Cost(cell)) * GridGraph.CellSize / playerSpeed;

        float CaptainArrival(Vector2Int cell, float captainSpeed) =>
            _fromCaptain.Cost(cell) * GridGraph.CellSize / captainSpeed;

        InterceptPlan MakePlan(InterceptKind kind, DijkstraField goalField, float playerTotalCost, int routeIndex,
            float playerSpeed, float captainSpeed)
        {
            Vector2Int cell = _route[routeIndex];
            return new InterceptPlan(kind, cell, routeIndex, PlayerArrival(goalField, playerTotalCost, cell, playerSpeed),
                CaptainArrival(cell, captainSpeed));
        }

        static void CheckSpeed(float speed, string name)
        {
            if (!(speed > 0f) || float.IsInfinity(speed))
                throw new ArgumentOutOfRangeException(name, "Speed must be a positive, finite number of m/s.");
        }
    }
}
