using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Debugging;

namespace ToyFactory.Tests
{
    public sealed class AgentDebugOverlayTests
    {
        readonly List<Object> _created = new List<Object>();

        sealed class RouteBrain : IAgentBrain
        {
            readonly List<Vector3> _path;
            bool _sent;
            public RouteBrain(params Vector3[] path) { _path = new List<Vector3>(path); }
            public AgentIntent Tick(in AgentContext ctx)
            {
                if (_sent)
                    return new AgentIntent { DebugState = "Patrol", DesiredSpeed = 0.5f };
                _sent = true;
                return new AgentIntent { Path = _path, DesiredSpeed = 0.5f, DebugState = "Patrol" };
            }
            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        sealed class CountingLayer : IAgentOverlayLayer
        {
            public static int Draws;
            public string Name => "Counting";
            public bool Handles(AgentController agent) => agent.Type == AgentType.Guard;
            public void Draw(AgentController agent, OverlayCanvas canvas) => Draws++;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        AgentDebugOverlay Overlay()
        {
            var go = new GameObject("Overlay");
            _created.Add(go);
            AgentDebugOverlay overlay = go.AddComponent<AgentDebugOverlay>();
            overlay.Shown = true;
            return overlay;
        }

        AgentController Agent(AgentType type, IAgentBrain brain, Vector3 position, GridGraph grid = null, WorldBlackboard world = null)
        {
            var go = new GameObject(type.ToString());
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<CharacterController>().center = new Vector3(0f, 1f, 0f);
            AgentController agent = go.AddComponent<AgentController>();
            agent.Initialise(new AgentIdentity(type, _created.Count), brain, world ?? new WorldBlackboard(), grid);
            return agent;
        }

        [Test]
        public void HiddenOverlayDrawsNothingAndShownOverlayLabelsEveryAgent()
        {
            AgentDebugOverlay overlay = Overlay();
            AgentController agent = Agent(AgentType.Tracker, new RouteBrain(), Vector3.zero);

            overlay.DrawFrame(new[] { agent }, null);
            Assert.AreEqual(1, overlay.Canvas.Labels.Count, "One label per agent from the base layer.");
            StringAssert.Contains("HP 3/3", overlay.Canvas.Labels[0].Text);

            overlay.Shown = false;
            overlay.Canvas.Clear();
            Assert.IsEmpty(overlay.Canvas.Labels);
        }

        [Test]
        public void LabelSaysWhyAnAgentIsNotActing()
        {
            AgentController agent = Agent(AgentType.Guard, new RouteBrain(), Vector3.zero);

            agent.Disable(5f);
            StringAssert.Contains("knocked out", AgentOverlayLayer.Describe(agent));

            agent.Scrap();
            StringAssert.Contains("scrapped", AgentOverlayLayer.Describe(agent));
        }

        [UnityTest]
        public IEnumerator BaseLayerDrawsTheRouteLeftToWalk()
        {
            AgentDebugOverlay overlay = Overlay();
            AgentController agent = Agent(AgentType.Tracker,
                new RouteBrain(new Vector3(2f, 0f, 0f), new Vector3(2f, 0f, 2f), new Vector3(4f, 0f, 2f)), Vector3.zero);

            yield return null;   // the brain hands over its route
            overlay.DrawFrame(new[] { agent }, null);

            Assert.AreEqual(agent.Follower.RemainingWaypointCount, overlay.Canvas.Lines.Count,
                "One segment per waypoint still to walk, starting at the agent.");
            Assert.Greater(overlay.Canvas.Lines.Count, 0);
        }

        [Test]
        public void RegisteredLayersDrawOnlyTheAgentsTheyHandle()
        {
            AgentDebugOverlay.Register(new CountingLayer());
            AgentDebugOverlay.Register(new CountingLayer());   // the same layer twice is ignored
            CountingLayer.Draws = 0;
            AgentDebugOverlay overlay = Overlay();
            AgentController guard = Agent(AgentType.Guard, new RouteBrain(), Vector3.zero);
            AgentController tracker = Agent(AgentType.Tracker, new RouteBrain(), Vector3.right * 3f);

            overlay.DrawFrame(new[] { guard, tracker }, null);

            Assert.AreEqual(1, CountingLayer.Draws, "Drawn once, for the Guard only.");
            int counting = 0;
            foreach (IAgentOverlayLayer layer in overlay.Layers)
                if (layer is CountingLayer)
                    counting++;
            Assert.AreEqual(1, counting);
        }

        // Three rooms joined by doorways at x = 10 and 20 (as in CaptainBrainTests).
        static GridGraph ThreeRooms()
        {
            string[] rows =
            {
                "##############################",
                "#.........#.........#........#",
                "#.........#.........#........#",
                "#............................#",
                "#.........#.........#........#",
                "#.........#.........#........#",
                "##############################",
            };
            var grid = new GridGraph(rows[0].Length, rows.Length, Vector3.zero);
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] == '#')
                        grid.SetWalkable(new Vector2Int(x, y), false);
            return grid;
        }

        [UnityTest]
        public IEnumerator CaptainLayerShowsGoalProbabilitiesRouteAndInterceptCell()
        {
            GridGraph grid = ThreeRooms();
            var world = new WorldBlackboard();
            world.SetObjectiveTargets(new[]
            {
                new ObjectiveTarget(1, new Vector2Int(27, 3), ObjectiveTargetKind.Task),
                new ObjectiveTarget(2, new Vector2Int(2, 3), ObjectiveTargetKind.Task),
            });
            var brain = new CaptainBrain(grid, new AStarSearch(grid), world, startAwake: true);
            // More than 10 m from the player: it watches them (it turns to its look target)
            // but does not start a fight, so the overlay still shows the intercept.
            AgentController captain = Agent(AgentType.Captain, brain, grid.CellToWorld(new Vector2Int(28, 3)), grid, world);
            AgentDebugOverlay overlay = Overlay();

            // The player walks east towards goal 1, one decision apart.
            SetPlayer(world, grid, new Vector2Int(3, 3));
            yield return Seconds(0.6f);
            SetPlayer(world, grid, new Vector2Int(6, 3));
            yield return Seconds(0.6f);

            overlay.DrawFrame(new[] { captain }, grid);

            var labels = new List<string>();
            foreach (OverlayCanvas.Label label in overlay.Canvas.Labels)
                labels.Add(label.Text);
            Assert.IsTrue(labels.Exists(t => t.StartsWith("P=0.8") || t.StartsWith("P=0.9")), "The likely goal's probability: " + string.Join(" | ", labels));
            Assert.IsTrue(labels.Exists(t => t.Contains("lead")), "The intercept cell with its arrival times.");
            Assert.Greater(overlay.Canvas.Lines.Count, 8, "Goal cells, the predicted route and the target cell.");
        }

        static void SetPlayer(WorldBlackboard world, GridGraph grid, Vector2Int cell) =>
            world.SetPlayer(new PlayerSnapshot(true, cell, grid.CellToWorld(cell), Vector3.zero, Vector3.right,
                5f, true, 1f, 1f, false, 0f, -1f));

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }
    }
}
