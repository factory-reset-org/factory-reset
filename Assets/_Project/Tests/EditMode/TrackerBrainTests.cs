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

        // A tick of the thrown wind-up toy: marked as a lure.
        SensorSnapshot ToyTick(Vector2Int cell, float level, int sourceId, float time) =>
            new SensorSnapshot(_grid.CellToWorld(cell), level, sourceId, time, noiseIsLure: true);

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

        [Test]
        public void ARepeatingSourceBeatsALouderOneOffShot()
        {
            TrackerBrain brain = Brain();
            var toy = new Vector2Int(14, 16);
            brain.Tick(At(Start, 0f));
            Assert.AreEqual("Investigate", brain.Tick(At(Start, 0.1f, Noise(new Vector2Int(20, 4), 90f, -1, 0.1f))).DebugState);
            brain.Tick(At(Start, 0.2f, Noise(toy, 40f, 9, 0.2f)));

            // The shot still scores higher (90 * e^-0.21 = 73 against 40), but the toy is the lure.
            AgentIntent intent = brain.Tick(At(Start, 0.8f, Noise(toy, 40f, 9, 0.8f)));

            Assert.AreEqual("Distracted", intent.DebugState);
            Assert.AreEqual(_grid.CellToWorld(toy), intent.LookTarget);
        }

        [Test]
        public void AToyPullsTheTrackerOffAChaseUntilItGoesQuiet()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));   // 7.5 m ahead
            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.1f)).DebugState);

            var toy = new Vector2Int(10, 16);
            brain.Tick(At(Start, 0.2f, Noise(toy, 50f, 9, 0.2f)));
            AgentIntent intent = brain.Tick(At(Start, 0.8f, Noise(toy, 50f, 9, 0.8f)));

            Assert.AreEqual("Distracted", intent.DebugState, "The ticking toy beats the chase.");
            Assert.AreEqual("Calm", brain.TopStateName);
            Assert.AreEqual(_grid.CellToWorld(toy), intent.LookTarget);
            Assert.AreEqual("Distracted", brain.Tick(At(Start, 1.4f, Noise(toy, 50f, 9, 1.4f))).DebugState,
                "The player 7.5 m away goes unseen while the toy ticks.");

            brain.Tick(At(Start, 3.0f));   // 1.6 s of silence
            Assert.AreEqual("Chase", brain.Tick(At(Start, 3.1f)).DebugState, "The player in view is seen again.");
        }

        [Test]
        public void ADistractedTrackerStopsShortOfTheToyFacingItAndFollowsOnlyOnceItWalksOff()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            brain.Tick(At(Start, 0.2f, Noise(new Vector2Int(16, 10), 50f, 9, 0.2f)));
            AgentIntent intent = brain.Tick(At(Start, 0.8f, Noise(new Vector2Int(16, 10), 50f, 9, 0.8f)));
            Assert.AreEqual("Distracted", intent.DebugState);
            Assert.AreEqual(new Vector2Int(16, 10), EndCell(intent), "It walks straight towards the toy.");

            // 1 m from the toy: it stops and watches it.
            var near = new Vector2Int(14, 10);
            intent = brain.Tick(At(near, 1.1f));
            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(0, intent.Path.Count, "Stops.");
            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(16, 10)), intent.LookTarget);

            // The toy walks on to 1.5 m: still watching, no new route.
            intent = brain.Tick(At(near, 1.4f, Noise(new Vector2Int(17, 10), 50f, 9, 1.4f)));
            Assert.IsNull(intent.Path);
            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(17, 10)), intent.LookTarget);

            // At 2.5 m it follows again.
            intent = brain.Tick(At(near, 2.0f, Noise(new Vector2Int(19, 10), 50f, 9, 2.0f)));
            Assert.AreEqual("Distracted", intent.DebugState);
            Assert.AreEqual(new Vector2Int(19, 10), EndCell(intent));
        }

        [Test]
        public void ADistractedTrackerNoticesThePlayerOnlyWithinTwoMetres()
        {
            TrackerBrain brain = Brain();
            var toy = new Vector2Int(10, 16);
            brain.Tick(At(Start, 0f));
            brain.Tick(At(Start, 0.2f, Noise(toy, 50f, 9, 0.2f)));
            Assert.AreEqual("Distracted", brain.Tick(At(Start, 0.8f, Noise(toy, 50f, 9, 0.8f))).DebugState);

            PlacePlayer(new Vector2Int(15, 10));   // 2.5 m ahead, in view
            Assert.AreEqual("Distracted", brain.Tick(At(Start, 1.0f)).DebugState,
                "Inside the 3 m a hunt would keep, but outside the 2 m that ends a distraction: no flip.");

            PlacePlayer(new Vector2Int(13, 10));   // 1.5 m
            Assert.AreEqual("Chase", brain.Tick(At(Start, 1.2f)).DebugState);
        }

        [Test]
        public void APlayerWithinThreeMetresIsChasedEvenWithAToyTicking()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(14, 10));   // 2 m ahead
            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.1f)).DebugState);

            var toy = new Vector2Int(10, 16);
            brain.Tick(At(Start, 0.2f, Noise(toy, 50f, 9, 0.2f)));
            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.8f, Noise(toy, 50f, 9, 0.8f))).DebugState);
            Assert.AreEqual("Hunting", brain.TopStateName);
        }

        [Test]
        public void AThrownToyDistractsOnItsFirstTick()
        {
            TrackerBrain brain = Brain();
            var toy = new Vector2Int(14, 16);
            brain.Tick(At(Start, 0f));

            AgentIntent intent = brain.Tick(At(Start, 0.2f, ToyTick(toy, 50f, 9, 0.2f)));

            Assert.AreEqual("Distracted", intent.DebugState, "No Investigate first, no wait for a second tick.");
            Assert.AreEqual(_grid.CellToWorld(toy), intent.LookTarget);
            Assert.IsNotNull(intent.Path);
        }

        [Test]
        public void AThrownToysFirstTickPullsTheTrackerOffAChase()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));   // 7.5 m ahead
            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.1f)).DebugState);

            AgentIntent intent = brain.Tick(At(Start, 0.2f, ToyTick(new Vector2Int(10, 16), 50f, 9, 0.2f)));

            Assert.AreEqual("Distracted", intent.DebugState);
            Assert.AreEqual("Calm", brain.TopStateName);
        }

        [Test]
        public void AThrownToyPullsAnAttackingTrackerAwayForThreeSeconds()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(12, 10));   // 1 m ahead: in reach
            AgentIntent chasing = brain.Tick(At(Start, 0.1f));
            Assert.AreEqual("Chase", chasing.DebugState);
            Assert.IsFalse(chasing.IgnorePlayer, "Chasing: the body stands off and pounces.");

            var toy = new Vector2Int(10, 16);
            AgentIntent intent = brain.Tick(At(Start, 0.2f, ToyTick(toy, 50f, 9, 0.2f)));
            Assert.AreEqual("Distracted", intent.DebugState, "The toy lock beats even a player 1 m away.");
            Assert.AreEqual(AgentAction.None, intent.Action, "No attack while locked onto the toy.");
            Assert.IsTrue(intent.IgnorePlayer, "The body walks off to the toy instead of holding and pouncing on the player.");
            Assert.AreEqual(_grid.CellToWorld(toy), intent.LookTarget);

            Assert.AreEqual("Distracted", brain.Tick(At(Start, 1.4f, ToyTick(toy, 50f, 9, 1.4f))).DebugState);
            Assert.AreEqual("Distracted", brain.Tick(At(Start, 2.6f, ToyTick(toy, 50f, 9, 2.6f))).DebugState,
                "Still locked 2.4 s after the first tick.");
            Assert.AreEqual("Chase", brain.Tick(At(Start, 3.3f, ToyTick(toy, 50f, 9, 3.2f))).DebugState,
                "After 3 s the usual rule is back: a player within 2 m is noticed.");
        }

        [Test]
        public void ATerminalBeepDoesNotLockLikeAThrownToy()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(12, 10));   // 1 m ahead
            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.1f)).DebugState);

            var terminal = new Vector2Int(10, 16);
            brain.Tick(At(Start, 0.2f, Noise(terminal, 50f, 9, 0.2f)));
            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.8f, Noise(terminal, 50f, 9, 0.8f))).DebugState,
                "A repeating noise that is not a thrown toy keeps the 3 m rule.");
        }

        // ---- Spawn grace ------------------------------------------------------------------

        TrackerBrain GraceBrain()
        {
            var points = new List<Vector3> { _grid.CellToWorld(PatrolA), _grid.CellToWorld(PatrolB) };
            return new TrackerBrain(_grid, _world, points, spawnGrace: true);
        }

        [Test]
        public void WithSpawnGraceAPlayerWhoHasNotMovedIsNotSeen()
        {
            TrackerBrain brain = GraceBrain();
            PlacePlayer(new Vector2Int(25, 10));   // 7.5 m ahead, in view, where it spawned
            brain.Tick(At(Start, 0f));
            Assert.AreEqual("Patrol", brain.Tick(At(Start, 0.5f)).DebugState, "Standing still where it spawned.");

            PlacePlayer(new Vector2Int(25, 12));   // 1 m from the spawn
            Assert.AreEqual("Patrol", brain.Tick(At(Start, 1.0f)).DebugState, "Within 1.5 m of the spawn.");

            PlacePlayer(new Vector2Int(25, 14));   // 2 m from the spawn
            Assert.AreEqual("Chase", brain.Tick(At(Start, 1.5f)).DebugState, "Moved off: seen.");
        }

        [Test]
        public void SpawnGraceEndsWhenTheTrackerIsRightNextToThePlayer()
        {
            TrackerBrain brain = GraceBrain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(14, 10));   // 2 m ahead: inside the 2.6 m all-round range

            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.1f)).DebugState);
        }

        [Test]
        public void SpawnGraceStartsAgainAfterARespawn()
        {
            TrackerBrain brain = GraceBrain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 0.1f));
            PlacePlayer(new Vector2Int(25, 14));
            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.2f)).DebugState);

            PlacePlayer(new Vector2Int(25, 14), alive: false);
            brain.Tick(At(Start, 0.3f));
            PlacePlayer(new Vector2Int(28, 6));    // respawned, in view
            brain.Tick(At(Start, 0.4f));

            Assert.AreEqual("Calm", brain.TopStateName, "A respawned player standing still is not seen.");
        }

        [Test]
        public void WithoutSpawnGraceAPlayerInViewIsSeenAtOnce()
        {
            TrackerBrain brain = Brain();
            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 0f));

            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.1f)).DebugState, "Tests and tools build it without the grace.");
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

        // A 1.5 m wide prop across the line of sight, on a grid with a sight layer.
        void PropAcrossTheView(bool tall)
        {
            for (int y = 8; y <= 12; y++)
            {
                _grid.SetWalkable(new Vector2Int(18, y), false);
                _grid.SetBlocksSight(new Vector2Int(18, y), tall);
            }
        }

        [Test]
        public void ThePlayerIsSeenOverALowProp()
        {
            PropAcrossTheView(tall: false);   // a crate or a belt: walking must go round it
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));

            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.1f)).DebugState);
        }

        [Test]
        public void ThePlayerIsNotSeenThroughATallProp()
        {
            PropAcrossTheView(tall: true);    // a press or a shelf
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));

            Assert.AreEqual("Patrol", brain.Tick(At(Start, 0.1f)).DebugState);
        }

        [Test]
        public void TheTrackersOwnCellNeverBlocksItsSight()
        {
            // Brushing a wall, the body can stand in a cell the wall overlaps.
            _grid.SetBlocksSight(Start, true);
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));

            Assert.AreEqual("Chase", brain.Tick(At(Start, 0.1f)).DebugState);
        }

        [TestCase(39, 10, "Chase", TestName = "TheTrackerSeesFourteenAndAHalfMetresAhead")]
        [TestCase(13, 18, "Chase", TestName = "TheTrackerSeesSeventyDegreesOffItsHeading")]
        [TestCase(11, 18, "Patrol", TestName = "TheTrackerDoesNotSeeEightyThreeDegreesOffItsHeading")]
        public void TheVisionConeIsSixteenMetresAndSeventyTwoDegreesEachSide(int x, int y, string expected)
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(x, y));

            Assert.AreEqual(expected, brain.Tick(At(Start, 0.1f)).DebugState);
        }

        [Test]
        public void ATrailOfFootstepsIsFollowedNotWatchedLikeAToy()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            int player = NoiseMemory.PlayerSourceId;

            brain.Tick(At(Start, 0.1f, Noise(new Vector2Int(10, 16), 20f, player, 0.1f)));
            brain.Tick(At(Start, 0.55f, Noise(new Vector2Int(11, 16), 20f, player, 0.55f)));
            AgentIntent intent = brain.Tick(At(Start, 1.0f, Noise(new Vector2Int(12, 16), 20f, player, 1.0f)));

            Assert.AreEqual("Investigate", intent.DebugState, "Steps 0.45 s apart are not a lure.");
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

        [Test]
        public void ARewindFromTheHuntWithAToyTickingGoesStraightToTheToy()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            PlacePlayer(new Vector2Int(25, 10));
            brain.Tick(At(Start, 0f));
            for (float t = 1f; t <= 10f; t += 1f)
                brain.Tick(At(Start, t));
            Assert.AreEqual("Rewind", brain.StateName);

            var toy = new Vector2Int(10, 16);
            brain.Tick(At(Start, 12.4f, Noise(toy, 50f, 9, 12.4f)));
            AgentIntent intent = brain.Tick(At(Start, 13f, Noise(toy, 50f, 9, 13f)));

            Assert.AreEqual("Distracted", intent.DebugState, "No tick of Search between the rewind and the toy.");
            Assert.AreEqual("Calm", brain.TopStateName);
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
            StringAssert.Contains("Hunting | 25 | Chase -> WaitAtDoor", table);
            StringAssert.Contains("Hunting | 22 | WaitAtDoor -> Search", table);
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

        // ---- Closed doors ---------------------------------------------------------------

        // A wall along column 20 with a three-cell door (rows 9-11), like a doorway in the level.
        static readonly Vector2Int DoorMiddle = new Vector2Int(20, 10);
        static readonly Vector2Int BeyondTheDoor = new Vector2Int(25, 10);

        void DoorWall(bool closed)
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                var cell = new Vector2Int(20, y);
                if (y >= 9 && y <= 11)
                    _grid.SetDoor(cell, 1, closed);
                else
                    _grid.SetWalkable(cell, false);
            }
        }

        void SetDoorClosed(bool closed)
        {
            for (int y = 9; y <= 11; y++)
                _grid.SetDoor(new Vector2Int(20, y), 1, closed);
        }

        // Chases the player through the open door, then the door shuts between them.
        TrackerBrain ChaseUntilTheDoorShuts()
        {
            DoorWall(closed: false);
            TrackerBrain brain = Brain(new Vector2Int(10, 2), new Vector2Int(14, 2));
            brain.Tick(At(Start, 0f));
            PlacePlayer(BeyondTheDoor);
            Assert.AreEqual("Chase", brain.Tick(At(Start, 1f)).DebugState);

            SetDoorClosed(true);
            return brain;
        }

        [Test]
        public void AClosedDoorSendsTheChaseToTheNearSideOfTheDoor()
        {
            TrackerBrain brain = ChaseUntilTheDoorShuts();

            AgentIntent intent = brain.Tick(At(Start, 1.5f));   // the next 0.5 s repath

            Assert.IsTrue(brain.IsBlockedByDoor);
            Assert.IsNotNull(intent.Path);
            Vector2Int end = EndCell(intent);
            Assert.AreEqual(19, end.x, "Stops on the cell before the door.");
            Assert.AreEqual(DoorMiddle.y, end.y, 1);
            Assert.AreEqual("WaitAtDoor", brain.Tick(At(Start, 1.6f)).DebugState, "The door beats losing sight (priority 25 > 20).");
        }

        [Test]
        public void ItWaitsAtTheShutDoorThenSearchesItsOwnSide()
        {
            TrackerBrain brain = ChaseUntilTheDoorShuts();
            AgentIntent planned = brain.Tick(At(Start, 1.5f));
            Vector2Int approach = EndCell(planned);
            brain.Tick(At(Start, 1.6f));

            AgentIntent waiting = brain.Tick(At(approach, 3f));   // arrived
            Assert.AreEqual("WaitAtDoor", waiting.DebugState);
            Assert.AreEqual(AlertLevel.Alert, waiting.Alert);
            Assert.AreEqual(0, waiting.Path.Count, "Stands at the door.");
            Assert.AreEqual(DoorMiddle.x, _grid.WorldToCell(waiting.LookTarget.Value).x, "Stares at the door.");

            Assert.AreEqual("WaitAtDoor", brain.Tick(At(approach, 5.4f)).DebugState);
            brain.Tick(At(approach, 5.5f));   // 2.5 s at the door
            AgentIntent search = brain.Tick(At(approach, 5.6f));

            Assert.AreEqual("Search", search.DebugState);
            Assert.AreEqual(AlertLevel.Suspicious, search.Alert);
            Assert.IsNotNull(search.Path);
            Assert.Less(EndCell(search).x, 20, "Searches the side it can reach.");
        }

        [Test]
        public void TheDoorOpeningResumesTheChase()
        {
            TrackerBrain brain = ChaseUntilTheDoorShuts();
            AgentIntent planned = brain.Tick(At(Start, 1.5f));
            Vector2Int approach = EndCell(planned);
            brain.Tick(At(Start, 1.6f));
            brain.Tick(At(approach, 3f));
            PlacePlayer(new Vector2Int(22, 2));   // beyond the door, out of the cone and behind the wall

            SetDoorClosed(false);
            AgentIntent intent = brain.Tick(At(approach, 3.2f));

            Assert.AreEqual("Chase", intent.DebugState);
            Assert.IsFalse(brain.IsBlockedByDoor);
            Assert.AreEqual(BeyondTheDoor, EndCell(intent), "Runs on through the door to where it last saw the player.");
        }

        [Test]
        public void ANoiseThroughAClosedDoorIsCheckedFromTheDoor()
        {
            DoorWall(closed: true);
            TrackerBrain brain = Brain(new Vector2Int(10, 2), new Vector2Int(14, 2));
            brain.Tick(At(Start, 0f));

            AgentIntent intent = brain.Tick(At(Start, 0.1f, Noise(BeyondTheDoor, 60f, 7, 0.1f)));

            Assert.AreEqual("Investigate", intent.DebugState);
            Assert.AreEqual(AlertLevel.Suspicious, intent.Alert);
            Assert.IsTrue(brain.IsBlockedByDoor);
            Assert.AreEqual(19, EndCell(intent).x);

            Vector2Int approach = EndCell(intent);
            brain.Tick(At(approach, 2f));   // arrived: looks around at the door
            brain.Tick(At(approach, 2f + TrackerBrain.InvestigateLookTime + 0.05f));
            Assert.AreEqual("Patrol", brain.Tick(At(approach, 4.6f)).DebugState);
        }

        [Test]
        public void ANoiseBehindAWallWithNoDoorIsLookedForFromWhereItStands()
        {
            for (int y = 0; y < _grid.Height; y++)
                _grid.SetWalkable(new Vector2Int(20, y), false);
            TrackerBrain brain = Brain(new Vector2Int(10, 2), new Vector2Int(14, 2));
            brain.Tick(At(Start, 0f));

            AgentIntent intent = brain.Tick(At(Start, 0.1f, Noise(BeyondTheDoor, 60f, 7, 0.1f)));

            Assert.AreEqual("Investigate", intent.DebugState);
            Assert.IsFalse(brain.IsBlockedByDoor, "A wall is not a door: there is nothing to wait at.");
            Assert.AreEqual(0, intent.Path.Count, "Unreachable: it stops and looks from here.");
        }

        [Test]
        public void ABoxPushedOntoTheLastSightingIsSearchedRoundNotWalkedInto()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            var player = new Vector2Int(25, 10);
            PlacePlayer(player);
            brain.Tick(At(Start, 1f));   // Chase towards the player
            PlacePlayer(new Vector2Int(2, 10));   // slips out of sight...

            // ...and shoves a box onto the spot it was seen at.
            for (int x = 24; x <= 26; x++)
                for (int y = 9; y <= 11; y++)
                    _grid.AddBlocker(new Vector2Int(x, y));
            brain.OnGraphChanged(new[] { player });
            AgentIntent chase = brain.Tick(At(Start, 1.5f));

            Assert.AreEqual("Chase", chase.DebugState);
            Vector2Int end = EndCell(chase);
            Assert.IsTrue(_grid.IsTraversable(end), "The goal is snapped to a free cell beside the box.");
            Assert.LessOrEqual(Mathf.Max(Mathf.Abs(end.x - player.x), Mathf.Abs(end.y - player.y)), TrackerBrain.NearestCellRadius);

            AgentIntent search = brain.Tick(At(Start, 1.8f));
            Assert.AreEqual("Search", search.DebugState);
            foreach (Vector3 point in search.Path)
                Assert.IsTrue(_grid.IsTraversable(_grid.WorldToCell(point)), "Search never routes through the box.");
        }

        // ---- Alert level ----------------------------------------------------------------

        [Test]
        public void TheAlertLevelFollowsTheState()
        {
            TrackerBrain brain = Brain();
            Assert.AreEqual(AlertLevel.None, brain.Tick(At(Start, 0f)).Alert, "Patrol");

            Assert.AreEqual(AlertLevel.Suspicious, brain.Tick(At(Start, 0.1f, Noise(new Vector2Int(12, 18), 60f, 5, 0.1f))).Alert, "Investigate");

            PlacePlayer(new Vector2Int(25, 10));
            Assert.AreEqual(AlertLevel.Alert, brain.Tick(At(Start, 0.2f)).Alert, "Chase");

            PlacePlayer(new Vector2Int(2, 10));
            brain.Tick(At(Start, 0.5f));
            Assert.AreEqual(AlertLevel.Suspicious, brain.Tick(At(Start, 1f)).Alert, "Search");
            Assert.AreEqual(AlertLevel.Suspicious, brain.Alert);
        }

        // ---- Read-only state for the debug overlay --------------------------------------

        [Test]
        public void TheOverlayCanReadTheRouteNoisesLastSightingAndSearchCentre()
        {
            TrackerBrain brain = Brain();
            brain.Tick(At(Start, 0f));
            Assert.AreEqual(PatrolA, brain.RouteCells[brain.RouteCells.Count - 1], "The GBFS route cells.");
            Assert.IsFalse(brain.HasLastKnownPosition);

            brain.Tick(At(Start, 0.1f, Noise(new Vector2Int(12, 18), 60f, 5, 0.1f)));
            var noises = new List<RememberedNoise>();
            brain.GetRememberedNoises(noises);
            Assert.AreEqual(1, noises.Count);
            Assert.AreEqual(5, noises[0].SourceId);

            var player = new Vector2Int(25, 10);
            PlacePlayer(player);
            brain.Tick(At(Start, 0.2f));
            Assert.IsTrue(brain.HasLastKnownPosition);
            Assert.AreEqual(_grid.CellToWorld(player), brain.LastKnownPosition);
            Assert.IsFalse(brain.IsSearching);

            PlacePlayer(new Vector2Int(2, 10));   // out of the cone
            brain.Tick(At(Start, 0.5f));
            brain.Tick(At(Start, 1f));
            Assert.IsTrue(brain.IsSearching);
            Assert.AreEqual(_grid.CellToWorld(player), brain.SearchCentre, "Search rings round where it last saw the player.");
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
