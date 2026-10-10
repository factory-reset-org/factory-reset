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
    /// The Captain opens doors: its router goes through a closed door when that is the only way
    /// or cheaper than walking round (a door costs 11 cells, about 1.2 s), it stops at the door
    /// and asks for it to open, carries on once it has, and shuts it behind when the door is on
    /// the player's predicted route. A door that will not open is given up.
    /// </summary>
    public class CaptainDoorTests
    {
        const float Dt = 0.05f;
        const int DoorId = 7;
        static readonly Vector2Int[] DoorCells = { new Vector2Int(30, 18), new Vector2Int(30, 19), new Vector2Int(30, 20) };
        static readonly Vector2Int[] GapCells = { new Vector2Int(30, 36), new Vector2Int(30, 37) };

        // Two rooms split at x = 30 by a wall with door 7 (rows 18-20), and optionally a gap
        // with no door far down the wall (rows 36-37), the long way round.
        static GridGraph TwoRooms(bool doorClosed, bool gap)
        {
            var grid = new GridGraph(60, 40, Vector3.zero);
            for (int x = 0; x < 60; x++)
            {
                grid.SetWalkable(new Vector2Int(x, 0), false);
                grid.SetWalkable(new Vector2Int(x, 39), false);
            }
            for (int y = 0; y < 40; y++)
            {
                grid.SetWalkable(new Vector2Int(0, y), false);
                grid.SetWalkable(new Vector2Int(59, y), false);
                bool door = y >= 18 && y <= 20;
                bool opening = gap && (y == 36 || y == 37);
                if (!door && !opening)
                    grid.SetWalkable(new Vector2Int(30, y), false);
            }
            foreach (Vector2Int cell in DoorCells)
            {
                grid.SetDoorway(cell, true);
                grid.SetDoor(cell, DoorId, doorClosed);
            }
            return grid;
        }

        static void SetDoor(GridGraph grid, bool closed)
        {
            foreach (Vector2Int cell in DoorCells)
                grid.SetDoor(cell, DoorId, closed);
        }

        static bool CrossesDoor(List<Vector2Int> path)
        {
            foreach (Vector2Int cell in path)
                if (System.Array.IndexOf(DoorCells, cell) >= 0)
                    return true;
            return false;
        }

        // ---- The router -----------------------------------------------------------------

        [Test]
        public void TheRouterGoesThroughTheOnlyDoorAndPaysForOpeningIt()
        {
            GridGraph grid = TwoRooms(doorClosed: true, gap: false);
            var router = new DoorRouter(grid);

            List<Vector2Int> path = router.FindPath(new Vector2Int(20, 19), new Vector2Int(40, 19));

            Assert.IsNotNull(path);
            Assert.IsTrue(CrossesDoor(path));
            Assert.AreEqual(20f + DoorRouter.DoorPenalty, router.LastCost, 1e-3f, "20 straight steps plus one door.");
            Assert.IsFalse(new AStarSearch(grid).FindPath(new Vector2Int(20, 19), new Vector2Int(40, 19), BaseCostModel.Instance).Found,
                "The shared A* sees a wall.");
        }

        [Test]
        public void TheRouterWalksRoundWhenThatIsCheaperThanOpeningTheDoor()
        {
            GridGraph grid = TwoRooms(doorClosed: true, gap: true);
            var router = new DoorRouter(grid);

            List<Vector2Int> nearGap = router.FindPath(new Vector2Int(27, 34), new Vector2Int(33, 34));
            Assert.IsFalse(CrossesDoor(nearGap), "Next to the gap: walk through it.");

            List<Vector2Int> nearDoor = router.FindPath(new Vector2Int(27, 19), new Vector2Int(33, 19));
            Assert.IsTrue(CrossesDoor(nearDoor), "Next to the door: 6 + 11 beats the 37-cell walk round.");
        }

        [Test]
        public void ADoorTheCaptainGaveUpOnIsAWall()
        {
            GridGraph grid = TwoRooms(doorClosed: true, gap: false);
            var router = new DoorRouter(grid);

            Assert.IsNull(router.FindPath(new Vector2Int(20, 19), new Vector2Int(40, 19), id => id == DoorId));
        }

        // ---- The brain, with a scripted body and door ------------------------------------

        /// <summary>
        /// Walks the brain's routes and faces where it walks; opens door 7 one second after the
        /// brain asks within the body's 2.5 m reach of the door, then tells the brain, as the
        /// runtime would. Shuts it when asked, likewise.
        /// </summary>
        sealed class World
        {
            public readonly GridGraph Grid;
            public readonly WorldBlackboard Board = new WorldBlackboard();
            public readonly CaptainBrain Brain;
            public Vector3 Captain;
            public Vector3 Forward = Vector3.right;
            public Vector3 Player;
            public bool DoorOpens = true;
            public int OpenRequests, CloseRequests;
            public bool WaitedAtDoor;
            List<Vector3> _path = new List<Vector3>();
            int _next;
            float _speed;
            float _openAt = float.PositiveInfinity;
            AgentAction _lastAction;

            public World(GridGraph grid, Vector2Int captain, Vector2Int player, Vector2Int goal)
            {
                Grid = grid;
                Board.SetObjectiveTargets(new[] { new ObjectiveTarget(1, goal, ObjectiveTargetKind.Task) });
                Brain = new CaptainBrain(grid, new AStarSearch(grid), Board, startAwake: true);
                Captain = grid.CellToWorld(captain);
                Player = grid.CellToWorld(player);
            }

            Vector3 DoorCentre => Grid.CellToWorld(DoorCells[1]);

            public void Tick(float t)
            {
                Board.SetPlayer(new PlayerSnapshot(true, Grid.WorldToCell(Player), Player, Vector3.zero, Vector3.right,
                    8.6f, true, 1f, 1f, false, 0f, -1f));
                var ctx = new AgentContext(Grid.WorldToCell(Captain), Captain, Forward, t, Board, default(SensorSnapshot));
                AgentIntent intent = Brain.Tick(ctx);
                WaitedAtDoor |= Brain.IsWaitingAtDoor;

                bool fresh = intent.Action != _lastAction;
                _lastAction = intent.Action;
                bool inReach = Flat(DoorCentre - Captain).magnitude <= 2.5f;
                if (intent.Action == AgentAction.OpenDoor && intent.ActionTargetId == DoorId && fresh)
                {
                    OpenRequests++;
                    if (inReach && DoorOpens)
                    {
                        _openAt = t + 1f;   // the swing
                        Brain.OnActionResolved(AgentAction.OpenDoor, DoorId, true);
                    }
                    else
                    {
                        Brain.OnActionResolved(AgentAction.OpenDoor, DoorId, false);
                    }
                }
                if (intent.Action == AgentAction.CloseDoor && intent.ActionTargetId == DoorId && fresh)
                {
                    CloseRequests++;
                    SetDoor(Grid, closed: true);
                    Brain.OnGraphChanged(DoorCells);
                    Brain.OnActionResolved(AgentAction.CloseDoor, DoorId, true);
                }
                if (t >= _openAt)
                {
                    _openAt = float.PositiveInfinity;
                    SetDoor(Grid, closed: false);
                    Brain.OnGraphChanged(DoorCells);
                }

                if (intent.Path != null)
                {
                    _path = new List<Vector3>(intent.Path);
                    _next = 0;
                    _speed = intent.DesiredSpeed;
                }
                while (_next < _path.Count && Flat(_path[_next] - Captain).magnitude <= 0.3f)
                    _next++;
                if (_next < _path.Count)
                {
                    Vector3 to = Flat(_path[_next] - Captain);
                    Captain += to.normalized * Mathf.Min(_speed * Dt, to.magnitude);
                    Forward = to.normalized;
                }
            }

            static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        }

        [Test]
        public void ShutInTheCaptainOpensTheDoorAndGoesThrough()
        {
            // Chapter 3 before the cutscene's door has opened, or a door the player shut on it.
            var world = new World(TwoRooms(doorClosed: true, gap: false),
                captain: new Vector2Int(20, 22), player: new Vector2Int(52, 30), goal: new Vector2Int(55, 30));

            for (float t = 0f; t < 10f && world.Captain.x < world.Grid.CellToWorld(new Vector2Int(33, 0)).x; t += Dt)
                world.Tick(t);

            Assert.IsTrue(world.WaitedAtDoor, "Stopped at the door.");
            Assert.AreEqual(1, world.OpenRequests, "Asked once to open it.");
            Assert.Greater(world.Captain.x, world.Grid.CellToWorld(new Vector2Int(33, 0)).x, "And went through.");
        }

        [Test]
        public void ItShutsTheDoorBehindItWhenTheDoorIsOnThePlayersRoute()
        {
            // The door is the Captain's shortcut to the goal. Once it is open the door is on the
            // player's shortest route too, and the player (5 m from it) is too close for the
            // Captain to hold the doorway, so it carries on to the goal and shuts the door behind
            // itself: the player has to open it again or take the long way round.
            var world = new World(TwoRooms(doorClosed: true, gap: true),
                captain: new Vector2Int(26, 20), player: new Vector2Int(20, 19), goal: new Vector2Int(55, 19));

            for (float t = 0f; t < 12f && world.CloseRequests == 0; t += Dt)
                world.Tick(t);

            Assert.AreEqual(1, world.OpenRequests, "Opened its shortcut.");
            Assert.AreEqual(1, world.CloseRequests, "Shut it behind.");
            Assert.IsTrue(world.Grid.GetNode(DoorCells[1]).IsDoorClosed);
            Assert.Greater(world.Captain.x, world.Grid.CellToWorld(DoorCells[1]).x, "From the far side.");
        }

        [Test]
        public void ItNeverShutsADoorThatWasAlreadyOpen()
        {
            // The same layout with the door open from the start: the Captain walks through it
            // and leaves it as it found it. It only shuts doors it had to open itself.
            var world = new World(TwoRooms(doorClosed: false, gap: true),
                captain: new Vector2Int(26, 20), player: new Vector2Int(20, 19), goal: new Vector2Int(55, 19));

            for (float t = 0f; t < 12f; t += Dt)
                world.Tick(t);

            Assert.Greater(world.Captain.x, world.Grid.CellToWorld(DoorCells[1]).x + 1f, "It went through the door.");
            Assert.AreEqual(0, world.OpenRequests);
            Assert.AreEqual(0, world.CloseRequests, "An open door stays open.");
            Assert.IsFalse(world.Grid.GetNode(DoorCells[1]).IsDoorClosed);
        }

        [Test]
        public void ADoorThatWillNotOpenIsGivenUp()
        {
            var world = new World(TwoRooms(doorClosed: true, gap: false),
                captain: new Vector2Int(20, 22), player: new Vector2Int(52, 30), goal: new Vector2Int(55, 30))
            { DoorOpens = false };

            bool movedOn = false;
            for (float t = 0f; t < 8f; t += Dt)
            {
                world.Tick(t);
                if (world.WaitedAtDoor && !world.Brain.IsWaitingAtDoor)
                {
                    movedOn = true;
                    break;
                }
            }
            Assert.IsTrue(movedOn, "Does not stand at a jammed door for ever.");
            Assert.IsNull(new DoorRouter(world.Grid).FindPath(new Vector2Int(20, 22), new Vector2Int(55, 30), id => world.Brain.IsDoorGivenUp(id)),
                "That door is off its routes for a while.");
        }
    }
}
