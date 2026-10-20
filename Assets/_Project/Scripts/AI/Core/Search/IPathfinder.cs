using UnityEngine;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// A search algorithm that finds a path between two cells on the shared grid.
    /// </summary>
    /// <remarks>
    /// Implementations receive the grid graph they search over when constructed, not per call,
    /// so a single instance is reusable across many <see cref="FindPath"/> calls.
    /// </remarks>
    public interface IPathfinder
    {
        /// <summary>
        /// Finds a route from <paramref name="start"/> to <paramref name="goal"/> using
        /// <paramref name="cost"/> to price each step.
        /// </summary>
        PathResult FindPath(Vector2Int start, Vector2Int goal, ICostModel cost);
    }
}
