using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// What a step costs a Saboteur: the octile base cost, plus extra into a body lying or standing in the way
    /// (another agent knocked down, or one standing still) and into a spot where its own walk got nowhere
    /// recently (something the grid does not know about, such as a scrapped body before it sinks away). Only
    /// ever adds to the base cost, so the octile heuristic stays admissible and A* still returns the cheapest
    /// route: round the obstacle when there is a way round, through it when there is none. The brain treats a
    /// route through a body as no route (<see cref="GoesThroughBody"/>), so it never pushes into one.
    /// </summary>
    /// <remarks>
    /// <para><b>Bodies</b> come from the runtime through <see cref="ICrowdAware"/>: every other agent that is up or
    /// knocked out. Only standing ones count (below <see cref="StandingSpeed"/>): a moving one will have moved on.</para>
    /// <para><b>Blocked spots</b> are learned: when a walk makes no progress, the next cells of its route are
    /// priced like a body for <see cref="BlockedSeconds"/>, so the route sent again goes round them.</para>
    /// </remarks>
    public sealed class SaboteurRouteCost : ICostModel
    {
        /// <summary>Within this (m) of a standing body a step is into it: a Saboteur and a lying body touch at about 1 m.</summary>
        public const float BodyRadius = 1.1f;

        /// <summary>
        /// Within this (m) of a standing body a route counts as going round it, and the brain sends it a leg at a time
        /// (the runtime would otherwise pull it straight through the body).
        /// </summary>
        public const float NearRadius = 2f;

        /// <summary>Spacing (m) of the checks along a straight leg.</summary>
        public const float LineStepMetres = 0.25f;

        /// <summary>Below this speed (m/s) another agent counts as standing (a knocked-down one is at 0).</summary>
        public const float StandingSpeed = 0.3f;

        /// <summary>
        /// Extra cost, in base steps, of a step into a standing body or a blocked spot: a way round up to about
        /// ten metres longer is taken instead.
        /// </summary>
        public const float BlockedPenalty = 20f;

        /// <summary>Seconds a spot where a walk got nowhere stays priced as blocked.</summary>
        public const float BlockedSeconds = 8f;

        readonly GridGraph _grid;
        readonly List<CrowdMember> _crowd = new List<CrowdMember>();
        readonly Dictionary<Vector2Int, float> _blockedUntil = new Dictionary<Vector2Int, float>();
        readonly List<Vector2Int> _expired = new List<Vector2Int>();
        float _now;

        /// <summary>Creates the cost model over the level grid.</summary>
        public SaboteurRouteCost(GridGraph grid)
        {
            _grid = grid ?? throw new System.ArgumentNullException(nameof(grid));
        }

        /// <summary>The other agents now. Copied, since the runtime reuses its list.</summary>
        public void SetCrowd(IReadOnlyList<CrowdMember> others)
        {
            _crowd.Clear();
            if (others == null)
                return;
            for (int i = 0; i < others.Count; i++)
                _crowd.Add(others[i]);
        }

        /// <summary>The other agents as last set.</summary>
        public IReadOnlyList<CrowdMember> Crowd => _crowd;

        /// <summary>The game time blocked spots are judged at; set before each search. Expired spots are dropped.</summary>
        public void SetTime(float now)
        {
            _now = now;
            if (_blockedUntil.Count == 0)
                return;

            _expired.Clear();
            foreach (KeyValuePair<Vector2Int, float> spot in _blockedUntil)
            {
                if (spot.Value <= now)
                    _expired.Add(spot.Key);
            }
            for (int i = 0; i < _expired.Count; i++)
                _blockedUntil.Remove(_expired[i]);
        }

        /// <summary>Prices <paramref name="cell"/> as blocked until <paramref name="until"/> (game time).</summary>
        public void Block(Vector2Int cell, float until)
        {
            if (!_blockedUntil.TryGetValue(cell, out float current) || current < until)
                _blockedUntil[cell] = until;
        }

        /// <summary>True while <paramref name="cell"/> is a learned blocked spot.</summary>
        public bool IsBlocked(Vector2Int cell) => _blockedUntil.TryGetValue(cell, out float until) && until > _now;

        /// <inheritdoc />
        public float StepCost(Vector2Int from, Vector2Int to)
        {
            float step = BaseCostModel.Instance.StepCost(from, to);
            return InTheWay(to) ? step * (1f + BlockedPenalty) : step;
        }

        /// <summary>True when a standing body is within <see cref="BodyRadius"/> of <paramref name="cell"/>, or it is a blocked spot.</summary>
        public bool InTheWay(Vector2Int cell) => IsBlocked(cell) || BodyAt(cell);

        /// <summary>
        /// True when a standing body lies on <paramref name="route"/> within <paramref name="lookAhead"/> cells after
        /// <paramref name="index"/>, not counting the last cell (the goal itself may be crowded).
        /// </summary>
        public bool BodyOnRoute(IReadOnlyList<Vector2Int> route, int index, int lookAhead)
        {
            if (route == null || _crowd.Count == 0)
                return false;

            int last = Mathf.Min(route.Count - 2, index + lookAhead);
            for (int i = Mathf.Max(0, index); i <= last; i++)
            {
                if (BodyAt(route[i]))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="route"/> goes deeper into a standing body than <paramref name="from"/> already is:
        /// some cell before its last is inside <see cref="BodyRadius"/> of the body and nearer to it than the start.
        /// That is a route with no way round (an aisle with a body lying in it), which the body could only push
        /// into. A route that starts pressed against a body and turns away from it does not count.
        /// </summary>
        public bool GoesThroughBody(IReadOnlyList<Vector2Int> route, Vector3 from)
        {
            if (route == null || _crowd.Count == 0)
                return false;

            for (int b = 0; b < _crowd.Count; b++)
            {
                if (_crowd[b].Speed >= StandingSpeed)
                    continue;

                Vector3 body = _crowd[b].Position;
                float start = Flat(from, body);
                for (int i = 0; i < route.Count - 1; i++)
                {
                    float d = Flat(_grid.CellToWorld(route[i]), body);
                    if (d < BodyRadius && d < start - 0.05f)
                        return true;
                }
            }

            return false;
        }

        /// <summary>True when a cell of <paramref name="route"/> passes within <see cref="NearRadius"/> of a standing body.</summary>
        public bool PassesNearBody(IReadOnlyList<Vector2Int> route)
        {
            if (route == null || _crowd.Count == 0)
                return false;

            for (int b = 0; b < _crowd.Count; b++)
            {
                if (_crowd[b].Speed >= StandingSpeed)
                    continue;
                for (int i = 0; i < route.Count; i++)
                {
                    if (Flat(_grid.CellToWorld(route[i]), _crowd[b].Position) < NearRadius)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when a straight walk from <paramref name="from"/> to <paramref name="to"/> stays on walkable cells and
        /// out of every standing body: no point of it inside <see cref="BodyRadius"/> and nearer the body than the start.
        /// </summary>
        public bool ClearLine(Vector3 from, Vector3 to)
        {
            float length = Flat(from, to);
            int samples = Mathf.Max(1, Mathf.CeilToInt(length / LineStepMetres));
            for (int s = 1; s <= samples; s++)
            {
                Vector3 at = Vector3.Lerp(from, to, s / (float)samples);
                if (!_grid.TryWorldToCell(at, out Vector2Int cell) || !_grid.IsTraversable(cell))
                    return false;
                for (int b = 0; b < _crowd.Count; b++)
                {
                    if (_crowd[b].Speed >= StandingSpeed)
                        continue;
                    float d = Flat(at, _crowd[b].Position);
                    if (d < BodyRadius && d < Flat(from, _crowd[b].Position) - 0.05f)
                        return false;
                }
            }

            return true;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        bool BodyAt(Vector2Int cell)
        {
            if (_crowd.Count == 0)
                return false;

            Vector3 at = _grid.CellToWorld(cell);
            for (int i = 0; i < _crowd.Count; i++)
            {
                if (_crowd[i].Speed >= StandingSpeed)
                    continue;
                float dx = _crowd[i].Position.x - at.x;
                float dz = _crowd[i].Position.z - at.z;
                if (dx * dx + dz * dz <= BodyRadius * BodyRadius)
                    return true;
            }

            return false;
        }
    }
}
