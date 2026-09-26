using UnityEngine;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// The base step cost on the 8-connected grid: 1 for an orthogonal step, sqrt(2) for a
    /// diagonal one, in grid units (multiply by the cell size for metres). Agents with no
    /// tactical preferences path with this; tactical cost models only ever add to it.
    /// </summary>
    public sealed class OctileCostModel : ICostModel
    {
        /// <summary>Cost of one diagonal step.</summary>
        public const float DiagonalCost = 1.41421356f;

        /// <summary>Shared instance; the model has no state.</summary>
        public static readonly OctileCostModel Instance = new OctileCostModel();

        public float StepCost(Vector2Int from, Vector2Int to) =>
            from.x != to.x && from.y != to.y ? DiagonalCost : 1f;
    }
}
