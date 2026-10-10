using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class CaptainBrainTests
    {
        // Three rooms in a row joined by one-cell doorways (chokepoints) at (10,3) and (20,3).
        // Goal 1 at the east end, goal 2 at the west end. The player starts in the west room.
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

        static readonly Vector2Int DoorB = new Vector2Int(20, 3);
        static readonly Vector2Int EastGoal = new Vector2Int(27, 3);
        static readonly Vector2Int WestGoal = new Vector2Int(2, 3);
        const int EastId = 1;
        const int WestId = 2;

        GridGraph _grid;
        WorldBlackboard _world;

        [SetUp]
        public void SetUp()
        {
            _grid = ThreeRooms();
            _world = new WorldBlackboard();
            _world.SetObjectiveTargets(new[]
            {
                new ObjectiveTarget(EastId, EastGoal, ObjectiveTargetKind.Task),
                new ObjectiveTarget(WestId, WestGoal, ObjectiveTargetKind.Task),
            });
        }

        CaptainBrain Captain(bool startAwake = true) =>
            new CaptainBrain(_grid, new AStarSearch(_grid), _world, startAwake);

        // The Captain faces east (away from the player) unless told otherwise, so it only
        // sees the player when a test means it to.
        AgentIntent Tick(CaptainBrain brain, Vector2Int captainCell, float time, Vector3? forward = null) =>
            brain.Tick(new AgentContext(captainCell, _grid.CellToWorld(captainCell), forward ?? Vector3.right,
                time, _world, default(SensorSnapshot)));

        void PlacePlayer(Vector2Int cell, bool alive = true, float sprint = 5f) =>
            _world.SetPlayer(new PlayerSnapshot(true, cell, _grid.CellToWorld(cell), Vector3.zero, Vector3.right,
                sprint, alive, 1f, 1f, false, 0f, -1f));

        // The player walks east along the corridor row, 3 cells (1.5 m) per 0.5 s decision.
        // The Captain waits two cells east of the second doorway, so it can beat the player
        // there by more than 1 s.
        CaptainBrain CommittedToTheSecondDoorway(out AgentIntent intent)
        {
            CaptainBrain brain = Captain();
            Vector2Int captain = new Vector2Int(22, 3);
            PlacePlayer(new Vector2Int(3, 3));
            Tick(brain, captain, 0f);
            PlacePlayer(new Vector2Int(6, 3));
            intent = Tick(brain, captain, 0.5f);
            return brain;
        }

        [Test]
        public void TransitionTableIsDataInPriorityOrder()
        {
            string table = Captain().DescribeTransitions();
            string[] lines = table.Split('\n');
            StringAssert.StartsWith("110 | Dormant -> Observe", lines[1]);
            StringAssert.Contains("80 | Ambush -> Engage", table);
            StringAssert.Contains("70 | Engage -> Pursue", table);
            StringAssert.Contains("20 | Observe -> Intercept", table);
        }

        [Test]
        public void DormantIgnoresThePlayerUntilTheWakeSignal()
        {
            CaptainBrain brain = Captain(startAwake: false);
            PlacePlayer(new Vector2Int(23, 3));   // right next to it

            AgentIntent first = Tick(brain, new Vector2Int(22, 3), 0f, Vector3.right);
            Tick(brain, new Vector2Int(22, 3), 1f, Vector3.right);
            Assert.AreEqual("Dormant", brain.StateName);
            Assert.IsNotNull(first.Path);
            Assert.AreEqual(0, first.Path.Count, "Stands still.");
            Assert.IsFalse(brain.Prediction.IsKnown);
            Assert.AreEqual(AgentAction.None, first.Action);

            _world.SetCaptainAwake();
            Tick(brain, new Vector2Int(22, 3), 1.5f);
            Assert.AreNotEqual("Dormant", brain.StateName);
            Assert.IsTrue(brain.IsAwake);
        }

        [Test]
        public void ChapterThreeWakesTheCaptainEvenIfTheSignalWasMissed()
        {
            CaptainBrain brain = Captain(startAwake: false);
            _world.SetChapterIndex(2);
            Tick(brain, new Vector2Int(22, 3), 0f);
            Assert.AreEqual("Dormant", brain.StateName);

            _world.SetChapterIndex(CaptainBrain.WakeChapter);
            Tick(brain, new Vector2Int(22, 3), 0.5f);
            Assert.AreEqual("Observe", brain.StateName);
        }

        [Test]
        public void NoPlayerMeansNoPredictionAndTheCaptainWaits()
        {
            CaptainBrain brain = Captain();
            AgentIntent intent = Tick(brain, new Vector2Int(22, 3), 0f);
            Tick(brain, new Vector2Int(22, 3), 0.5f);

            Assert.AreEqual("Observe", brain.StateName);
            Assert.IsFalse(brain.Prediction.IsKnown);
            Assert.AreEqual(0, intent.Path.Count);

            PlacePlayer(new Vector2Int(6, 3), alive: false);
            Tick(brain, new Vector2Int(22, 3), 1f);
            Assert.IsFalse(brain.Prediction.IsKnown, "A dead player is not predicted.");
        }

        [Test]
        public void WalkingTowardsAGoalCommitsToTheFirstChokepointItCanBeat()
        {
            CaptainBrain brain = CommittedToTheSecondDoorway(out AgentIntent intent);

            Assert.AreEqual("Intercept", brain.StateName);
            Assert.IsTrue(brain.Prediction.IsKnown);
            Assert.AreEqual(EastId, brain.Prediction.GoalId);
            Assert.GreaterOrEqual(brain.Prediction.Confidence, CaptainBrain.ConfidenceThreshold);

            InterceptPlan plan = brain.Plan;
            Assert.AreEqual(InterceptKind.Chokepoint, plan.Kind);
            Assert.AreEqual(DoorB, plan.Cell, "The first doorway is too close to the player to beat by 1 s.");
            Assert.LessOrEqual(plan.CaptainArrival + InterceptPlanner.DefaultMarginSeconds, plan.PlayerArrival + 1e-4f);

            Assert.IsNotNull(intent.Path);
            Assert.Greater(intent.Path.Count, 0);
            Assert.AreEqual(_grid.CellToWorld(DoorB), intent.Path[intent.Path.Count - 1], "Walks with A* to the doorway.");
            Assert.AreEqual(CaptainBrain.InterceptSpeed, intent.DesiredSpeed);
        }

        [Test]
        public void AlertLevelFollowsHowCommittedTheCaptainIs()
        {
            CaptainBrain brain = Captain(startAwake: false);
            PlacePlayer(new Vector2Int(3, 3));
            Assert.AreEqual(AlertLevel.None, Tick(brain, new Vector2Int(22, 3), 0f).Alert, "Dormant: no icon.");

            brain = Captain();
            AgentIntent watching = Tick(brain, new Vector2Int(22, 3), 0f);
            Assert.AreEqual("Observe", brain.StateName);
            Assert.AreEqual(AlertLevel.Suspicious, watching.Alert, "Watching and predicting: \"?\".");

            CaptainBrain committed = CommittedToTheSecondDoorway(out AgentIntent intercept);
            Assert.AreEqual("Intercept", committed.StateName);
            Assert.AreEqual(AlertLevel.Alert, intercept.Alert, "Committed to cutting the player off: \"!\".");
        }

        [Test]
        public void ReachingTheCellTurnsToAmbushFacingTheWayThePlayerComes()
        {
            CaptainBrain brain = CommittedToTheSecondDoorway(out _);

            AgentIntent intent = Tick(brain, DoorB, 0.6f, Vector3.right);

            Assert.AreEqual("Ambush", brain.StateName);
            Assert.AreEqual(0f, intent.DesiredSpeed);
            Assert.IsTrue(intent.LookTarget.HasValue);
            Assert.Less(intent.LookTarget.Value.x, _grid.CellToWorld(DoorB).x, "Faces west, towards the player.");
        }

        [Test]
        public void StoppedShortOnThePlayersSideItStillFacesThePlayersWay()
        {
            // The body stops up to 0.6 m off its cell. Coming from the player's side it stands
            // past the next route cell, so facing that cell turned it round (seen guarding the
            // console in Chapter 4: facing the wall, back to the Storage door).
            CaptainBrain brain = CommittedToTheSecondDoorway(out _);
            Vector3 standing = _grid.CellToWorld(DoorB) + new Vector3(-0.55f, 0f, 0f);

            AgentIntent intent = brain.Tick(new AgentContext(DoorB, standing, Vector3.right, 0.6f, _world, default(SensorSnapshot)));

            Assert.AreEqual("Ambush", brain.StateName);
            Assert.Less(intent.LookTarget.Value.x, standing.x - 1f, "Faces well west, the way the player comes.");
        }

        [Test]
        public void TheCommitmentIsOfferedForTheCallouts()
        {
            CaptainBrain watching = Captain();
            Tick(watching, new Vector2Int(22, 3), 0f);
            Assert.IsFalse(watching.TryGetCommitment(out _, out _, out _), "Watching: no commitment.");

            CaptainBrain brain = CommittedToTheSecondDoorway(out _);
            Assert.IsTrue(brain.TryGetCommitment(out CandidateGoal goal, out Vector3 position, out bool guarding));
            Assert.AreEqual(EastId, goal.Id);
            Assert.AreEqual(_grid.CellToWorld(EastGoal), position, "The goal itself, not the intercept cell.");
            Assert.IsFalse(guarding);
        }

        [Test]
        public void AmbushHoldsWhileTheCellIsStillAheadOfThePlayer()
        {
            CaptainBrain brain = CommittedToTheSecondDoorway(out _);
            Tick(brain, DoorB, 0.6f, Vector3.right);

            PlacePlayer(new Vector2Int(8, 3));   // still west of the first doorway, out of range
            Tick(brain, DoorB, 1.0f, Vector3.right);

            Assert.AreEqual("Ambush", brain.StateName);
        }

        [Test]
        public void TaskCompletedMidInterceptDropsThePlanAndRepredicts()
        {
            CaptainBrain brain = CommittedToTheSecondDoorway(out _);

            // The east task completes, so its target leaves the objectives.
            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(WestId, WestGoal, ObjectiveTargetKind.Task) });
            PlacePlayer(new Vector2Int(8, 3));
            Tick(brain, new Vector2Int(22, 3), 1f);

            Assert.AreEqual(WestId, brain.Prediction.GoalId);
            Assert.AreNotEqual(DoorB, brain.Plan.Cell, "The doorway plan for the east goal is gone.");
        }

        [Test]
        public void PlayerInViewWithinTenMetresIsEngagedOneShotPerInterval()
        {
            CaptainBrain brain = Captain();
            Vector2Int captain = new Vector2Int(22, 3);
            PlacePlayer(new Vector2Int(26, 3));   // 2 m east, in front

            AgentIntent first = Tick(brain, captain, 0f, Vector3.right);
            Assert.AreEqual("Engage", brain.StateName);
            Assert.AreEqual(AgentAction.Shoot, first.Action, "Asks for a shot; the body adds the 0.3 s telegraph.");
            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(26, 3)), first.LookTarget);

            AgentIntent tooSoon = Tick(brain, captain, 0.5f, Vector3.right);
            Assert.AreEqual(AgentAction.None, tooSoon.Action, "One shot per 1.2 s.");

            AgentIntent next = Tick(brain, captain, CaptainBrain.FireInterval, Vector3.right);
            Assert.AreEqual(AgentAction.Shoot, next.Action);
        }

        [Test]
        public void PlayerOutOfRangeOrBehindAWallIsNotEngaged()
        {
            CaptainBrain brain = Captain();
            PlacePlayer(new Vector2Int(5, 3));   // 11 m west, down the open corridor row
            Tick(brain, new Vector2Int(27, 3), 0f, Vector3.left);
            Assert.AreNotEqual("Engage", brain.StateName);

            PlacePlayer(new Vector2Int(18, 1));   // 2 m away but behind the wall between rooms
            Tick(brain, new Vector2Int(22, 1), 0.5f, Vector3.left);
            Assert.AreNotEqual("Engage", brain.StateName);
        }

        [Test]
        public void SteppingInAndOutOfTenMetresDoesNotFlipTheFight()
        {
            // The old rule left Engage after 0.7 s beyond 10 m, so this walk switched it on and
            // off. Contact out to 14 m keeps the fight going.
            CaptainBrain brain = Captain();
            Vector2Int captain = new Vector2Int(27, 3);
            Vector2Int near = new Vector2Int(8, 3);   // 9.5 m west, down the open row
            Vector2Int far = new Vector2Int(5, 3);    // 11 m
            PlacePlayer(near);
            Tick(brain, captain, 0f, Vector3.left);
            Assert.AreEqual("Engage", brain.StateName);

            for (float t = 0.25f; t < 6f; t += 0.25f)
            {
                // About 1 s out, 0.25 s in, over and over.
                PlacePlayer(t % 1.25f < 1f ? far : near);
                Tick(brain, captain, t, Vector3.left);
                Assert.AreEqual("Engage", brain.StateName, $"t = {t}");
            }
        }

        [Test]
        public void AFightLastsAtLeastTwoSecondsThenPursuesToTheLastSeenSpot()
        {
            CaptainBrain brain = Captain();
            Vector2Int captain = new Vector2Int(22, 3);
            Vector2Int lastSeen = new Vector2Int(26, 3);
            PlacePlayer(lastSeen);
            Tick(brain, captain, 0f, Vector3.right);
            Assert.AreEqual("Engage", brain.StateName);

            PlacePlayer(new Vector2Int(18, 1));   // behind the wall between the rooms
            AgentIntent hidden = Tick(brain, captain, 1.5f, Vector3.right);
            Assert.AreEqual("Engage", brain.StateName, "Committed for at least 2 s.");
            Assert.AreEqual(AgentAction.None, hidden.Action, "No shots without contact.");
            Assert.AreEqual(_grid.CellToWorld(lastSeen), hidden.LookTarget, "Faces where it last saw the player, not through the wall.");

            AgentIntent pursue = Tick(brain, captain, 2.1f, Vector3.right);
            Assert.AreEqual("Pursue", brain.StateName);
            Assert.AreEqual(CaptainBrain.InterceptSpeed, pursue.DesiredSpeed);
            Assert.IsNotNull(pursue.Path);
            Assert.AreEqual(_grid.CellToWorld(lastSeen), pursue.Path[pursue.Path.Count - 1], "Walks to the last-seen spot.");
            Assert.AreEqual(AlertLevel.Alert, pursue.Alert);

            // There: it stops and looks round for a second, then predicts again.
            AgentIntent arrived = Tick(brain, lastSeen, 2.6f, Vector3.right);
            Assert.AreEqual("Pursue", brain.StateName);
            Assert.AreEqual(0f, arrived.DesiredSpeed);
            Tick(brain, lastSeen, 3.7f, Vector3.right);
            Tick(brain, lastSeen, 3.8f, Vector3.right);
            Assert.AreNotEqual("Pursue", brain.StateName);
            Assert.AreNotEqual("Engage", brain.StateName);
        }

        [Test]
        public void PursuitGivesUpAfterFiveSecondsIfItNeverGetsThere()
        {
            CaptainBrain brain = Captain();
            Vector2Int captain = new Vector2Int(22, 3);
            PlacePlayer(new Vector2Int(26, 3));
            Tick(brain, captain, 0f, Vector3.right);
            PlacePlayer(new Vector2Int(18, 1));
            Tick(brain, captain, 2.1f, Vector3.right);
            Assert.AreEqual("Pursue", brain.StateName);

            Tick(brain, captain, 2.1f + CaptainBrain.PursueTimeout, Vector3.right);
            Tick(brain, captain, 2.2f + CaptainBrain.PursueTimeout, Vector3.right);
            Assert.AreNotEqual("Pursue", brain.StateName);
        }

        [Test]
        public void ContactDuringThePursuitResumesTheFight()
        {
            CaptainBrain brain = Captain();
            Vector2Int captain = new Vector2Int(22, 3);
            PlacePlayer(new Vector2Int(26, 3));
            Tick(brain, captain, 0f, Vector3.right);
            PlacePlayer(new Vector2Int(18, 1));
            Tick(brain, captain, 2.1f, Vector3.right);
            Assert.AreEqual("Pursue", brain.StateName);

            // Back in line of sight through the doorway, 6 m west and behind it: contact,
            // even outside the view cone.
            PlacePlayer(new Vector2Int(10, 3));
            AgentIntent again = Tick(brain, captain, 2.4f, Vector3.right);
            Assert.AreEqual("Engage", brain.StateName);
            Assert.AreEqual(AgentAction.Shoot, again.Action);
        }

        [Test]
        public void ADeadPlayerEndsTheFightAtOnce()
        {
            CaptainBrain brain = Captain();
            Vector2Int captain = new Vector2Int(22, 3);
            PlacePlayer(new Vector2Int(26, 3));
            Tick(brain, captain, 0f, Vector3.right);

            PlacePlayer(new Vector2Int(26, 3), alive: false);
            Tick(brain, captain, 0.2f, Vector3.right);
            Assert.AreNotEqual("Engage", brain.StateName, "The 2 s commitment does not outlive the player.");
            Assert.AreNotEqual("Pursue", brain.StateName);
        }

        [Test]
        public void StunDropsThePredictionAndReassessesAfterTheReboot()
        {
            CaptainBrain brain = CommittedToTheSecondDoorway(out _);
            Assert.IsTrue(brain.Prediction.IsKnown);

            brain.OnStunned(6f);
            Assert.IsFalse(brain.Prediction.IsKnown);
            Assert.IsFalse(brain.Plan.HasPlan);

            PlacePlayer(new Vector2Int(8, 3));
            Tick(brain, new Vector2Int(22, 3), 6.5f);
            Assert.AreNotEqual("Stunned", brain.StateName, "Stunned and Reassess pass straight through.");
            Assert.IsTrue(brain.Prediction.IsKnown, "Predicts again from the kept history.");
        }

        [Test]
        public void DormantCaptainIsNotStunnedAwake()
        {
            CaptainBrain brain = Captain(startAwake: false);
            Tick(brain, new Vector2Int(22, 3), 0f);
            brain.OnStunned(6f);
            _world.SetCaptainAwake();

            Tick(brain, new Vector2Int(22, 3), 1f);
            Tick(brain, new Vector2Int(22, 3), 1.5f);
            Assert.AreEqual("Observe", brain.StateName);
        }

        [Test]
        public void BlockedInterceptCellMakesTheCaptainReplan()
        {
            CaptainBrain brain = CommittedToTheSecondDoorway(out _);

            _grid.SetWalkable(DoorB, false);   // a box is pushed into the doorway
            brain.OnGraphChanged(new List<Vector2Int> { DoorB });
            Tick(brain, new Vector2Int(22, 3), 0.6f);

            Assert.AreNotEqual(DoorB, brain.Plan.Cell);
            Assert.AreEqual(WestId, brain.Prediction.GoalId, "The east goal is walled off now, so only the west one is left.");
        }

        [Test]
        public void GoalThePlayerHasReachedIsLeftOutUntilTheyLeave()
        {
            CaptainBrain brain = Captain();
            Vector2Int captain = new Vector2Int(15, 1);
            PlacePlayer(new Vector2Int(23, 3));
            Tick(brain, captain, 0f, Vector3.left);
            PlacePlayer(EastGoal);
            Tick(brain, captain, 0.5f, Vector3.left);   // predicts east, sees the player is there, leaves it out
            Tick(brain, captain, 0.5f, Vector3.left);   // re-predicts straight away

            Assert.AreEqual(WestId, brain.Prediction.GoalId);
        }

        [Test]
        public void PredictionIsOfferedToTheRuntimeThroughIGoalPredictor()
        {
            IGoalPredictor predictor = CommittedToTheSecondDoorway(out _);

            PredictedGoal prediction = predictor.Prediction;
            Assert.IsTrue(prediction.IsKnown);
            Assert.AreEqual(EastId, prediction.GoalId);
            Assert.AreEqual(EastGoal, prediction.Cell);
            Assert.AreEqual(0.5f, prediction.Time);
        }
    }
}
