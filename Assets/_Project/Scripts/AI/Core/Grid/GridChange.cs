using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.AI.Core.Grid
{
    /// <summary>A completed mutation and the cells relevant to path invalidation.</summary>
    public sealed class GridChange
    {
        public int Version { get; }
        public IReadOnlyList<Vector2Int> ChangedCells { get; }

        /// <summary>
        /// Changed cells and their in-bounds neighbours. Includes endpoints of diagonal
        /// moves whose clearance may have changed. Runtime can forward this to brains.
        /// </summary>
        public IReadOnlyList<Vector2Int> AffectedCells { get; }

        internal GridChange(int version, Vector2Int cell, Vector2Int[] affectedCells)
        {
            Version = version;
            ChangedCells = Array.AsReadOnly(new[] { cell });
            AffectedCells = Array.AsReadOnly(affectedCells);
        }
    }
}
