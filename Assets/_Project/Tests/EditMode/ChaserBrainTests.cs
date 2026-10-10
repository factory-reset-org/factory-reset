using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.AI.Agents.Mock;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The demo's chaser: straight for the player's cell at the Captain's speed, re-planned
    /// every 0.5 s, no shooting, and it stops when there is no player.
    /// </summary>
    public class ChaserBrainTests
    {
        GridGraph _grid;
        WorldBlackboard _world;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(40, 10, Vector3.zero);
            _world = new WorldBlackboard();
        }

        void PlacePlayer(Vector2Int cell, bool alive = true) =>
            _world.SetPlayer(new PlayerSnapshot(true, cell, _grid.CellToWorld(cell), Vector3.zero, Vector3.right,
                7f, alive, 1f, 1f, false, 0f, -1f));

        AgentIntent Tick(ChaserBrain brain, Vector2Int cell, float time) =>
            brain.Tick(new AgentContext(cell, _grid.CellToWorld(cell), Vector3.forward, time, _world, default(SensorSnapshot)));

        [Test]
        public void RunsForThePlayersCellAtTheCaptainsSpeed()
        {
            var brain = new ChaserBrain(_grid);
            PlacePlayer(new Vector2Int(30, 5));

            AgentIntent intent = Tick(brain, new Vector2Int(2, 5), 0f);

            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(30, 5)), intent.Path[intent.Path.Count - 1]);
            Assert.AreEqual(CaptainBrain.InterceptSpeed, intent.DesiredSpeed);
            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(30, 5)), intent.LookTarget);
            Assert.AreEqual(AgentAction.None, intent.Action, "It never shoots.");
            Assert.AreEqual(ChaserBrain.StateName, intent.DebugState);
        }

        [Test]
        public void ReplansEveryHalfSecondToWhereThePlayerIsNow()
        {
            var brain = new ChaserBrain(_grid);
            PlacePlayer(new Vector2Int(30, 5));
            Tick(brain, new Vector2Int(2, 5), 0f);

            PlacePlayer(new Vector2Int(30, 8));
            Assert.IsNull(Tick(brain, new Vector2Int(4, 5), 0.2f).Path, "Keeps its route between plans.");

            AgentIntent next = Tick(brain, new Vector2Int(6, 5), 0.5f);
            Assert.IsNotNull(next.Path);
            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(30, 8)), next.Path[next.Path.Count - 1], "To where the player is now, not where they are going.");
        }

        [Test]
        public void WithNoPlayerItStopsOnce()
        {
            var brain = new ChaserBrain(_grid);
            PlacePlayer(new Vector2Int(30, 5));
            Tick(brain, new Vector2Int(2, 5), 0f);

            PlacePlayer(new Vector2Int(30, 5), alive: false);
            AgentIntent stop = Tick(brain, new Vector2Int(4, 5), 0.5f);
            Assert.IsNotNull(stop.Path);
            Assert.AreEqual(0, stop.Path.Count, "Stops.");
            Assert.IsNull(Tick(brain, new Vector2Int(4, 5), 1f).Path, "Once.");
            Assert.AreEqual(AlertLevel.None, Tick(brain, new Vector2Int(4, 5), 1.5f).Alert);
        }
    }
}
