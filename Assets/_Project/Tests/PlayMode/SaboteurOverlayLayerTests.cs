using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Debugging;

namespace ToyFactory.Tests
{
    public sealed class SaboteurOverlayLayerTests
    {
        readonly List<Object> _created = new List<Object>();

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

        AgentController Saboteur(GridGraph grid, out SaboteurBrain brain)
        {
            var go = new GameObject("Saboteur A");
            _created.Add(go);
            go.transform.position = grid.CellToWorld(new Vector2Int(10, 10));
            go.AddComponent<CharacterController>().center = new Vector3(0f, 1f, 0f);
            AgentController agent = go.AddComponent<AgentController>();

            var world = new WorldBlackboard();
            brain = new SaboteurBrain(new SaboteurIdentity(1, SaboteurLetter.A), grid, new AStarSearch(grid), world.Claims,
                new List<Vector3> { grid.CellToWorld(new Vector2Int(30, 10)) });
            agent.Initialise(new AgentIdentity(AgentType.Saboteur, 1), brain, world, grid);
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
        public IEnumerator TheLayerRegistersItselfAndShowsTheSquadRowAndTheDecision()
        {
            var grid = new GridGraph(40, 20, Vector3.zero);
            AgentController saboteur = Saboteur(grid, out _);
            AgentDebugOverlay overlay = Overlay();
            yield return null;   // the brain makes its first decision on its first tick

            overlay.DrawFrame(new[] { saboteur }, grid);

            bool registered = false;
            foreach (IAgentOverlayLayer layer in overlay.Layers)
                registered |= layer is SaboteurOverlayLayer;
            Assert.IsTrue(registered, "Registered on scene load, no edit to the overlay needed.");

            List<string> labels = Labels(overlay);
            string all = string.Join(" | ", labels);
            Assert.IsTrue(labels.Exists(t => t.StartsWith("A  Idle  health 100%")), all);
            Assert.IsTrue(labels.Exists(t => t.Contains("> Idle 0.10")), all);
        }

        [UnityTest]
        public IEnumerator ADestroyedSaboteurShowsOnlyThatItIsDestroyed()
        {
            var grid = new GridGraph(40, 20, Vector3.zero);
            AgentController saboteur = Saboteur(grid, out SaboteurBrain brain);
            AgentDebugOverlay overlay = Overlay();
            yield return null;
            brain.OnDestroyed();

            overlay.DrawFrame(new[] { saboteur }, grid);

            List<string> labels = Labels(overlay);
            string all = string.Join(" | ", labels);
            Assert.IsTrue(labels.Exists(t => t == "A  destroyed"), all);
            Assert.IsFalse(labels.Exists(t => t.Contains("> Idle")), all);
        }

        [Test]
        public void LayerOnlyHandlesSaboteurs()
        {
            var layer = new SaboteurOverlayLayer();
            var go = new GameObject("Other");
            _created.Add(go);
            go.AddComponent<CharacterController>();
            AgentController agent = go.AddComponent<AgentController>();

            Assert.IsFalse(layer.Handles(agent));
        }
    }
}
