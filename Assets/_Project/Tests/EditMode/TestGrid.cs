using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// A small in-memory <see cref="IGridGraph"/> for search tests. Built from text rows
    /// where '.' is walkable and '#' is blocked; row index is cell.y, column is cell.x.
    /// </summary>
    sealed class TestGrid : IGridGraph
    {
        readonly bool[,] _walkable;

        public int Width { get; }
        public int Height { get; }
        public int Version { get; private set; }

        TestGrid(int width, int height)
        {
            Width = width;
            Height = height;
            _walkable = new bool[width, height];
        }

        public static TestGrid FromRows(params string[] rows)
        {
            var grid = new TestGrid(rows[0].Length, rows.Length);
            for (int y = 0; y < rows.Length; y++)
            {
                if (rows[y].Length != grid.Width)
                    throw new ArgumentException("All rows must be the same length.", nameof(rows));

                for (int x = 0; x < grid.Width; x++)
                    grid._walkable[x, y] = rows[y][x] == '.';
            }
            return grid;
        }

        public static TestGrid Random(int width, int height, float blockedChance, System.Random rng)
        {
            var grid = new TestGrid(width, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    grid._walkable[x, y] = rng.NextDouble() >= blockedChance;
            return grid;
        }

        /// <summary>Changes one cell and bumps <see cref="Version"/>, like a box or door would.</summary>
        public void SetWalkable(Vector2Int cell, bool walkable)
        {
            _walkable[cell.x, cell.y] = walkable;
            Version++;
        }

        public bool IsTraversable(Vector2Int cell) =>
            cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height && _walkable[cell.x, cell.y];

        public void GetNeighbours(Vector2Int cell, List<Vector2Int> results)
        {
            results.Clear();
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    var next = new Vector2Int(cell.x + dx, cell.y + dy);
                    if (!IsTraversable(next))
                        continue;

                    bool diagonal = dx != 0 && dy != 0;
                    if (diagonal && (!IsTraversable(new Vector2Int(cell.x + dx, cell.y))
                                     || !IsTraversable(new Vector2Int(cell.x, cell.y + dy))))
                        continue;

                    results.Add(next);
                }
            }
        }
    }
}
