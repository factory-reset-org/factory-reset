using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using Debug = UnityEngine.Debug;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// Measures what choosing a retreat costs, for the IS evidence log (Docs/AIPerformanceLog.md). The
    /// numbers are printed to the console, not asserted: timings depend on the machine, so the
    /// assertions only hold the things that do not (the search budget and that most retreats exist).
    /// </summary>
    public class SaboteurFleeEvidenceTests
    {
        sealed class CountingPathfinder : IPathfinder
        {
            readonly IPathfinder _inner;
            public int Calls;
            public CountingPathfinder(IPathfinder inner) { _inner = inner; }

            public PathResult FindPath(Vector2Int start, Vector2Int goal, ICostModel cost)
            {
                Calls++;
                return _inner.FindPath(start, goal, cost);
            }
        }

        // The level is 41.5 m square, so 83 cells a side. About one cell in six is blocked, in scattered
        // single cells plus three long bars, so rooms are neither empty nor mazes.
        static GridGraph LevelSizedGrid(System.Random random)
        {
            var grid = new GridGraph(84, 84, Vector3.zero);
            using (GridGraph.Batch batch = grid.BeginBatch())
            {
                for (int x = 0; x < grid.Width; x++)
                for (int y = 0; y < grid.Height; y++)
                    if (random.NextDouble() < 0.16)
                        batch.SetWalkable(new Vector2Int(x, y), false);

                for (int bar = 0; bar < 3; bar++)
                {
                    int y = 20 + bar * 22;
                    for (int x = 10; x < 60; x++)
                        batch.SetWalkable(new Vector2Int(x, y), false);
                }

                batch.Commit();
            }

            return grid;
        }

        [Test]
        public void RetreatCostOnALevelSizedGrid()
        {
            const int Runs = 300;
            var random = new System.Random(7);
            GridGraph grid = LevelSizedGrid(random);
            var counting = new CountingPathfinder(new AStarSearch(grid));

            var samples = new List<double>(Runs);
            int found = 0;
            int attempts = 0;
            var stopwatch = new Stopwatch();

            // Warm up, so the first call's JIT does not count.
            FleePlanner.TryChooseRetreat(grid, counting, new Vector2Int(5, 5), grid.CellToWorld(new Vector2Int(5, 5)), grid.CellToWorld(new Vector2Int(9, 5)), null, out _);
            counting.Calls = 0;

            while (attempts < Runs)
            {
                var from = new Vector2Int(random.Next(grid.Width), random.Next(grid.Height));
                var player = new Vector2Int(random.Next(grid.Width), random.Next(grid.Height));
                if (!grid.IsTraversable(from) || !grid.IsTraversable(player))
                    continue;

                Vector3 fromWorld = grid.CellToWorld(from);
                Vector3 playerWorld = grid.CellToWorld(player);
                if (Vector3.Distance(fromWorld, playerWorld) > FleeSource.Range)
                    continue;

                attempts++;
                stopwatch.Restart();
                bool ok = FleePlanner.TryChooseRetreat(grid, counting, from, fromWorld, playerWorld, null, out _);
                stopwatch.Stop();
                samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                if (ok)
                    found++;
            }

            samples.Sort();
            double mean = 0;
            foreach (double sample in samples)
                mean += sample;
            mean /= samples.Count;

            Debug.Log($"FleeEvidence: {Runs} retreats on an 84 x 84 grid: found {found} ({100.0 * found / Runs:0}%), " +
                      $"A* searches per retreat {counting.Calls / (double)Runs:0.00}, " +
                      $"time mean {mean:0.000} ms, median {samples[samples.Count / 2]:0.000} ms, " +
                      $"95th percentile {samples[(int)(samples.Count * 0.95)]:0.000} ms, worst {samples[samples.Count - 1]:0.000} ms");

            Assert.LessOrEqual(counting.Calls, Runs * FleePlanner.MaxSearches, "Never more than the search budget.");
            Assert.Greater(found, Runs / 2, "Most Saboteurs that are faced by the player have somewhere to run.");
        }
    }
}
