using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Plans a patrol that covers a Saboteur's room instead of pacing between two points. The room is
    /// everything walkable that can be reached from the Saboteur's first patrol point without passing a door
    /// (doors separate the rooms), cut off at <see cref="MaxRadiusCells"/> in case two rooms are joined by an
    /// open gap. Where two Saboteurs share a room, each keeps the part nearer its own patrol point, so they
    /// sweep different halves. Points are spread across that area, away from walls where it can, and visited
    /// in a loop by nearest neighbour.
    /// </summary>
    /// <remarks>Plain grid work done once per Saboteur, at its first patrol; no physics and no search.</remarks>
    public static class RoomPatrolPlanner
    {
        /// <summary>How far, in cells (18 m), the room is followed from the starting point.</summary>
        public const int MaxRadiusCells = 36;

        /// <summary>The fewest points a planned patrol has; with fewer room cells it falls back to the given points.</summary>
        public const int MinPoints = 3;

        /// <summary>The most points a planned patrol has.</summary>
        public const int MaxPoints = 7;

        /// <summary>Room cells per patrol point: a bigger area gets more points, up to <see cref="MaxPoints"/>.</summary>
        public const int CellsPerPoint = 90;

        /// <summary>The closest, in cells (3 m), two planned points may be.</summary>
        public const int MinSpacingCells = 6;

        /// <summary>Cells of free floor wanted round a point, so the Saboteur does not hug walls and props.</summary>
        public const int Clearance = 2;

        static readonly Vector2Int[] Steps = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        /// <summary>
        /// The patrol points for a Saboteur whose own points are <paramref name="ownPoints"/> (the first one
        /// is its home), with the homes of the other squad members in <paramref name="otherHomes"/>. Returns
        /// <paramref name="ownPoints"/> unchanged when no room can be planned.
        /// </summary>
        public static Vector2Int[] Plan(GridGraph grid, IReadOnlyList<Vector2Int> ownPoints, IReadOnlyList<Vector2Int> otherHomes)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (ownPoints == null || ownPoints.Count == 0)
                return Array.Empty<Vector2Int>();

            var fallback = new Vector2Int[ownPoints.Count];
            for (int i = 0; i < ownPoints.Count; i++)
                fallback[i] = ownPoints[i];

            if (!Snap(grid, ownPoints[0], out Vector2Int home))
                return fallback;

            HashSet<Vector2Int> room = FloodRoom(grid, home);

            // Share the room with other Saboteurs whose home is in it: keep the cells nearer this home.
            var rivals = new List<Vector2Int>();
            if (otherHomes != null)
            {
                for (int i = 0; i < otherHomes.Count; i++)
                {
                    if (Snap(grid, otherHomes[i], out Vector2Int other) && other != home && room.Contains(other))
                        rivals.Add(other);
                }
            }

            var mine = new List<Vector2Int>(room.Count);
            foreach (Vector2Int cell in room)
            {
                float own = BaseCostModel.OctileDistance(cell, home);
                bool keep = true;
                for (int i = 0; i < rivals.Count && keep; i++)
                {
                    float theirs = BaseCostModel.OctileDistance(cell, rivals[i]);
                    // Ties go to the lower cell, so two Saboteurs never both keep or both drop a cell.
                    if (theirs < own || (theirs == own && Less(rivals[i], home)))
                        keep = false;
                }

                if (keep)
                    mine.Add(cell);
            }

            // A stable order makes the plan the same every run.
            mine.Sort(CompareCells);

            int count = Mathf.Clamp(mine.Count / CellsPerPoint, MinPoints, MaxPoints);
            if (mine.Count < MinPoints * MinSpacingCells)
                return fallback;

            List<Vector2Int> open = WithClearance(grid, mine);
            List<Vector2Int> chosen = SpreadPoints(open.Count >= count ? open : mine, ownPoints, room, count);
            if (chosen.Count < MinPoints)
                return fallback;

            return Loop(chosen);
        }

        /// <summary>Every walkable cell reachable from <paramref name="start"/> without passing a door, within <see cref="MaxRadiusCells"/>.</summary>
        public static HashSet<Vector2Int> FloodRoom(GridGraph grid, Vector2Int start)
        {
            var room = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            room.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                for (int i = 0; i < Steps.Length; i++)
                {
                    Vector2Int next = cell + Steps[i];
                    if (room.Contains(next) || !grid.Contains(next))
                        continue;
                    if (Math.Abs(next.x - start.x) > MaxRadiusCells || Math.Abs(next.y - start.y) > MaxRadiusCells)
                        continue;
                    GridNode node = grid.GetNode(next);
                    if (node.DoorId.HasValue || !node.IsSoundTraversable)
                        continue;   // a doorway ends the room; walls and props are not floor

                    room.Add(next);
                    queue.Enqueue(next);
                }
            }

            return room;
        }

        static List<Vector2Int> WithClearance(GridGraph grid, List<Vector2Int> cells)
        {
            var open = new List<Vector2Int>(cells.Count);
            for (int c = 0; c < cells.Count; c++)
            {
                Vector2Int cell = cells[c];
                bool clear = true;
                for (int dx = -Clearance; dx <= Clearance && clear; dx++)
                {
                    for (int dy = -Clearance; dy <= Clearance && clear; dy++)
                    {
                        if (!grid.IsTraversable(new Vector2Int(cell.x + dx, cell.y + dy)))
                            clear = false;
                    }
                }

                if (clear)
                    open.Add(cell);
            }

            return open;
        }

        // Farthest-point sampling, seeded with the Saboteur's own points that lie in the room, so the patrol
        // still passes the places the level designer chose and spreads out from there.
        static List<Vector2Int> SpreadPoints(List<Vector2Int> cells, IReadOnlyList<Vector2Int> ownPoints, HashSet<Vector2Int> room, int count)
        {
            var chosen = new List<Vector2Int>(count);
            for (int i = 0; i < ownPoints.Count && chosen.Count < count; i++)
            {
                if (room.Contains(ownPoints[i]) && FarFromAll(ownPoints[i], chosen, MinSpacingCells))
                    chosen.Add(ownPoints[i]);
            }

            if (chosen.Count == 0 && cells.Count > 0)
                chosen.Add(cells[0]);

            while (chosen.Count < count)
            {
                int bestIndex = -1;
                float bestDistance = 0f;
                for (int i = 0; i < cells.Count; i++)
                {
                    float nearest = float.MaxValue;
                    for (int j = 0; j < chosen.Count; j++)
                        nearest = Math.Min(nearest, BaseCostModel.OctileDistance(cells[i], chosen[j]));
                    if (nearest > bestDistance)
                    {
                        bestDistance = nearest;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0 || bestDistance < MinSpacingCells)
                    break;
                chosen.Add(cells[bestIndex]);
            }

            return chosen;
        }

        // Nearest-neighbour order from the first point, which closes into a loop.
        static Vector2Int[] Loop(List<Vector2Int> points)
        {
            var order = new List<Vector2Int>(points.Count) { points[0] };
            var left = new List<Vector2Int>(points);
            left.RemoveAt(0);
            while (left.Count > 0)
            {
                Vector2Int from = order[order.Count - 1];
                int best = 0;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < left.Count; i++)
                {
                    float d = BaseCostModel.OctileDistance(from, left[i]);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }

                order.Add(left[best]);
                left.RemoveAt(best);
            }

            return order.ToArray();
        }

        static bool FarFromAll(Vector2Int cell, List<Vector2Int> chosen, int spacing)
        {
            for (int i = 0; i < chosen.Count; i++)
            {
                if (BaseCostModel.OctileDistance(cell, chosen[i]) < spacing)
                    return false;
            }

            return true;
        }

        static bool Snap(GridGraph grid, Vector2Int cell, out Vector2Int result)
        {
            if (grid.IsTraversable(cell) && !grid.GetNode(cell).DoorId.HasValue)
            {
                result = cell;
                return true;
            }

            return grid.TryFindNearestTraversable(cell, SaboteurPaths.SnapRadius, out result) && !grid.GetNode(result).DoorId.HasValue;
        }

        static bool Less(Vector2Int a, Vector2Int b) => a.y != b.y ? a.y < b.y : a.x < b.x;

        static int CompareCells(Vector2Int a, Vector2Int b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);
    }
}
