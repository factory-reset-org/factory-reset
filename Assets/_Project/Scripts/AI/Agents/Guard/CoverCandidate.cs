using UnityEngine;

namespace ToyFactory.AI.Agents.Guard
{
    /// <summary>A cell the Guard could take cover in, with its protection and peek ability.</summary>
    public readonly struct CoverCandidate
    {
        public CoverCandidate(Vector2Int cell, float protection, bool canPeek)
        {
            Cell = cell;
            Protection = protection;
            CanPeek = canPeek;
        }

        public Vector2Int Cell { get; }

        /// <summary>1 for full cover, 0.5 for half cover.</summary>
        public float Protection { get; }

        public bool CanPeek { get; }
    }
}
