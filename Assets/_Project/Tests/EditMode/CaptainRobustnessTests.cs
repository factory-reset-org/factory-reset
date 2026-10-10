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
    /// <summary>
    /// The Captain cannot be left standing still or flip-flopping: a goal it cannot reach is
    /// no plan, a door that opens gets it moving without the target changing, a target it
    /// makes no progress towards is given up and avoided, it closes in on a player busy at
    /// the only goal, a confidence hovering round 0.5 does not stop and start it, with nothing
    /// to watch it guards the likeliest goal, and a back-off step it cannot finish is dropped.
    /// These replay the situations found in the review (scripted, with a simple body that
    /// walks the brain's routes and faces where it walks).
    /// </summary>
    public class CaptainRobustnessTests
    {
        const float Dt = 0.05f;

        // A walled room, optionally split by a wall at x = 30 with a three-cell doorway (rows 18-20).
        static GridGraph Room(int width, int height, bool splitWithDoor = false)
        {
            var grid = new GridGraph(width, height, Vector3.zero);
            for (int x = 0; x < width; x++)
            {
                grid.SetWalkable(new Vector2Int(x, 0), false);
                grid.SetWalkable(new Vector2Int(x, height - 1), false);
            }
            for (int y = 0; y < height; y++)
            {
                grid.SetWalkable(new Vector2Int(0, y), false);
                grid.SetWalkable(new Vector2Int(width - 1, y), false);
                if (splitWithDoor && (y < 18 || y > 20))
                    grid.SetWalkable(new Vector2Int(30, y), false);
            }
            return grid;
        }

        static readonly Vector2Int[] DoorCells = { new Vector2Int(30, 18), new Vector2Int(30, 19), new Vector2Int(30, 20) };

        static void SetDoor(GridGraph grid, bool open)
        {
            foreach (Vector2Int cell in DoorCells)
                grid.SetWalkable(cell, open);
        }

        static WorldBlackboard World(params ObjectiveTarget[] goals)
        {
            var world = new WorldBlackboard();
            world.SetObjectiveTargets(goals);
            return world;
        }

        static void PlacePlayer(WorldBlackboard world, GridGraph grid, Vector3 position, Vector3 velocity)
        {
            Vector3 forward = velocity.sqrMagnitude > 0f ? velocity.normalized : Vector3.forward;
            world.SetPlayer(new PlayerSnapshot(true, grid.WorldToCell(position), position, velocity, forward,
                8.6f, true, 1f, 1f, false, 0f, -1f));
        }

        /// <summary>A body that walks the brain's routes like AgentPathFollower and faces where it walks.</summary>
        sealed class Body
        {
            readonly GridGraph _grid;
            List<Vector3> _path = new List<Vector3>();
            int _next;
            float _speed;
            public Vector3 Position;
            public Vector3 Forward = Vector3.left;
            public bool Pinned;   // a prop or a jam holds it where it is

            // The body's stand-off: held still within this distance of HoldNear (the player).
            public Vector3? HoldNear;
            public float HoldDistance = 2f;

            public Body(GridGraph grid, Vector2Int cell)
            {
                _grid = grid;
                Position = grid.CellToWorld(cell);
            }

            public bool HasPath => _next < _path.Count;

            public AgentIntent Tick(CaptainBrain brain, WorldBlackboard world, float time)
            {
                var ctx = new AgentContext(_grid.WorldToCell(Position), Position, Forward, time, world, default(SensorSnapshot));
                AgentIntent intent = brain.Tick(ctx);
                if (intent.Path != null)
                {
                    _path = new List<Vector3>(intent.Path);
                    _next = 0;
                    _speed = intent.DesiredSpeed;
                }
                while (_next < _path.Count && Flat(_path[_next] - Position).magnitude <= 0.3f)
                    _next++;
                bool held = HoldNear.HasValue && Flat(HoldNear.Value - Position).magnitude <= HoldDistance;
                if (_next < _path.Count && !Pinned && !held)
                {
                    Vector3 to = Flat(_path[_next] - Position);
                    float step = Mathf.Min(_speed * Dt, to.magnitude);
                    Position += to.normalized * step;
                    Forward = to.normalized;
                }
                return intent;
            }

            static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        }

        static CaptainBrain Captain(GridGraph grid, WorldBlackboard world) =>
            new CaptainBrain(grid, new AStarSearch(grid), world, startAwake: true);

        [Test]
        public void AGoalTheCaptainCannotReachGivesNoPlan()
        {
            GridGraph grid = Room(60, 40, splitWithDoor: true);
            SetDoor(grid, open: false);
            var goalField = new DijkstraField(grid);
            goalField.Compute(new Vector2Int(55, 30), BaseCostModel.Instance);

            // The player is right by the goal, so only "defend the goal" could qualify, and the
            // Captain is shut in the other room.
            InterceptPlan plan = new InterceptPlanner(grid).Plan(goalField, new Vector2Int(52, 30), new Vector2Int(20, 20), 8.6f, 4.6f);

            Assert.AreEqual(InterceptKind.None, plan.Kind, "No plan it cannot walk.");
        }

        [Test]
        public void ADoorThatOpensAfterTheWakeGetsTheCaptainMoving()
        {
            // The Chapter 3 skip: the Captain decides while its door is still shut, then the
            // door finishes opening with the target unchanged.
            GridGraph grid = Room(60, 40, splitWithDoor: true);
            SetDoor(grid, open: false);
            WorldBlackboard world = World(new ObjectiveTarget(1, new Vector2Int(55, 30), ObjectiveTargetKind.Task));
            CaptainBrain brain = Captain(grid, world);
            var body = new Body(grid, new Vector2Int(20, 20));
            Vector3 player = grid.CellToWorld(new Vector2Int(52, 30));   // by the goal, out of view

            float t = 0f;
            for (; t < 2f; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                body.Tick(brain, world, t);
            }
            Assert.AreNotEqual("Intercept", brain.StateName, "Shut in: no plan to stand still on.");
            Assert.IsFalse(body.HasPath);

            SetDoor(grid, open: true);
            brain.OnGraphChanged(DoorCells);
            for (float end = t + 1f; t < end; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                body.Tick(brain, world, t);
            }
            Assert.AreEqual("Intercept", brain.StateName);
            Assert.IsTrue(body.HasPath, "It walks as soon as the door is open.");
            Assert.Less(body.Position.x, grid.CellToWorld(new Vector2Int(20, 20)).x + 6f);
            Assert.Greater(body.Position.x, grid.CellToWorld(new Vector2Int(20, 20)).x + 1f, "And it has started moving.");
        }

        [Test]
        public void ATargetItMakesNoProgressTowardsIsGivenUpAndAvoided()
        {
            GridGraph grid = Room(100, 60);
            WorldBlackboard world = World(new ObjectiveTarget(1, new Vector2Int(95, 30), ObjectiveTargetKind.Task));
            CaptainBrain brain = Captain(grid, world);
            var body = new Body(grid, new Vector2Int(45, 55));
            Vector3 player = grid.CellToWorld(new Vector2Int(5, 30));
            Vector3 toGoal = (grid.CellToWorld(new Vector2Int(95, 30)) - player).normalized * 4f;

            float t = 0f;
            for (; t < 3f && brain.StateName != "Intercept"; t += Dt)
            {
                player += toGoal * Dt;
                PlacePlayer(world, grid, player, toGoal);
                body.Tick(brain, world, t);
            }
            Assert.AreEqual("Intercept", brain.StateName);
            Vector2Int target = brain.TargetCell;

            // Pinned on a prop the grid does not know about. The player stops too, so the plan
            // itself does not change: only the lack of progress can end it.
            body.Pinned = true;
            bool leftIntercept = false;
            for (float end = t + 3f; t < end; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                body.Tick(brain, world, t);
                if (brain.StateName != "Intercept" || brain.TargetCell != target)
                {
                    leftIntercept = true;
                    break;
                }
            }
            Assert.IsTrue(leftIntercept, "Given up within about 2 s instead of pushing forever.");
            Assert.IsTrue(brain.IsCellAvoided(target), "And that cell is not chosen again for a while.");
            if (brain.HasTarget)
                Assert.AreNotEqual(target, brain.TargetCell);
        }

        [Test]
        public void PushedOffItsAmbushCellTheCaptainPlansAgain()
        {
            GridGraph grid = Room(100, 60);
            WorldBlackboard world = World(new ObjectiveTarget(1, new Vector2Int(95, 30), ObjectiveTargetKind.Task));
            CaptainBrain brain = Captain(grid, world);
            var body = new Body(grid, new Vector2Int(45, 55));
            Vector3 player = grid.CellToWorld(new Vector2Int(5, 30));
            Vector3 toGoal = (grid.CellToWorld(new Vector2Int(95, 30)) - player).normalized * 1.5f;

            float t = 0f;
            for (; t < 15f && brain.StateName != "Ambush"; t += Dt)
            {
                player += toGoal * Dt;
                PlacePlayer(world, grid, player, toGoal);
                body.Tick(brain, world, t);
            }
            Assert.AreEqual("Ambush", brain.StateName);

            body.Position += new Vector3(0f, 0f, 6f);   // shoved 6 m off its cell
            bool replanned = false;
            for (float end = t + 1.5f; t < end && !replanned; t += Dt)
            {
                player += toGoal * Dt;
                PlacePlayer(world, grid, player, toGoal);
                body.Tick(brain, world, t);
                replanned = brain.StateName != "Ambush";
            }
            Assert.IsTrue(replanned, "Not holding a cell it is no longer standing on.");
        }

        [Test]
        public void APropBetweenItAndThePlayerAtTheConsoleDoesNotStopTheFight()
        {
            // The level after props were added to the grid: the Console's footprint is blocked
            // (walk round it), the player holds the Console on its east side, the Captain starts
            // on the west. It used to close in, stop at the stand-off 2 m away on the far side of
            // the corner, fail to "see" over the Console, give up and stand there.
            GridGraph grid = Room(60, 40);
            for (int x = 25; x <= 28; x++)
                for (int y = 18; y <= 20; y++)
                    grid.AddBlocker(new Vector2Int(x, y));
            Vector2Int console = new Vector2Int(27, 19);
            WorldBlackboard world = World(new ObjectiveTarget(9, console, ObjectiveTargetKind.Console));
            CaptainBrain brain = Captain(grid, world);
            Vector3 player = grid.CellToWorld(console) + new Vector3(0.9f, 0f, 0f);   // at the Console, just east of it
            var body = new Body(grid, new Vector2Int(19, 19)) { HoldNear = player };

            bool converged = false, engaged = false;
            for (float t = 0f; t < 8f && !engaged; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                body.Tick(brain, world, t);
                converged |= brain.StateName == "Converge";
                engaged = brain.StateName == "Engage";
            }
            Assert.IsTrue(converged, "Closes in round the Console.");
            Assert.IsTrue(engaged, "And sees the player over it and fights.");
        }

        [Test]
        public void WhileThePlayerHoldsTheOnlyGoalTheCaptainClosesInAndEngages()
        {
            // The console hold: the player reached the last goal, so there is nothing left to
            // predict. Before, the Captain watched from 23 m while they finished.
            GridGraph grid = Room(100, 60);
            WorldBlackboard world = World(new ObjectiveTarget(9, new Vector2Int(50, 30), ObjectiveTargetKind.Console));
            CaptainBrain brain = Captain(grid, world);
            var body = new Body(grid, new Vector2Int(90, 55));
            Vector3 player = grid.CellToWorld(new Vector2Int(50, 30));
            float start = Vector3.Distance(body.Position, player);

            bool converged = false, engaged = false;
            for (float t = 0f; t < 12f; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                body.Tick(brain, world, t);
                converged |= brain.StateName == "Converge";
                if (brain.StateName == "Engage")
                {
                    engaged = true;
                    break;
                }
            }
            Assert.IsTrue(converged, "Closes in on the player at the goal.");
            Assert.IsTrue(engaged, "And starts the fight.");
            Assert.Less(Vector3.Distance(body.Position, player), Mathf.Min(start, CaptainBrain.EngageRange + 0.5f));
        }

        [Test]
        public void WeavingBetweenTwoGoalsDoesNotStopAndStartTheCaptain()
        {
            // The review's run: three Observe/Intercept flips and two dead stops in 13 s.
            GridGraph grid = Room(100, 60);
            WorldBlackboard world = World(
                new ObjectiveTarget(1, new Vector2Int(90, 50), ObjectiveTargetKind.Task),
                new ObjectiveTarget(2, new Vector2Int(90, 10), ObjectiveTargetKind.Task));
            CaptainBrain brain = Captain(grid, world);
            var body = new Body(grid, new Vector2Int(10, 55));
            Vector2Int[] weave =
            {
                new Vector2Int(5, 30), new Vector2Int(55, 30), new Vector2Int(62, 35), new Vector2Int(66, 26),
                new Vector2Int(71, 35), new Vector2Int(75, 25), new Vector2Int(80, 33)
            };
            Vector3 player = grid.CellToWorld(weave[0]);
            int next = 1, flips = 0;
            string last = brain.StateName;

            for (float t = 0f; t < 25f && brain.StateName != "Engage"; t += Dt)
            {
                Vector3 velocity = Vector3.zero;
                if (next < weave.Length)
                {
                    Vector3 to = grid.CellToWorld(weave[next]) - player;
                    if (to.magnitude <= 4f * Dt)
                    {
                        player = grid.CellToWorld(weave[next]);
                        next++;
                    }
                    else
                    {
                        velocity = to.normalized * 4f;
                        player += velocity * Dt;
                    }
                }
                PlacePlayer(world, grid, player, velocity);
                body.Tick(brain, world, t);
                string state = brain.StateName;
                if ((last == "Intercept" && state == "Observe") || (last == "Observe" && state == "Intercept"))
                    flips++;
                last = state;
            }

            // Each Intercept -> Observe flip was a dead stop in the middle of a route.
            Assert.LessOrEqual(flips, 1, "Commits once instead of flipping at the 0.5 edge.");
        }

        // Three goals of one kind: a player standing still is equally likely to want each, so
        // the Captain is never confident (1/3 each).
        static WorldBlackboard ThreeTasks() => World(
            new ObjectiveTarget(1, new Vector2Int(10, 50), ObjectiveTargetKind.Task),
            new ObjectiveTarget(2, new Vector2Int(90, 50), ObjectiveTargetKind.Task),
            new ObjectiveTarget(3, new Vector2Int(50, 55), ObjectiveTargetKind.Task));

        [Test]
        public void APlayerHidingAwayFromEveryGoalIsWaitedForAtTheLikeliestGoal()
        {
            // Chapter 4 in Storage: the player fled the fight and stands still, out of contact,
            // in a room with no goals. It used to stand where it lost them, showing "?", for good.
            GridGraph grid = Room(100, 60);
            WorldBlackboard world = ThreeTasks();
            CaptainBrain brain = Captain(grid, world);
            var body = new Body(grid, new Vector2Int(50, 45));
            Vector3 player = grid.CellToWorld(new Vector2Int(50, 5));   // 20 m away: no contact

            float t = 0f;
            for (; t < 2.5f; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                body.Tick(brain, world, t);
            }
            Assert.AreEqual("Observe", brain.StateName, "Watches first.");
            Assert.Less(brain.Confidence, CaptainBrain.ConfidenceThreshold);

            for (float end = t + 15f; t < end && brain.StateName != "Ambush"; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                body.Tick(brain, world, t);
            }
            Assert.AreEqual("Ambush", brain.StateName, "Then guards a goal instead of watching nothing.");
            Assert.AreEqual(InterceptKind.Guard, brain.Plan.Kind);
            Assert.AreEqual(brain.Prediction.Cell, brain.TargetCell, "The likeliest goal itself.");
            Assert.Less(Vector3.Distance(body.Position, grid.CellToWorld(brain.TargetCell)), 1f);

            // It holds the goal while nothing changes.
            for (float end = t + 3f; t < end; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                body.Tick(brain, world, t);
                Assert.AreEqual("Ambush", brain.StateName, $"t = {t}");
            }
        }

        [Test]
        public void WhileThePlayerIsInContactAnUnsureCaptainOnlyWatches()
        {
            // 12 m away in the open: in contact (line of sight within 14 m) but not close enough
            // to fight. There is something to watch, so no guarding.
            GridGraph grid = Room(100, 60);
            WorldBlackboard world = ThreeTasks();
            CaptainBrain brain = Captain(grid, world);
            var body = new Body(grid, new Vector2Int(50, 30));
            Vector3 player = grid.CellToWorld(new Vector2Int(26, 30));

            for (float t = 0f; t < 8f; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                body.Tick(brain, world, t);
                Assert.AreEqual("Observe", brain.StateName, $"t = {t}");
            }
            Assert.IsFalse(brain.Plan.HasPlan);
        }

        [Test]
        public void ABackOffStepItCannotFinishIsDroppedAndNotRetried()
        {
            // Observe backs off from a player within 8 m it cannot see (behind it). Pinned on a
            // body lying in the aisle, it used to run on the spot for good.
            GridGraph grid = Room(100, 60);
            WorldBlackboard world = ThreeTasks();
            CaptainBrain brain = Captain(grid, world);
            var body = new Body(grid, new Vector2Int(50, 30)) { Forward = Vector3.left, Pinned = true };
            Vector3 player = grid.CellToWorld(new Vector2Int(62, 30));   // 6 m east, behind it

            Vector3? stepEnd = null;
            float droppedAt = -1f;
            float t = 0f;
            for (; t < 4f && droppedAt < 0f; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                AgentIntent intent = body.Tick(brain, world, t);
                if (intent.Path != null && intent.Path.Count > 0)
                    stepEnd = intent.Path[intent.Path.Count - 1];
                else if (intent.Path != null && stepEnd.HasValue)
                    droppedAt = t;
            }
            Assert.IsTrue(stepEnd.HasValue, "It backs off.");
            Assert.Greater(droppedAt, CaptainBrain.StuckTime - 0.1f, "Not before it has had 2 s to move.");
            Assert.Less(droppedAt, CaptainBrain.StuckTime + 0.5f, "But soon after.");
            Assert.AreEqual("Observe", brain.StateName);
            Assert.IsTrue(brain.IsCellAvoided(grid.WorldToCell(stepEnd.Value)));

            // Standing, it faces the player, and does not try the same step again.
            for (float end = t + 3f; t < end; t += Dt)
            {
                PlacePlayer(world, grid, player, Vector3.zero);
                AgentIntent intent = body.Tick(brain, world, t);
                Assert.AreEqual(player, intent.LookTarget);
                Assert.IsTrue(intent.Path == null || intent.Path.Count == 0, $"No new step at t = {t}");
            }
        }
    }
}
