using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using Debug = UnityEngine.Debug;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// IS evidence for Docs/AIPerformanceLog.md (S2, Week 14: A* against Dijkstra). On a level-sized grid
    /// (83 x 83 cells, a quarter blocked, seeded) and 20 seeded start/goal pairs, the Guard's A* must find the
    /// same cost as Dijkstra, both with the plain octile cost and with the tactical cost (lambda = 3, a third of
    /// the cells exposed), while expanding fewer cells. Dijkstra here is <see cref="DijkstraField"/> run from the
    /// start and stopped at the goal's cost, so both searches answer the same question. The table goes to the
    /// test output.
    /// </summary>
    [Category("Evidence")]
    public class AStarVsDijkstraEvidenceTests
    {
        const int Size = 83;
        const int Pairs = 20;
        const float Tolerance = 1e-3f;

        static GridGraph LevelSizedGrid(System.Random rng)
        {
            var grid = new GridGraph(Size, Size, Vector3.zero);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (rng.NextDouble() < 0.25)
                        grid.SetWalkable(new Vector2Int(x, y), false);
            return grid;
        }

        static Vector2Int RandomCell(GridGraph grid, System.Random rng)
        {
            while (true)
            {
                var cell = new Vector2Int(rng.Next(Size), rng.Next(Size));
                if (grid.IsTraversable(cell))
                    return cell;
            }
        }

        static float PathCost(List<Vector2Int> cells, ICostModel cost)
        {
            float total = 0f;
            for (int i = 1; i < cells.Count; i++)
                total += cost.StepCost(cells[i - 1], cells[i]);
            return total;
        }

        [Test]
        public void Evidence_AStarMatchesDijkstraAndExpandsFewerCells()
        {
            var rng = new System.Random(2026);
            GridGraph grid = LevelSizedGrid(rng);
            var exposed = new HashSet<Vector2Int>();
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (rng.NextDouble() < 1.0 / 3.0)
                        exposed.Add(new Vector2Int(x, y));

            var models = new (string Name, ICostModel Cost)[]
            {
                ("octile", BaseCostModel.Instance),
                ("tactical, lambda 3", new TacticalCostModel(cell => exposed.Contains(cell), 3f))
            };

            var astar = new AStarSearch(grid);
            var field = new DijkstraField(grid);
            var report = new StringBuilder();
            report.AppendLine($"A* against Dijkstra, {Size} x {Size} grid (25% blocked, seed 2026), {Pairs} pairs");
            report.AppendLine("| Cost | Pairs found | Cost equal on | A* cells expanded (total) | Dijkstra cells expanded (total) | A* ms (total) | Dijkstra ms (total) |");
            report.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

            foreach (var (name, cost) in models)
            {
                var pairRng = new System.Random(7);
                int found = 0, equal = 0;
                long astarNodes = 0, dijkstraNodes = 0;
                double astarMs = 0, dijkstraMs = 0;
                for (int i = 0; i < Pairs; i++)
                {
                    Vector2Int start = RandomCell(grid, pairRng);
                    Vector2Int goal = RandomCell(grid, pairRng);

                    var watch = Stopwatch.StartNew();
                    PathResult result = astar.FindPath(start, goal, cost);
                    watch.Stop();
                    astarMs += watch.Elapsed.TotalMilliseconds;

                    // Dijkstra from the start, bounded by the A* answer so it stops where A* stopped looking
                    // for a cheaper route; without a route it covers the whole reachable grid.
                    float bound = result.Found ? PathCost(result.Cells, cost) + Tolerance : float.PositiveInfinity;
                    watch.Restart();
                    field.Compute(start, cost, bound);
                    watch.Stop();
                    dijkstraMs += watch.Elapsed.TotalMilliseconds;

                    Assert.AreEqual(field.IsReachable(goal), result.Found, $"{name}, pair {i}: reachability differs");
                    if (!result.Found)
                        continue;
                    found++;
                    astarNodes += result.NodesExpanded;
                    dijkstraNodes += field.NodesExpanded;
                    float astarCost = PathCost(result.Cells, cost);
                    Assert.AreEqual(field.Cost(goal), astarCost, Tolerance, $"{name}, pair {i}: A* is not optimal");
                    equal++;
                }

                report.AppendLine($"| {name} | {found} | {equal} | {astarNodes:N0} | {dijkstraNodes:N0} | {astarMs:0.00} | {dijkstraMs:0.00} |");
                Assert.Less(astarNodes, dijkstraNodes, $"{name}: A* expands fewer cells than Dijkstra");
            }

            Debug.Log(report.ToString());
            TestContext.Out.WriteLine(report.ToString());
        }
    }
}
