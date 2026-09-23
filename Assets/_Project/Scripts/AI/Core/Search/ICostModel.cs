using UnityEngine;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// Returns the cost of moving from one grid cell to an adjacent cell.
    /// </summary>
    /// <remarks>
    /// The value returned for any pair of adjacent cells must be greater than
    /// or equal to the base octile step cost (1 for an orthogonal move, sqrt(2)
    /// for a diagonal move). Cost models only ever add penalties on top of the
    /// base step cost, never discounts, so that a heuristic computed from the
    /// base octile distance stays admissible for every implementation.
    /// </remarks>
    public interface ICostModel
    {
        /// <summary>
        /// Cost of the single step from <paramref name="from"/> to
        /// <paramref name="to"/>. The two cells must be adjacent on the grid.
        /// </summary>
        float StepCost(Vector2Int from, Vector2Int to);
    }
}
