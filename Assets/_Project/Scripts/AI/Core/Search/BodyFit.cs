using System;
using UnityEngine;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// Which grid cells a wide body fits through, and which way round. The grid itself only
    /// keeps a 0.55 m capsule clear of walls and props; a body much wider than its capsule
    /// (the Captain's shoulders are 1.9 m across) can stand in a cell whose walls it would
    /// scrape with its shoulders. Built once by the runtime, which measures the level; read by
    /// a brain's cost model.
    /// </summary>
    /// <remarks>
    /// Travelling along X the shoulders spread along Z, and the other way round, so each cell
    /// keeps two answers. A cell neither way fits can still be crossed, since the capsule fits
    /// (the cell is walkable); the shoulders then scrape, so a brain only avoids it.
    /// </remarks>
    public sealed class BodyFit
    {
        const byte AlongX = 1, AlongZ = 2;

        readonly byte[] _fits;

        public int Width { get; }
        public int Height { get; }

        /// <summary>A map of the given grid size on which the body fits everywhere.</summary>
        public BodyFit(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width;
            Height = height;
            _fits = new byte[width * height];
            for (int i = 0; i < _fits.Length; i++)
                _fits[i] = AlongX | AlongZ;
        }

        /// <summary>Records whether the body fits in <paramref name="cell"/> travelling along X and along Z.</summary>
        public void Set(Vector2Int cell, bool alongX, bool alongZ)
        {
            if (!Contains(cell)) throw new ArgumentOutOfRangeException(nameof(cell));
            _fits[cell.y * Width + cell.x] = (byte)((alongX ? AlongX : 0) | (alongZ ? AlongZ : 0));
        }

        /// <summary>True if the body fits in <paramref name="cell"/> walking along X (shoulders across Z). Outside the map: true.</summary>
        public bool FitsAlongX(Vector2Int cell) => !Contains(cell) || (_fits[cell.y * Width + cell.x] & AlongX) != 0;

        /// <summary>True if the body fits in <paramref name="cell"/> walking along Z (shoulders across X). Outside the map: true.</summary>
        public bool FitsAlongZ(Vector2Int cell) => !Contains(cell) || (_fits[cell.y * Width + cell.x] & AlongZ) != 0;

        /// <summary>
        /// True if the body fits stepping from <paramref name="from"/> into the neighbouring
        /// <paramref name="to"/> facing the way it steps: a step along X needs the cell's along-X
        /// answer, a step along Z its along-Z answer, a diagonal step either (it can angle its
        /// shoulders between the two).
        /// </summary>
        public bool FitsStep(Vector2Int from, Vector2Int to)
        {
            bool alongX = from.x != to.x, alongZ = from.y != to.y;
            if (alongX && alongZ)
                return FitsAlongX(to) || FitsAlongZ(to);
            return alongX ? FitsAlongX(to) : FitsAlongZ(to);
        }

        bool Contains(Vector2Int cell) => cell.x >= 0 && cell.x < Width && cell.y >= 0 && cell.y < Height;
    }
}
