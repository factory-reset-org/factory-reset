using UnityEngine;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Where the Saboteur scene tests put the player so the Saboteur can fairly notice them: in front of it (inside
    /// its vision cone), or on open floor a few metres away with nothing in between.
    /// </summary>
    internal static class SaboteurScenePlacement
    {
        /// <summary>The point <paramref name="metres"/> in front of <paramref name="body"/> on the ground plane.</summary>
        public static Vector3 InFront(Component body, float metres)
        {
            Vector3 forward = body.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            return body.transform.position + forward.normalized * metres;
        }

        /// <summary>
        /// A walkable cell 5 to 7 m from <paramref name="start"/> with open floor in a straight line between them,
        /// and at least 4 m farther from <paramref name="battery"/> than <paramref name="start"/> is.
        /// </summary>
        public static bool TryPlayerCell(GridGraph grid, Vector2Int start, Vector2Int battery, out Vector2Int cell)
        {
            float startToBattery = Vector2Int.Distance(start, battery);
            for (int radius = 10; radius <= 14; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius)
                            continue;
                        cell = new Vector2Int(start.x + dx, start.y + dy);
                        if (!grid.IsTraversable(cell) || Vector2Int.Distance(cell, battery) < startToBattery + 8f)
                            continue;
                        if (OpenLine(grid, start, cell))
                            return true;
                    }
                }
            }

            cell = default;
            return false;
        }

        /// <summary>True when every cell on the straight line between the two is walkable.</summary>
        public static bool OpenLine(GridGraph grid, Vector2Int from, Vector2Int to)
        {
            int steps = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                var cell = new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t)), Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, t)));
                if (!grid.IsTraversable(cell))
                    return false;
            }

            return true;
        }
    }
}
