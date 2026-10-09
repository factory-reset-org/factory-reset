using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Debugging;

namespace ToyFactory.Tests
{
    public sealed class GuardOverlayLayerTests
    {
        // A 30 x 11 room with a wall column at x = 15. The player stands to the west, so the
        // cells just east of the wall are in its shadow and make cover.
        const int WallX = 15;
        static readonly Vector2Int PlayerCell = new Vector2Int(5, 5);
        static readonly Vector2Int GuardCell = new Vector2Int(22, 5);

        sealed class WallShadow : ICoverVisibility
        {
            public bool IsBlocked(Vector2Int cell, float height) =>
                cell.x > WallX && cell.x <= WallX + 4 && cell.y >= 3 && cell.y <= 7;
        }

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        static GridGraph Room()
        {
            var grid = new GridGraph(30, 11, Vector3.zero);
            for (int y = 3; y <= 7; y++)
                grid.SetWalkable(new Vector2Int(WallX, y), false);
            return grid;
        }

        AgentDebugOverlay Overlay()
        {
            var go = new GameObject("Overlay");
            _created.Add(go);
            AgentDebugOverlay overlay = go.AddComponent<AgentDebugOverlay>();
            overlay.Shown = true;
            return overlay;
        }

        AgentController Guard(GridGraph grid, bool playerPresent)
        {
            var go = new GameObject("Guard");
            _created.Add(go);
            go.transform.position = grid.CellToWorld(GuardCell);
            go.AddComponent<CharacterController>().center = new Vector3(0f, 1f, 0f);
            AgentController agent = go.AddComponent<AgentController>();

            var world = new WorldBlackboard();
            if (playerPresent)
                world.SetPlayer(new PlayerSnapshot(
                    isKnown: true,
                    cell: PlayerCell,
                    position: grid.CellToWorld(PlayerCell),
                    velocity: Vector3.zero,
                    forward: Vector3.right,
                    sprintSpeed: 7f,
                    isAlive: true,
                    healthFraction: 1f,
                    ammoFraction: 1f,
                    isReloading: false,
                    overchargeTimeLeft: 0f,
                    lastShotTime: -1f));

            var brain = new GuardBrain(grid, new AStarSearch(grid), world, new WallShadow(), 0);
            agent.Initialise(new AgentIdentity(AgentType.Guard, 0), brain, world, grid);
            return agent;
        }

        static List<string> Labels(AgentDebugOverlay overlay)
        {
            var labels = new List<string>();
            foreach (OverlayCanvas.Label label in overlay.Canvas.Labels)
                labels.Add(label.Text);
            return labels;
        }

        [UnityTest]
        public IEnumerator TheLayerRegistersItselfAndShowsTheGuardsCover()
        {
            GridGraph grid = Room();
            AgentController guard = Guard(grid, playerPresent: true);
            AgentDebugOverlay overlay = Overlay();
            yield return null;   // the brain picks its cover on its first tick

            overlay.DrawFrame(new[] { guard }, grid);

            bool registered = false;
            foreach (IAgentOverlayLayer layer in overlay.Layers)
                registered |= layer is GuardOverlayLayer;
            Assert.IsTrue(registered, "Registered on scene load, no edit to the overlay needed.");

            List<string> labels = Labels(overlay);
            string all = string.Join(" | ", labels);
            Assert.IsTrue(labels.Exists(t => t.Contains("battery High") && t.Contains("ideal 10 m")), all);
            Assert.IsTrue(labels.Exists(t => t.StartsWith("cover S ")), all);
            Assert.Greater(overlay.Canvas.Lines.Count, 32, "The ideal-range ring alone is 32 lines.");
        }

        [UnityTest]
        public IEnumerator WithNoPlayerOnlyTheSummaryIsShown()
        {
            GridGraph grid = Room();
            AgentController guard = Guard(grid, playerPresent: false);
            AgentDebugOverlay overlay = Overlay();
            yield return null;

            overlay.DrawFrame(new[] { guard }, grid);

            List<string> labels = Labels(overlay);
            string all = string.Join(" | ", labels);
            Assert.IsTrue(labels.Exists(t => t.StartsWith("Patrol") && t.Contains("battery High")), all);
            Assert.IsFalse(labels.Exists(t => t.StartsWith("cover") || t.StartsWith("option")), all);
        }
    }
}
