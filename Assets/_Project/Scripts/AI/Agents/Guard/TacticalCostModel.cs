using System;
using UnityEngine;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Guard
{
    /// <summary>
    /// Tactical A* cost model: stepCost(n -> m) = base(n, m) * (1 + lambda * exposure(m)).
    /// Exposure (whether the player can see cell m) needs a scene Linecast, which this
    /// class never performs itself, the caller supplies it so the model stays pure and
    /// testable without a scene, the same split used by VisionQuery.
    /// </summary>
    public sealed class TacticalCostModel : ICostModel
    {
        readonly Func<Vector2Int, bool> _isExposed;
        readonly float _lambda;

        /// <param name="isExposed">True if the player can currently see the given cell.</param>
        /// <param name="lambda">Exposure penalty multiplier. 3 means an exposed step costs 4x a hidden one.</param>
        public TacticalCostModel(Func<Vector2Int, bool> isExposed, float lambda = 3f)
        {
            _isExposed = isExposed ?? throw new ArgumentNullException(nameof(isExposed));
            _lambda = lambda;
        }

        public float StepCost(Vector2Int from, Vector2Int to)
        {
            float baseCost = BaseCostModel.Instance.StepCost(from, to);
            float exposure = _isExposed(to) ? 1f : 0f;
            return baseCost * (1f + _lambda * exposure);
        }
    }
}
