using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// IS evidence for Docs/AI/GuardBot.md: what the exposure penalty lambda does to a route,
    /// and how many route searches one pushed box costs the Guard. Both are deterministic and
    /// run in well under a second. The tables go to the test output.
    /// </summary>
    [Category("Evidence")]
    public class GuardTuningEvidenceTests
    {
        sealed class CountingPathfinder : IPathfinder
        {
            readonly AStarSearch _inner;
            public int Calls;

            public CountingPathfinder(GridGraph grid) => _inner = new AStarSearch(grid);

            public PathResult FindPath(Vector2Int start, Vector2Int goal, ICostModel cost)
            {
                Calls++;
                return _inner.FindPath(start, goal, cost);
            }
        }

        sealed class NoCoverVisibility : ICoverVisibility
        {
            public bool IsBlocked(Vector2Int cell, float height) => false;
        }

        static List<Vector2Int> CellsOf(GridGraph grid, IReadOnlyList<Vector3> path)
        {
            var cells = new List<Vector2Int>(path.Count);
            for (int i = 0; i < path.Count; i++)
                cells.Add(grid.WorldToCell(path[i]));
            return cells;
        }

        static float Length(List<Vector2Int> cells, ICostModel model)
        {
            float total = 0f;
            for (int i = 1; i < cells.Count; i++)
                total += model.StepCost(cells[i - 1], cells[i]);
            return total;
        }

        [Test]
        public void Evidence_LambdaAgainstDetourAndExposure()
        {
            // A 41 x 15 room. A band of exposed cells, 11 wide and 9 high, sits across the
            // straight route; going round it costs extra steps. For each lambda: the route's
            // length in cells, how many exposed cells it crosses, and its length relative to
            // the plain shortest route.
            var grid = new GridGraph(41, 15, Vector3.zero);
            var search = new AStarSearch(grid);
            var start = new Vector2Int(0, 7);
            var goal = new Vector2Int(40, 7);
            Func<Vector2Int, bool> exposed = c => c.x >= 15 && c.x <= 25 && c.y >= 3 && c.y <= 11;

            PathResult plain = search.FindPath(start, goal, BaseCostModel.Instance);
            float plainLength = Length(plain.Cells, BaseCostModel.Instance);

            var report = new StringBuilder();
            report.AppendLine("Lambda against route (41 x 15 grid, exposed band 11 x 9 across the straight route)");
            report.AppendLine("| lambda | exposed step multiplier | route length (cells) | exposed cells crossed | length / shortest |");
            report.AppendLine("| --- | --- | --- | --- | --- |");

            float lastLength = 0f;
            foreach (float lambda in new[] { 0f, 0.5f, 1f, 3f, 6f, 10f })
            {
                var model = new TacticalCostModel(exposed, lambda);
                PathResult result = search.FindPath(start, goal, model);
                Assert.IsTrue(result.Found);

                float length = Length(result.Cells, BaseCostModel.Instance);
                int crossed = result.Cells.FindAll(c => exposed(c)).Count;
                report.AppendLine($"| {lambda:0.#} | {1f + lambda:0.#}x | {length:0.0} | {crossed} | {length / plainLength:0.00}x |");

                // More lambda never makes the route shorter, and never longer than (1 + lambda) times.
                Assert.GreaterOrEqual(length + 1e-3f, lastLength);
                Assert.LessOrEqual(length, plainLength * (1f + lambda) + 1e-3f);
                lastLength = length;
            }

            TestContext.Out.WriteLine(report.ToString());
            Debug.Log(report.ToString());
        }

        [Test]
        public void Evidence_ReplansPerBoxPush()
        {
            // A Guard walks a long patrol route and a box is pushed ten times, alternately well
            // off the route and onto the cells ahead of it. The count of new route searches each
            // kind of push causes is what the table reports.
            var grid = new GridGraph(60, 20, Vector3.zero);
            var world = new WorldBlackboard();
            var counting = new CountingPathfinder(grid);
            var patrol = new List<Vector3> { grid.CellToWorld(new Vector2Int(55, 10)) };
            var brain = new GuardBrain(grid, counting, world, new NoCoverVisibility(), 7, patrol);

            var position = new Vector2Int(2, 10);
            float time = 0f;
            AgentIntent first = brain.Tick(new AgentContext(position, grid.CellToWorld(position), Vector3.right, time,
                world, default(SensorSnapshot)));
            Assert.IsNotNull(first.Path, "the Guard planned a patrol route");
            List<Vector2Int> route = CellsOf(grid, first.Path);

            int offRoutePushes = 0, onRoutePushes = 0, searchesOnRoute = 0, searchesOffRoute = 0;
            for (int push = 0; push < 10; push++)
            {
                // Alternate: a box beside the route (a cell well off it), then one onto it.
                bool onRoute = push % 2 == 1;
                Vector2Int cell = onRoute
                    ? route[Mathf.Min(route.Count - 1, 8)]
                    : new Vector2Int(30, 2);

                int before = counting.Calls;
                grid.SetWalkable(cell, false);
                brain.OnGraphChanged(new List<Vector2Int> { cell });
                time += 2f;
                AgentIntent intent = brain.Tick(new AgentContext(position, grid.CellToWorld(position), Vector3.right, time,
                    world, default(SensorSnapshot)));
                int searches = counting.Calls - before;
                if (intent.Path != null && intent.Path.Count > 0)
                    route = CellsOf(grid, intent.Path);

                if (onRoute)
                {
                    onRoutePushes++;
                    searchesOnRoute += searches;
                }
                else
                {
                    offRoutePushes++;
                    searchesOffRoute += searches;
                }
            }

            Assert.GreaterOrEqual(searchesOnRoute, 1, "a box on the route makes the Guard plan again");
            Assert.AreEqual(0, searchesOffRoute, "a box off the route costs no search");

            string report =
                "Replans per box push (60 x 20 grid, patrol route about 53 cells)\n" +
                "| Push | Pushes | New searches | Per push |\n| --- | --- | --- | --- |\n" +
                $"| Onto the route | {onRoutePushes} | {searchesOnRoute} | {(float)searchesOnRoute / onRoutePushes:0.0} |\n" +
                $"| Off the route | {offRoutePushes} | {searchesOffRoute} | {(float)searchesOffRoute / offRoutePushes:0.0} |\n";
            TestContext.Out.WriteLine(report);
            Debug.Log(report);
        }
    }
}
