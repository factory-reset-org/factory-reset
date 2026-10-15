using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>
    /// What a step costs the Captain: the octile base cost, plus extra where its shoulders
    /// would scrape a shelf or a wall and near other agents (a body in the way is walked round,
    /// a standing one most of all). Only ever adds to the base cost, so the octile heuristic
    /// stays admissible and A* still returns the cheapest route.
    /// </summary>
    /// <remarks>
    /// <para><b>Its shoulders.</b> The grid keeps its 1.1 m capsule clear, but its shoulders,
    /// arms and cannon are 1.9 m across, so the shortest routes hugged shelf edges and the
    /// model slid through the shelves. A step where they would scrape costs
    /// <see cref="ScrapePenalty"/> base steps more, so a route along the middle of an aisle
    /// beats one along a shelf edge. It is a cost, not a wall: where no route keeps them clear
    /// (a narrow doorway) it still goes through, as its capsule fits.</para>
    /// <para><b>Other agents</b> are bodies, not walls: the grid does not know them. A step
    /// into a cell within <see cref="CloseRadius"/> of one costs extra, a lot more if it is
    /// standing (it will still be there), less within <see cref="NearRadius"/>. A moving one
    /// costs little: it will usually have moved on.</para>
    /// </remarks>
    public sealed class CaptainRouteCost : ICostModel
    {
        /// <summary>
        /// Extra cost, in base steps, of a step where its shoulders would scrape: a scraping
        /// stretch is avoided whenever going round is less than four times as long.
        /// </summary>
        public const float ScrapePenalty = 3f;

        /// <summary>Within this (m) of another agent a step is into its body: two capsules touch at about 1 m.</summary>
        public const float CloseRadius = 1.2f;

        /// <summary>Within this (m) a step brushes past another agent.</summary>
        public const float NearRadius = 2f;

        /// <summary>Below this speed (m/s) another agent counts as standing.</summary>
        public const float StandingSpeed = 0.3f;

        /// <summary>Extra cost (base steps) of a step into a standing agent, and past one.</summary>
        public const float StandingClose = 12f, StandingNear = 3f;

        /// <summary>Extra cost (base steps) of a step into a moving agent, and past one.</summary>
        public const float MovingClose = 2f, MovingNear = 0.5f;

        readonly GridGraph _grid;
        readonly List<CrowdMember> _crowd = new List<CrowdMember>();
        BodyFit _fit;

        public CaptainRouteCost(GridGraph grid)
        {
            _grid = grid;
        }

        /// <summary>Where the Captain's shoulders fit (null: everywhere, as before it was measured).</summary>
        public BodyFit Fit
        {
            get => _fit;
            set => _fit = value != null && value.Width == _grid.Width && value.Height == _grid.Height ? value : null;
        }

        /// <summary>The other agents now. Copied.</summary>
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

        public float StepCost(Vector2Int from, Vector2Int to)
        {
            float step = BaseCostModel.Instance.StepCost(from, to);
            float cost = step;
            if (_fit != null && !_fit.FitsStep(from, to))
                cost += ScrapePenalty * step;
            return cost + CrowdPenalty(to) * step;
        }

        /// <summary>Extra cost, in base steps, of standing in <paramref name="cell"/> because of other agents.</summary>
        public float CrowdPenalty(Vector2Int cell)
        {
            if (_crowd.Count == 0)
                return 0f;
            Vector3 at = _grid.CellToWorld(cell);
            float penalty = 0f;
            for (int i = 0; i < _crowd.Count; i++)
            {
                float dx = _crowd[i].Position.x - at.x, dz = _crowd[i].Position.z - at.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance > NearRadius)
                    continue;
                bool standing = _crowd[i].Speed < StandingSpeed;
                if (distance <= CloseRadius)
                    penalty += standing ? StandingClose : MovingClose;
                else
                    penalty += standing ? StandingNear : MovingNear;
            }
            return penalty;
        }

        /// <summary>True if a standing agent is within <see cref="CloseRadius"/> of <paramref name="cell"/>: in the way.</summary>
        public bool BlockedByStanding(Vector2Int cell)
        {
            Vector3 at = _grid.CellToWorld(cell);
            for (int i = 0; i < _crowd.Count; i++)
            {
                if (_crowd[i].Speed >= StandingSpeed)
                    continue;
                float dx = _crowd[i].Position.x - at.x, dz = _crowd[i].Position.z - at.z;
                if (dx * dx + dz * dz <= CloseRadius * CloseRadius)
                    return true;
            }
            return false;
        }
    }
}
