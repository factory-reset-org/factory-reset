using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// The outcome of a single <see cref="IPathfinder.FindPath"/> call.
    /// </summary>
    public readonly struct PathResult
    {
        /// <summary>
        /// Cells from start to goal, inclusive. Null when <see cref="Found"/> is false.
        /// </summary>
        public readonly List<Vector2Int> Cells;

        /// <summary>True when a path to the goal was found.</summary>
        public readonly bool Found;

        /// <summary>Number of nodes the search expanded, for the performance log and debug overlay.</summary>
        public readonly int NodesExpanded;

        /// <summary>Wall-clock time the search took, in milliseconds.</summary>
        public readonly float ElapsedMs;

        /// <summary>
        /// The grid's <c>Version</c> at the time this result was computed. Callers compare this
        /// against the grid's current version to detect a stale path after the grid changes.
        /// </summary>
        public readonly int GraphVersion;

        public PathResult(List<Vector2Int> cells, bool found, int nodesExpanded, float elapsedMs, int graphVersion)
        {
            Cells = cells;
            Found = found;
            NodesExpanded = nodesExpanded;
            ElapsedMs = elapsedMs;
            GraphVersion = graphVersion;
        }

        /// <summary>Convenience factory for a failed search that still reports its cost.</summary>
        public static PathResult NotFound(int nodesExpanded, float elapsedMs, int graphVersion) =>
            new PathResult(null, false, nodesExpanded, elapsedMs, graphVersion);
    }
}
