using UnityEngine;

namespace ToyFactory.AI.Agents.Guard
{
    /// <summary>
    /// One cover cell the Guard costed with a real path in its latest cover evaluation, with
    /// the score it ended up with. Read-only, for the debug overlay and tests.
    /// </summary>
    public readonly struct ScoredCover
    {
        public ScoredCover(CoverCandidate candidate, float pathCost, float score)
        {
            Cell = candidate.Cell;
            Protection = candidate.Protection;
            CanPeek = candidate.CanPeek;
            PathCost = pathCost;
            Score = score;
        }

        public Vector2Int Cell { get; }

        /// <summary>1 for full cover, 0.5 for half cover.</summary>
        public float Protection { get; }

        public bool CanPeek { get; }

        /// <summary>Tactical cost of walking there, in grid units, capped at the brain's MaxPathCost.</summary>
        public float PathCost { get; }

        /// <summary>The cover score S, 0 to 1.</summary>
        public float Score { get; }
    }
}
