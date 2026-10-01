using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>
    /// Predicts which goal the player is heading for from how they have moved. For each
    /// candidate goal g, with s the player's cell about 5 s ago and x their cell now:
    /// <code>
    /// D(g) = C(s→x) + C(x→g) − C(s→g)      detour from the shortest route to g, in metres
    /// w(g) = P(g) · exp(−β · D(g))         prior times likelihood
    /// P(g | movement) = w(g) / Σ w          posterior
    /// </code>
    /// D(g) is 0 while the player stays on a shortest route to g and grows as they stray
    /// from it. It is never negative, because field costs are true shortest paths (the
    /// triangle inequality). See Docs/AI/CaptainBot.md for why β = 0.5.
    /// </summary>
    /// <remarks>
    /// Costs come from Dijkstra fields: one per goal, built the first time the goal appears,
    /// cached by goal id and dropped when the goal leaves. Grid movement and the base cost
    /// model are symmetric, so a field computed from g gives C(x→g) for every x. C(s→x)
    /// comes from one bounded field rooted at the player's current cell. After the first
    /// update for a set of goals, updating again allocates nothing.
    /// </remarks>
    public sealed class GoalInference
    {
        /// <summary>How strongly a detour counts against a goal, per metre.</summary>
        public const float DefaultBeta = 0.5f;

        /// <summary>How far (grid units) to search for a walkable cell when a goal or the player is on a blocked cell.</summary>
        public const int SnapRadius = 4;

        /// <summary>
        /// Bound on the field rooted at the player, in grid units (50 m). The player cannot
        /// walk further than this in the 5 s window, so a larger search would be wasted.
        /// </summary>
        public const float PlayerFieldBound = 100f;

        readonly GridGraph _grid;
        readonly float _beta;
        readonly Dictionary<int, DijkstraField> _fields = new Dictionary<int, DijkstraField>();
        readonly HashSet<int> _currentIds = new HashSet<int>();
        readonly List<int> _removedIds = new List<int>();
        readonly DijkstraField _fromPlayer;

        float[] _priors = new float[8];
        float[] _detours = new float[8];
        bool[] _included = new bool[8];
        float[] _posteriors = new float[8];

        /// <summary>Number of goals in the last update; <see cref="Posterior"/> takes indices below this.</summary>
        public int GoalCount { get; private set; }

        /// <summary>Index (into the last goals list) of the most likely goal, or -1 with no prediction.</summary>
        public int MostLikelyIndex { get; private set; } = -1;

        /// <summary>Posterior of the most likely goal, P(g*); 0 with no prediction.</summary>
        public float Confidence { get; private set; }

        /// <summary>Number of goal fields currently cached.</summary>
        public int CachedFieldCount => _fields.Count;

        /// <summary>How many goal fields have been computed in total, for tests and the performance log.</summary>
        public int FieldComputations { get; private set; }

        public GoalInference(GridGraph grid, float beta = DefaultBeta)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (!(beta > 0f) || float.IsInfinity(beta))
                throw new ArgumentOutOfRangeException(nameof(beta), "Beta must be a positive, finite number.");
            _beta = beta;
            _fromPlayer = new DijkstraField(grid);
        }

        /// <summary>Posterior P(g | movement) of the goal at <paramref name="index"/> in the last goals list.</summary>
        public float Posterior(int index)
        {
            if (index < 0 || index >= GoalCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _posteriors[index];
        }

        /// <summary>Detour D(g) in metres of the goal at <paramref name="index"/> in the last update; 0 if it was left out.</summary>
        public float Detour(int index)
        {
            if (index < 0 || index >= GoalCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _detours[index];
        }

        /// <summary>
        /// Re-runs the prediction. Returns false if there is no prediction: no goal is
        /// reachable or included, or the player is nowhere near a walkable cell. A goal that
        /// cannot be reached gets posterior 0 and is left out of the normalisation.
        /// </summary>
        /// <param name="goals">The current candidate goals.</param>
        /// <param name="pastCell">The player's cell about 5 s ago (<see cref="PlayerTrack.TryGetPast"/>).</param>
        /// <param name="currentCell">The player's cell now.</param>
        /// <param name="finalChapter">True in the final chapter (raises the console prior).</param>
        /// <param name="ammoFraction">The player's ammo, 0 to 1 (batteries count below 30%).</param>
        public bool Update(IReadOnlyList<CandidateGoal> goals, Vector2Int pastCell, Vector2Int currentCell,
            bool finalChapter, float ammoFraction)
        {
            if (goals == null) throw new ArgumentNullException(nameof(goals));

            EnsureCapacity(goals.Count);
            GoalCount = goals.Count;
            SyncFields(goals);
            GoalPriors.Compute(goals, finalChapter, ammoFraction, _priors);

            for (int i = 0; i < goals.Count; i++)
            {
                _included[i] = false;
                _detours[i] = 0f;
            }

            if (_grid.TryFindNearestTraversable(currentCell, SnapRadius, out Vector2Int x) &&
                _grid.TryFindNearestTraversable(pastCell, SnapRadius, out Vector2Int s))
            {
                // Standing still: every detour is 0, so the posterior equals the prior.
                bool moved = s != x;
                float sToX = 0f;
                if (moved)
                {
                    _fromPlayer.Compute(x, BaseCostModel.Instance, PlayerFieldBound);
                    sToX = _fromPlayer.Cost(s);
                }

                for (int i = 0; i < goals.Count; i++)
                {
                    if (_priors[i] <= 0f)
                        continue;
                    DijkstraField field = _fields[goals[i].Id];
                    float xToG = field.Cost(x);
                    if (float.IsPositiveInfinity(xToG))
                        continue; // the player cannot reach this goal

                    _included[i] = true;
                    if (moved && !float.IsPositiveInfinity(sToX))
                    {
                        float sToG = field.Cost(s);
                        // Rounding can leave a tiny negative on an optimal route; clamp to 0.
                        _detours[i] = Mathf.Max(0f, (sToX + xToG - sToG) * GridGraph.CellSize);
                    }
                    // If the past cell is out of reach of the bound (a teleport or respawn),
                    // the movement says nothing and every detour stays 0, i.e. the prior.
                }
            }

            int best = Normalise(_priors, _detours, _included, GoalCount, _beta, _posteriors);
            MostLikelyIndex = best;
            Confidence = best >= 0 ? _posteriors[best] : 0f;
            return best >= 0;
        }

        /// <summary>
        /// The posterior step on its own: P(g) ∝ prior(g) · exp(−β · detour(g)) over the
        /// included goals, written into <paramref name="posteriors"/>. Excluded goals get 0.
        /// Works in log space and subtracts the largest log-weight before exponentiating, so
        /// even huge detours cannot underflow every weight to 0 (which would divide 0 by 0).
        /// Returns the index of the most likely goal, or -1 if nothing is included.
        /// </summary>
        public static int Normalise(float[] priors, float[] detours, bool[] included, int count, float beta, float[] posteriors)
        {
            if (priors == null) throw new ArgumentNullException(nameof(priors));
            if (detours == null) throw new ArgumentNullException(nameof(detours));
            if (included == null) throw new ArgumentNullException(nameof(included));
            if (posteriors == null) throw new ArgumentNullException(nameof(posteriors));
            if (count < 0 || count > priors.Length || count > detours.Length || count > included.Length || count > posteriors.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            int best = -1;
            float bestLog = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (!included[i] || priors[i] <= 0f)
                {
                    posteriors[i] = 0f;
                    continue;
                }
                float logWeight = Mathf.Log(priors[i]) - beta * detours[i];
                posteriors[i] = logWeight; // log-weight for now; turned into a probability below
                if (logWeight > bestLog)
                {
                    bestLog = logWeight;
                    best = i;
                }
            }

            if (best < 0)
                return -1;

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                if (!included[i] || priors[i] <= 0f)
                    continue;
                posteriors[i] = Mathf.Exp(posteriors[i] - bestLog); // the best goal gets exactly 1
                sum += posteriors[i];
            }
            for (int i = 0; i < count; i++)
                posteriors[i] /= sum;

            return best;
        }

        // Builds a field for each new goal, rebuilds fields that are stale or whose goal has
        // moved, and drops the fields of goals that have gone.
        void SyncFields(IReadOnlyList<CandidateGoal> goals)
        {
            _currentIds.Clear();
            for (int i = 0; i < goals.Count; i++)
            {
                CandidateGoal goal = goals[i];
                if (!_currentIds.Add(goal.Id))
                    throw new ArgumentException($"Goal id {goal.Id} appears twice.", nameof(goals));

                if (!_grid.TryFindNearestTraversable(goal.Cell, SnapRadius, out Vector2Int source))
                    source = goal.Cell; // nothing walkable nearby: the field stays unreachable

                if (!_fields.TryGetValue(goal.Id, out DijkstraField field))
                {
                    field = new DijkstraField(_grid);
                    _fields.Add(goal.Id, field);
                }
                if (field.IsStale || field.Source != source)
                {
                    field.Compute(source, BaseCostModel.Instance);
                    FieldComputations++;
                }
            }

            _removedIds.Clear();
            foreach (KeyValuePair<int, DijkstraField> entry in _fields)
                if (!_currentIds.Contains(entry.Key))
                    _removedIds.Add(entry.Key);
            for (int i = 0; i < _removedIds.Count; i++)
                _fields.Remove(_removedIds[i]);
        }

        void EnsureCapacity(int count)
        {
            if (_priors.Length >= count)
                return;
            int size = Mathf.Max(count, _priors.Length * 2);
            _priors = new float[size];
            _detours = new float[size];
            _included = new bool[size];
            _posteriors = new float[size];
        }
    }
}
