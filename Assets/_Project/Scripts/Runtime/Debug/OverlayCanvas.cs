using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// What overlay layers draw into: world-space lines and labels, collected for one frame and
    /// then drawn by <see cref="AgentDebugOverlay"/> (lines through GL, labels through IMGUI).
    /// Layers never draw directly, so a layer is plain code that is easy to write and test.
    /// </summary>
    public sealed class OverlayCanvas
    {
        /// <summary>One world-space line segment.</summary>
        public readonly struct Line
        {
            public readonly Vector3 From, To;
            public readonly Color Colour;
            public Line(Vector3 from, Vector3 to, Color colour) { From = from; To = to; Colour = colour; }
        }

        /// <summary>One text label anchored at a world point.</summary>
        public readonly struct Label
        {
            public readonly Vector3 Position;
            public readonly string Text;
            public readonly Color Colour;
            public Label(Vector3 position, string text, Color colour) { Position = position; Text = text; Colour = colour; }
        }

        readonly List<Line> _lines = new List<Line>();
        readonly List<Label> _labels = new List<Label>();

        /// <summary>The level grid, for drawing cells; null when no grid has been built.</summary>
        public GridGraph Grid { get; internal set; }

        /// <summary>Lines drawn this frame.</summary>
        public IReadOnlyList<Line> Lines => _lines;

        /// <summary>Labels drawn this frame.</summary>
        public IReadOnlyList<Label> Labels => _labels;

        /// <summary>Forgets everything drawn, ready for the next frame.</summary>
        public void Clear()
        {
            _lines.Clear();
            _labels.Clear();
        }

        /// <summary>A line between two world points.</summary>
        public void DrawLine(Vector3 from, Vector3 to, Color colour) => _lines.Add(new Line(from, to, colour));

        /// <summary>A text label at a world point.</summary>
        public void DrawLabel(Vector3 position, string text, Color colour) => _labels.Add(new Label(position, text, colour));

        /// <summary>The outline of a grid cell, slightly above the floor. Nothing without a grid.</summary>
        public void DrawCell(Vector2Int cell, Color colour, float inset = 0.05f, float height = 0.05f)
        {
            if (Grid == null || !Grid.Contains(cell))
                return;
            Vector3 c = Grid.CellToWorld(cell) + Vector3.up * height;
            float h = GridGraph.CellSize * 0.5f - inset;
            Vector3 a = c + new Vector3(-h, 0f, -h), b = c + new Vector3(h, 0f, -h);
            Vector3 d = c + new Vector3(-h, 0f, h), e = c + new Vector3(h, 0f, h);
            DrawLine(a, b, colour);
            DrawLine(b, e, colour);
            DrawLine(e, d, colour);
            DrawLine(d, a, colour);
        }

        /// <summary>A polyline through grid cells, slightly above the floor. Nothing without a grid.</summary>
        public void DrawCellPath(IReadOnlyList<Vector2Int> cells, Color colour, float height = 0.08f)
        {
            if (Grid == null || cells == null)
                return;
            for (int i = 1; i < cells.Count; i++)
                if (Grid.Contains(cells[i - 1]) && Grid.Contains(cells[i]))
                    DrawLine(Grid.CellToWorld(cells[i - 1]) + Vector3.up * height,
                        Grid.CellToWorld(cells[i]) + Vector3.up * height, colour);
        }
    }
}
