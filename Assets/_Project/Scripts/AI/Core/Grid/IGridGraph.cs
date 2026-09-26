using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.AI.Core.Grid
{
    /// <summary>
    /// The shared search grid, as seen by pathfinders: a uniform grid of cells where
    /// <c>cell.x</c> runs along world X and <c>cell.y</c> along world Z. Implemented by the
    /// real level grid built from the NavMesh, and by small fake grids in tests.
    /// </summary>
    public interface IGridGraph
    {
        /// <summary>Number of cells along world X.</summary>
        int Width { get; }

        /// <summary>Number of cells along world Z.</summary>
        int Height { get; }

        /// <summary>
        /// Incremented every time any cell's traversability changes (a door opens or closes,
        /// a box settles or moves). Paths record the version they were computed on.
        /// </summary>
        int Version { get; }

        /// <summary>
        /// True if an agent can stand in <paramref name="cell"/> right now. Always false for
        /// cells outside the grid.
        /// </summary>
        bool IsTraversable(Vector2Int cell);

        /// <summary>
        /// Clears <paramref name="results"/> and fills it with the traversable cells an agent
        /// can step to from <paramref name="cell"/>: 8-connected, and a diagonal step is only
        /// allowed when both orthogonal cells beside it are traversable (no corner cutting).
        /// Takes a caller-owned list so searches do not allocate.
        /// </summary>
        void GetNeighbours(Vector2Int cell, List<Vector2Int> results);
    }
}
