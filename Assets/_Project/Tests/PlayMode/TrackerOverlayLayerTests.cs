using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Agents.Tracker;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Debugging;

namespace ToyFactory.Tests
{
    public sealed class TrackerOverlayLayerTests
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

        // 20 m x 10 m open floor, or with a wall down column 20 when walled.
        static GridGraph Floor(bool walled = false)
        {
            var grid = new GridGraph(40, 20, Vector3.zero);
            if (walled)
                for (int y = 0; y < grid.Height; y++)
                    grid.SetWalkable(new Vector2Int(20, y), false);
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

        AgentController Tracker(GridGraph grid, Vector2Int cell)
        {
            var go = new GameObject("Tracker");
            _created.Add(go);
            go.transform.position = grid.CellToWorld(cell);
            go.AddComponent<CharacterController>().center = new Vector3(0f, 1f, 0f);
            AgentController agent = go.AddComponent<AgentController>();
            var world = new WorldBlackboard();
            var brain = new TrackerBrain(grid, world, new[] { grid.CellToWorld(new Vector2Int(30, 10)), grid.CellToWorld(new Vector2Int(30, 2)) });
            agent.Initialise(new AgentIdentity(AgentType.Tracker, 0), brain, world, grid);
            return agent;
        }

        // The layer instance the overlay uses (registered once per play session).
        static TrackerOverlayLayer LayerOf(AgentDebugOverlay overlay)
        {
            foreach (IAgentOverlayLayer layer in overlay.Layers)
                if (layer is TrackerOverlayLayer tracker)
                    return tracker;
            return null;
        }

        static List<string> Labels(AgentDebugOverlay overlay)
        {
            var labels = new List<string>();
            foreach (OverlayCanvas.Label label in overlay.Canvas.Labels)
                labels.Add(label.Text);
            return labels;
        }

        [UnityTest]
        public IEnumerator TheLayerRegistersItselfAndSummarisesTheTracker()
        {
            GridGraph grid = Floor();
            AgentController tracker = Tracker(grid, new Vector2Int(10, 10));
            AgentDebugOverlay overlay = Overlay();
            yield return null;   // the brain plans its first patrol route

            overlay.DrawFrame(new[] { tracker }, grid);

            Assert.IsNotNull(LayerOf(overlay), "Registered on scene load, no edit to the overlay needed.");
            List<string> labels = Labels(overlay);
            Assert.IsTrue(labels.Exists(t => t.StartsWith("Calm · spring")), string.Join(" | ", labels));
            Assert.Greater(overlay.Canvas.Lines.Count, 1, "The base route plus the GBFS route cells.");
        }

        [UnityTest]
        public IEnumerator ANoiseShowsAHeatmapThatWallsStop()
        {
            GridGraph open = Floor();
            AgentController tracker = Tracker(open, new Vector2Int(10, 10));
            AgentDebugOverlay overlay = Overlay();
            overlay.DrawFrame(new[] { tracker }, open);
            TrackerOverlayLayer layer = LayerOf(overlay);
            var source = new Vector2Int(18, 10);

            layer.Hear(new NoiseEvent(open.CellToWorld(source), NoiseLoudness.DoorSlam, 1, Time.time));
            yield return null;
            overlay.DrawFrame(new[] { tracker }, open);
            int openCells = layer.HeatmapCellsDrawn;

            Assert.Greater(openCells, 0);
            Assert.IsTrue(Labels(overlay).Exists(t => t.StartsWith("noise 60 heard 12.5 m out")));

            GridGraph walled = Floor(walled: true);
            yield return null;
            overlay.DrawFrame(new[] { tracker }, walled);   // a new grid: the noise is propagated again on it

            Assert.Greater(layer.HeatmapCellsDrawn, 0);
            Assert.Less(layer.HeatmapCellsDrawn, openCells, "Nothing is drawn past the wall.");
        }

        [UnityTest]
        public IEnumerator TheHeatmapIsDrawnOncePerFrameAndFadesAfterFourSeconds()
        {
            GridGraph grid = Floor();
            AgentController tracker = Tracker(grid, new Vector2Int(10, 10));
            AgentDebugOverlay overlay = Overlay();
            overlay.DrawFrame(new[] { tracker }, grid);
            TrackerOverlayLayer layer = LayerOf(overlay);
            layer.Hear(new NoiseEvent(grid.CellToWorld(new Vector2Int(18, 10)), NoiseLoudness.TerminalBeep, 2, Time.time));
            yield return null;

            overlay.DrawFrame(new[] { tracker }, grid);
            int lines = overlay.Canvas.Lines.Count;
            Assert.Greater(layer.HeatmapCellsDrawn, 0);

            float until = Time.time + TrackerOverlayLayer.HeatmapSeconds + 0.1f;
            while (Time.time < until)
                yield return null;
            overlay.DrawFrame(new[] { tracker }, grid);

            Assert.AreEqual(0, layer.HeatmapCellsDrawn);
            Assert.Less(overlay.Canvas.Lines.Count, lines);
        }
    }
}
