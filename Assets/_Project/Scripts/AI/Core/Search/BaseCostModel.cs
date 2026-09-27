using UnityEngine;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// The base step cost on the 8-connected grid: 1 for an orthogonal step, sqrt(2) for a
    /// diagonal one, in grid units (multiply by the cell size for metres). Agents with no
    /// tactical preferences path with this; tactical cost models only ever add to it.
    /// </summary>
    public sealed class BaseCostModel : ICostModel
    {
        /// <summary>Cost of one orthogonal step.</summary>
        public const float StraightCost = 1f;

        /// <summary>Cost of one diagonal step.</summary>
        public const float DiagonalCost = 1.41421356f;

        /// <summary>Shared instance; the model has no state.</summary>
        public static readonly BaseCostModel Instance = new BaseCostModel();

        public float StepCost(Vector2Int from, Vector2Int to) =>
            from.x != to.x && from.y != to.y ? DiagonalCost : StraightCost;

        /// <summary>
        /// Base distance in grid units on an open eight-connected grid. Admissible and
        /// consistent when cost models only add penalties to the base step costs.
        /// </summary>
        public static float OctileDistance(Vector2Int from, Vector2Int to)
        {
            int dx = Mathf.Abs(from.x - to.x);
            int dy = Mathf.Abs(from.y - to.y);
            return Mathf.Abs(dx - dy) + Mathf.Min(dx, dy) * DiagonalCost;
        }
    }
}
