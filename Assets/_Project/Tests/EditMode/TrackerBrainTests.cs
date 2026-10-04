using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Tracker;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;

namespace ToyFactory.Tests.EditMode
{
    public class TrackerBrainTests
    {
        // 20 m x 10 m open floor (0.5 m cells). The Tracker starts at (10, 10) facing +x.
        static readonly Vector2Int Start = new Vector2Int(10, 10);
        static readonly Vector2Int PatrolA = new Vector2Int(30, 10);
        static readonly Vector2Int PatrolB = new Vector2Int(30, 2);

        GridGraph _grid;
        WorldBlackboard _world;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(40, 20, Vector3.zero);
            _world = new WorldBlackboard();
        }

        TrackerBrain Brain(params Vector2Int[] patrolCells)
        {
            if (patrolCells.Length == 0)
                patrolCells = new[] { PatrolA, PatrolB };
            var points = new List<Vector3>();
            foreach (Vector2Int cell in patrolCells)
                points.Add(_grid.CellToWorld(cell));
            return new TrackerBrain(_grid, _world, points);
        }

        AgentContext At(Vector2Int cell, float time, SensorSnapshot senses = default) =>
            new AgentContext(cell, _grid.CellToWorld(cell), Vector3.right, time, _world, senses);

        void PlacePlayer(Vector2Int cell, bool alive = true) =>
            _world.SetPlayer(new PlayerSnapshot(true, cell, _grid.CellToWorld(cell), Vector3.zero, Vector3.forward,
                5f, alive, 1f, 1f, false, 0f, -1f));

        void RemovePlayer() => _world.SetPlayer(default);

        SensorSnapshot Noise(Vector2Int cell, float level, int sourceId, float time) =>
            new SensorSnapshot(_grid.CellToWorld(cell), level, sourceId, time);

        Vector2Int EndCell(AgentIntent intent) => _grid.WorldToCell(intent.Path[intent.Path.Count - 1]);

        // ---- Patrol ---------------------------------------------------------------------

        [Test]
        public void FirstTickPatrolsWithAGbfsRoute()
        {
            TrackerBrain brain = Brain();

            AgentIntent intent = brain.Tick(At(Start, 0f));

            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(Start, _grid.WorldToCell(intent.Path[0]));
            Assert.AreEqual(PatrolA, EndCell(intent));
            Assert.AreEqual(TrackerBrain.PatrolSpeed, intent.DesiredSpeed);
            Assert.AreEqual(AgentAction.None, intent.Action);
            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.AreEqual("Calm", brain.TopStateName);
        }

        [Test]
        public void PatrolKeepsItsRouteUntilArrivalThenGoesToTheNextPoint()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));

            Assert.IsNull(brain.Tick(At(new Vector2Int(20, 10), 1f)).Path);

            AgentIntent intent = brain.Tick(At(PatrolA, 2f));
            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(PatrolB, EndCell(intent));
        }

        // ---- Hearing --------------------------------------------------------------------

        [Test]
        public void ALoudNoiseStartsAnInvestigation()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            var source = new Vector2Int(12, 18);

            AgentIntent intent = brain.Tick(At(Start, 0.1f, Noise(source, 60f, 5, 0.1f)));

            Assert.AreEqual("Investigate", intent.DebugState);
            Assert.AreEqual(TrackerBrain.InvestigateSpeed, intent.DesiredSpeed);
            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(source, EndCell(intent));
        }

        [Test]
        public void InvestigationLooksAroundThenReturnsToPatrolAndDoesNotRepeat()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            brain.Tick(At(Start, 0.1f, Noise(Start, 60f, 5, 0.1f)));   // arrived at once

            AgentIntent looking = brain.Tick(At(Start, 1f));
            Assert.AreEqual("Investigate", looking.DebugState);
            Assert.IsTrue(looking.LookTarget.HasValue);

            brain.Tick(At(Start, 0.1f + TrackerBrain.InvestigateLookTime + 0.05f));
            Assert.AreEqual("Patrol", brain.Tick(At(Start, 2.7f)).DebugState);
            Assert.AreEqual("Patrol", brain.Tick(At(Start, 2.8f)).DebugState, "A handled noise is not investigated twice.");
        }

        [Test]
        public void ARepeatingSourceDistractsUntilItGoesQuiet()
        {
            TrackerBrain brain = Brain();
            var toy = new Vector2Int(14, 16);
            brain.Tick(At(Start, 0f));
            brain.Tick(At(Start, 0.1f, Noise(toy, 50f, 9, 0.1f)));

            AgentIntent intent = brain.Tick(At(Start, 0.9f, Noise(toy, 50f, 9, 0.9f)));
            Assert.AreEqual("Distracted", intent.DebugState);
            Assert.AreEqual(TrackerBrain.DistractedSpeed, intent.DesiredSpeed);
            Assert.AreEqual(_grid.CellToWorld(toy), intent.LookTarget);
            Assert.IsNotNull(intent.Path);

            Assert.AreEqual("Distracted", brain.Tick(At(Start, 2.0f)).DebugState);
            brain.Tick(At(Start, 2.5f));   // 1.6 s of silence
            Assert.AreEqual("Patrol", brain.Tick(At(Start, 2.6f)).DebugState);
            Assert.AreEqual("Patrol", brain.Tick(At(Start, 2.7f)).DebugState, "The quiet lure is not investigated.");
        }

        // ---- Vision ---------------------------------------------------------------------

        [Test]
        public void SeeingThePlayerStartsTheChase()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            var player = new Vector2Int(25, 10);
            PlacePlayer(player);

            AgentIntent intent = brain.Tick(At(Start, 0.1f));

            Assert.AreEqual("Chase", intent.DebugState);
            Assert.AreEqual("Hunting", brain.TopStateName);
            Assert.AreEqual(TrackerBrain.ChaseSpeed, intent.DesiredSpeed);
            Assert.AreEqual(_grid.CellToWorld(player), intent.LookTarget);
            Assert.AreEqual(player, EndCell(intent));
        }

        [Test]
        public void APlayerBehindAWallIsNotSeen()
        {
            for (int y = 0; y < 20; y++)
                _grid.SetWalkable(new Vector2Int(20, y), false);
            TrackerBrain brain = Brain(new Vector2Int(15, 10), new Vector2Int(15, 2));
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));

            Assert.AreEqual("Patrol", brain.Tick(At(Start, 0.1f)).DebugState);
        }

        [Test]
        public void APlayerBehindTheTrackerIsSeenOnlyUpClose()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));

            PlacePlayer(new Vector2Int(2, 10));   // 4 m behind
            Assert.AreEqual("Patrol", brain.Tick(At(Start, 0.1f)).DebugState);

            PlacePlayer(new Vector2Int(7, 10));   // 1.5 m behind
            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.2f)).DebugState);
        }

        [Test]
        public void ChaseReplansEveryHalfSecond()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));

            Assert.IsNotNull(brain.Tick(At(Start, 1f)).Path);
            Assert.IsNull(brain.Tick(At(Start, 1.2f)).Path);
            Assert.IsNull(brain.Tick(At(Start, 1.4f)).Path);
            Assert.IsNotNull(brain.Tick(At(Start, 1.5f)).Path);
        }

        [Test]
        public void LosingSightSwitchesToSearchAfterPointSevenSeconds()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 1f));

            PlacePlayer(new Vector2Int(2, 10));   // slipped out of the cone
            Assert.AreEqual("Chase", brain.Tick(At(Start, 1.5f)).DebugState);
            AgentIntent intent = brain.Tick(At(Start, 1.8f));

            Assert.AreEqual("Search", intent.DebugState);
            Assert.AreEqual(TrackerBrain.SearchSpeed, intent.DesiredSpeed);
            Assert.IsNotNull(intent.Path);
            Assert.Greater(intent.Path.Count, 0, "Search heads for the rings round the last known position.");
        }

        [Test]
        public void SearchGivesUpAfterEightSeconds()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 1f));
            PlacePlayer(new Vector2Int(2, 10));
            brain.Tick(At(Start, 1.8f));   // Search from here

            Assert.AreEqual("Search", brain.Tick(At(Start, 9.5f)).DebugState);
            brain.Tick(At(Start, 9.9f));
            AgentIntent intent = brain.Tick(At(Start, 10f));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.AreEqual("Calm", brain.TopStateName);
        }

        [Test]
        public void SeeingThePlayerAgainDuringSearchResumesTheChase()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 1f));
            PlacePlayer(new Vector2Int(2, 10));
            brain.Tick(At(Start, 1.8f));

            PlacePlayer(new Vector2Int(20, 12));
            Assert.AreEqual("Chase", brain.Tick(At(Start, 2f)).DebugState);
        }

        [Test]
        public void AMissingOrDeadPlayerEndsTheHunt()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 0.1f));

            PlacePlayer(new Vector2Int(25, 10), alive: false);
            Assert.AreEqual("Patrol", brain.Tick(At(Start, 0.2f)).DebugState);

            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 0.3f));
            RemovePlayer();
            Assert.AreEqual("Patrol", brain.Tick(At(Start, 0.4f)).DebugState);
        }

        // ---- Wind-up energy -------------------------------------------------------------

        [Test]
        public void RunningOutOfEnergyRewindsInPlaceThenCarriesOn()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));

            AgentIntent intent = brain.Tick(At(Start, 50f));   // 2/s for 50 s
            Assert.AreEqual("Rewind", intent.DebugState);
            Assert.AreEqual(AgentAction.Rewind, intent.Action);
            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(0, intent.Path.Count, "An empty path stops the body.");
            Assert.AreEqual(0f, intent.DesiredSpeed);
            Assert.IsTrue(brain.IsRewinding);
            Assert.AreEqual(2f, brain.DamageMultiplier);

            Assert.AreEqual(AgentAction.Rewind, brain.Tick(At(Start, 51f)).Action);

            intent = brain.Tick(At(Start, 53f));
            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.AreEqual(AgentAction.None, intent.Action);
            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(1f, brain.Energy01);
            Assert.AreEqual(1f, brain.DamageMultiplier);
        }

        [Test]
        public void ChasingDrainsEnergyFiveTimesFaster()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 0f));     // starts chasing
            brain.Tick(At(Start, 2f));     // 2 s of chase at 10/s

            Assert.AreEqual(0.8f, brain.Energy01, 1e-4f);
        }

        [Test]
        public void ARewindFromTheHuntReturnsToTheHunt()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 0f));
            for (float t = 1f; t <= 10f; t += 1f)   // 10/s: empty at 10 s
                brain.Tick(At(Start, t));
            Assert.AreEqual("Rewind", brain.StateName);

            brain.Tick(At(Start, 13f));
            Assert.AreEqual("Hunting", brain.TopStateName);
        }

        // ---- Stuns ----------------------------------------------------------------------

        [Test]
        public void AStunDoesNotDrainEnergyAndAFreshRouteGoesOutOnTheFirstTickAfter()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            brain.Tick(At(Start, 1f));
            float before = brain.Energy01;

            brain.OnStunned(7f);
            AgentIntent intent = brain.Tick(At(new Vector2Int(12, 10), 8f));

            Assert.AreEqual(before, brain.Energy01, 1e-5f);
            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(new Vector2Int(12, 10), _grid.WorldToCell(intent.Path[0]));
        }

        [Test]
        public void AStunWhileHuntingResumesTheHunt()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 0.1f));

            brain.OnStunned(7f);
            PlacePlayer(new Vector2Int(2, 10));   // out of sight by the reboot
            AgentIntent intent = brain.Tick(At(Start, 7.1f));

            Assert.AreEqual("Hunting", brain.TopStateName);
            Assert.IsNotNull(intent.Path, "Heads for the last known position at once.");
        }

        [Test]
        public void AStunDuringARewindGoesBackToRewinding()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            brain.Tick(At(Start, 50f));
            Assert.IsTrue(brain.IsRewinding);

            brain.OnStunned(7f);
            AgentIntent intent = brain.Tick(At(Start, 57f));

            Assert.AreEqual("Rewind", intent.DebugState);
            Assert.AreEqual(AgentAction.Rewind, intent.Action);
        }

        [Test]
        public void TheStunInterruptHasTheHighestPriority()
        {
            string table = Brain().DescribeTransitions();

            // Within each machine the rows run from the highest priority down, and the
            // top machine's first row is the stun.
            var lastPriority = new Dictionary<string, int>();
            string firstTopRow = null;
            foreach (string line in table.Split('\n'))
            {
                string[] columns = line.Split('|');
                if (columns.Length < 4 || !int.TryParse(columns[1].Trim(), out int priority))
                    continue;
                string machine = columns[0].Trim();
                if (machine == "Top" && firstTopRow == null)
                    firstTopRow = line;
                if (lastPriority.TryGetValue(machine, out int previous))
                    Assert.LessOrEqual(priority, previous, line);
                lastPriority[machine] = priority;
            }

            StringAssert.Contains("Top | 100 | any -> Stunned", firstTopRow);
            StringAssert.Contains("Calm | 30 | any -> Distracted", table);
            StringAssert.Contains("Hunting | 20 | Chase -> Search", table);
        }

        // ---- Graph changes --------------------------------------------------------------

        [Test]
        public void AGraphChangeOffTheRouteKeepsIt()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));

            brain.OnGraphChanged(new[] { new Vector2Int(5, 2) });

            Assert.IsNull(brain.Tick(At(Start, 0.1f)).Path);
        }

        [Test]
        public void AGraphChangeOnTheRouteReplansAroundIt()
        {
            TrackerBrain brain = Brain();
            AgentIntent first = brain.Tick(At(Start, 0f));
            Vector2Int blocked = _grid.WorldToCell(first.Path[first.Path.Count / 2]);

            _grid.SetWalkable(blocked, false);
            brain.OnGraphChanged(new[] { blocked });
            AgentIntent intent = brain.Tick(At(Start, 0.1f));

            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(PatrolA, EndCell(intent));
            foreach (Vector3 point in intent.Path)
                Assert.AreNotEqual(blocked, _grid.WorldToCell(point));
        }

        [Test]
        public void ConstructorRejectsMissingInputs()
        {
            Assert.Throws<System.ArgumentNullException>(() => new TrackerBrain(null, _world, new[] { Vector3.zero }));
            Assert.Throws<System.ArgumentNullException>(() => new TrackerBrain(_grid, null, new[] { Vector3.zero }));
            Assert.Throws<System.ArgumentException>(() => new TrackerBrain(_grid, _world, new Vector3[0]));
        }
    }
}
