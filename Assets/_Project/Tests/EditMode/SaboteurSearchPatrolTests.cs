using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The Saboteur after it loses the player (search where last seen, investigate a noise, back to patrol),
    /// its patrol of the whole room, progress along its route, the squad's shared failures and the steadier
    /// hand-over between attacking, fleeing and patrolling.
    /// </summary>
    public class SaboteurSearchPatrolTests
    {
        sealed class FakeSight : IPlayerSight
        {
            public bool Visible = true;
            public bool CanSeeCell(Vector2Int cell) => Visible;
        }

        const float Frame = 1f / 64f;

        GridGraph _grid;
        TargetClaims _claims;
        FakeSight _sight;
        Vector2Int _home;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(80, 80, Vector3.zero);
            _claims = new TargetClaims();
            _sight = new FakeSight();
            _home = new Vector2Int(40, 40);
        }

        static SaboteurIdentity Identity(SaboteurLetter letter) => new SaboteurIdentity((int)letter + 1, letter);

        SaboteurBrain Brain(SaboteurLetter letter = SaboteurLetter.B, SaboteurOptions options = null, params Vector2Int[] patrol)
        {
            var points = patrol.Select(c => _grid.CellToWorld(c)).ToList();
            var source = new CompositeCandidateSource(new AttackPlayerSource(_sight), new FleeSource(_sight));
            return new SaboteurBrain(Identity(letter), _grid, new AStarSearch(_grid), _claims, points, null, 0, source, null,
                options ?? SaboteurOptions.WaypointsOnly);
        }

        WorldBlackboard PlayerAtCell(Vector2Int cell, Vector3? velocity = null, bool alive = true)
        {
            var world = new WorldBlackboard();
            world.SetPlayer(new PlayerSnapshot(true, cell, _grid.CellToWorld(cell), velocity ?? Vector3.zero, Vector3.forward,
                5f, alive, 1f, 1f, false, 0f, 0f));
            return world;
        }

        AgentContext At(Vector2Int cell, WorldBlackboard world, float time, Vector3? facing = null, SensorSnapshot senses = default) =>
            new AgentContext(cell, _grid.CellToWorld(cell), facing ?? Vector3.forward, time, world, senses);

        // ---- Searching after losing sight

        [Test]
        public void APlayerLostFromSightIsSearchedForWhereLastSeenAheadOfWhereTheyWent()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, null, new Vector2Int(10, 10));

            // Seen 10 m ahead (out of attack range), moving east at 2 m/s.
            brain.Tick(At(_home, PlayerAtCell(new Vector2Int(40, 60), new Vector3(2f, 0f, 0f)), 0f));
            _sight.Visible = false;

            AgentIntent intent = default;
            for (float t = Frame; t < 0.6f; t += Frame)
                intent = brain.Tick(At(_home, PlayerAtCell(new Vector2Int(70, 70)), t));

            Assert.AreEqual(SaboteurActionKind.Search, brain.CurrentAction.Kind);
            Assert.IsTrue(brain.Awareness.HasLastSeen);
            Vector3 expected = _grid.CellToWorld(new Vector2Int(40, 60)) + new Vector3(2f, 0f, 0f) * SaboteurBrain.SearchLeadSeconds;
            Assert.AreEqual(expected.x, brain.SearchGoal.x, 1e-3f, "One second ahead along the way the player went.");
            Assert.AreEqual(expected.z, brain.SearchGoal.z, 1e-3f);
        }

        [Test]
        public void ArrivingItLooksRoundChecksOneSpotFurtherOnAndGoesBackToPatrol()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, null, new Vector2Int(10, 10));
            Vector2Int seenAt = new Vector2Int(40, 60);
            brain.Tick(At(_home, PlayerAtCell(seenAt, new Vector3(0f, 0f, 2f)), 0f));
            _sight.Visible = false;
            WorldBlackboard gone = PlayerAtCell(new Vector2Int(75, 75));

            // Walk the body to the search point and stand there.
            Vector2Int goal = _grid.WorldToCell(_grid.CellToWorld(seenAt) + new Vector3(0f, 0f, 2f));
            float t = Frame;
            for (; t < 0.5f; t += Frame)
                brain.Tick(At(_home, gone, t));
            AgentIntent arrived = brain.Tick(At(goal, gone, t));
            Assert.AreEqual("Search", arrived.DebugState);
            Assert.IsNotNull(arrived.Path);
            Assert.AreEqual(0, arrived.Path.Count, "Stopped at the spot.");

            // Looking round: the look target turns.
            AgentIntent look1 = brain.Tick(At(goal, gone, t + 0.4f));
            AgentIntent look2 = brain.Tick(At(goal, gone, t + 1.2f));
            Assert.IsTrue(look1.LookTarget.HasValue && look2.LookTarget.HasValue);
            Assert.Greater((look1.LookTarget.Value - look2.LookTarget.Value).magnitude, 0.5f, "It turns its head.");

            // Then one spot further on, the way the player went.
            AgentIntent probe = brain.Tick(At(goal, gone, t + SaboteurBrain.LookAroundSeconds + 0.1f));
            Assert.AreEqual("Search", probe.DebugState);
            Assert.IsNotNull(probe.Path);
            Assert.Greater(probe.Path.Count, 0, "Walking to the probe point.");

            Vector2Int probeCell = _grid.WorldToCell(brain.SearchGoal);
            float t2 = t + SaboteurBrain.LookAroundSeconds + 0.2f;
            brain.Tick(At(probeCell, gone, t2));
            AgentIntent back = default;
            for (float u = t2 + Frame; u < t2 + SaboteurBrain.LookAroundSeconds + 1f; u += Frame)
                back = brain.Tick(At(probeCell, gone, u));

            Assert.AreEqual("Patrol", back.DebugState, "The search is over.");
            Assert.IsFalse(brain.Awareness.HasLastSeen, "The sighting was searched; it is not offered again.");
        }

        [Test]
        public void APlayerNoiseIsInvestigatedEvenWithoutSight()
        {
            _sight.Visible = false;
            SaboteurBrain brain = Brain(SaboteurLetter.A, null, new Vector2Int(10, 10));
            WorldBlackboard world = PlayerAtCell(new Vector2Int(75, 75));
            Vector3 noiseAt = _grid.CellToWorld(new Vector2Int(55, 40));

            brain.Tick(At(_home, world, 0f, senses: new SensorSnapshot(noiseAt, 80f, SaboteurAwareness.PlayerNoiseSourceId, 0f)));
            AgentIntent intent = default;
            for (float t = Frame; t < 0.5f; t += Frame)
                intent = brain.Tick(At(_home, world, t));

            Assert.AreEqual(SaboteurActionKind.Search, brain.CurrentAction.Kind);
            Assert.AreEqual(noiseAt.x, brain.SearchGoal.x, 1e-3f);
            Assert.AreEqual(noiseAt.z, brain.SearchGoal.z, 1e-3f);
        }

        [Test]
        public void ALureOrAnAgentNoiseIsNotInvestigated()
        {
            _sight.Visible = false;
            SaboteurBrain brain = Brain(SaboteurLetter.A, null, new Vector2Int(10, 10));
            WorldBlackboard world = PlayerAtCell(new Vector2Int(75, 75));
            Vector3 noiseAt = _grid.CellToWorld(new Vector2Int(55, 40));

            brain.Tick(At(_home, world, 0f, senses: new SensorSnapshot(noiseAt, 80f, SaboteurAwareness.PlayerNoiseSourceId, 0f, noiseIsLure: true)));
            brain.Tick(At(_home, world, 0.1f, senses: new SensorSnapshot(noiseAt, 80f, 2, 0.1f)));
            for (float t = 0.2f; t < 0.6f; t += Frame)
                brain.Tick(At(_home, world, t));

            Assert.AreNotEqual(SaboteurActionKind.Search, brain.CurrentAction.Kind);
        }

        [Test]
        public void APlayerSeenOutOfAttackRangeIsApproached()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, null, new Vector2Int(10, 10));
            AgentIntent intent = brain.Tick(At(_home, PlayerAtCell(new Vector2Int(40, 62)), 0f));   // 11 m ahead

            Assert.AreEqual(SaboteurActionKind.Search, brain.CurrentAction.Kind);
            Assert.AreEqual("Approach", intent.DebugState);
            Assert.IsNotNull(intent.Path);
            Assert.Greater(intent.Path.Count, 0);
        }

        [Test]
        public void APlayerBehindAndOutOfTheAllRoundRangeIsNotNoticed()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, null, new Vector2Int(10, 10));
            AgentIntent intent = brain.Tick(At(_home, PlayerAtCell(new Vector2Int(40, 30)), 0f));   // 5 m behind

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsFalse(brain.Awareness.Sees);
        }

        [Test]
        public void ASearchThatCannotEndIsGivenUpAtTheTimeout()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, null, new Vector2Int(10, 10));
            brain.Tick(At(_home, PlayerAtCell(new Vector2Int(40, 60)), 0f));
            _sight.Visible = false;
            WorldBlackboard gone = PlayerAtCell(new Vector2Int(75, 75));

            // The body never moves (held, wedged): the search still ends.
            AgentIntent intent = default;
            for (float t = Frame; t < SaboteurBrain.SearchTimeoutSeconds + 1f; t += Frame)
                intent = brain.Tick(At(_home, gone, t));

            Assert.AreNotEqual("Search", intent.DebugState);
        }

        // ---- Attacking, fleeing and patrolling hand over steadily

        [Test]
        public void AnAttackUnderWayCarriesOnPastEightMetresButANewOneDoesNotStartThere()
        {
            var fresh = new AttackPlayerSource(_sight);
            var list = new List<ActionCandidate>();
            fresh.AddCandidates(At(_home, PlayerAtCell(new Vector2Int(40, 58)), 0f), Identity(SaboteurLetter.A), list);   // 9 m
            Assert.IsEmpty(list, "9 m: too far to start an attack.");

            var engaged = new AttackPlayerSource(_sight);
            engaged.AddCandidates(At(_home, PlayerAtCell(new Vector2Int(40, 50)), 0f), Identity(SaboteurLetter.A), list);   // 5 m
            Assert.AreEqual(1, list.Count);
            list.Clear();
            engaged.AddCandidates(At(_home, PlayerAtCell(new Vector2Int(40, 58)), 0.5f), Identity(SaboteurLetter.A), list);
            Assert.AreEqual(1, list.Count, "9 m, a moment later: the attack carries on.");
            list.Clear();
            engaged.AddCandidates(At(_home, PlayerAtCell(new Vector2Int(40, 62)), 1f), Identity(SaboteurLetter.A), list);
            Assert.IsEmpty(list, "11 m: past the keep range.");
        }

        [Test]
        public void AFlightStaysOnOfferForAMomentAfterThePlayerLooksAway()
        {
            var flee = new FleeSource(_sight);
            flee.OnHealthChanged(1, 4);
            var world = new WorldBlackboard();
            Vector3 player = _grid.CellToWorld(new Vector2Int(40, 50));
            world.SetPlayer(new PlayerSnapshot(true, new Vector2Int(40, 50), player, Vector3.zero, Vector3.back, 5f, true, 1f, 1f, false, 0f, 0f));
            var list = new List<ActionCandidate>();
            flee.AddCandidates(At(_home, world, 0f), Identity(SaboteurLetter.B), list);
            Assert.AreEqual(1, list.Count, "Aimed at, hurt, in sight.");
            float score = list[0].BaseScore;

            var away = new WorldBlackboard();
            away.SetPlayer(new PlayerSnapshot(true, new Vector2Int(40, 50), player, Vector3.zero, Vector3.forward, 5f, true, 1f, 1f, false, 0f, 0f));
            list.Clear();
            flee.AddCandidates(At(_home, away, 1f), Identity(SaboteurLetter.B), list);
            Assert.AreEqual(1, list.Count, "Looked away a second ago: still running.");
            Assert.AreEqual(score, list[0].BaseScore);

            list.Clear();
            flee.AddCandidates(At(_home, away, 1f + FleeSource.HoldSeconds + 0.1f), Identity(SaboteurLetter.B), list);
            Assert.IsEmpty(list, "Long enough: the flight is off.");
        }

        // ---- Patrolling the room

        [Test]
        public void TheRoomStopsAtItsDoors()
        {
            GridGraph grid = TwoRooms(out _);
            HashSet<Vector2Int> room = RoomPatrolPlanner.FloodRoom(grid, new Vector2Int(5, 5));

            Assert.IsTrue(room.Contains(new Vector2Int(18, 18)), "The far corner of the same room.");
            Assert.IsFalse(room.Contains(new Vector2Int(25, 5)), "The other room, through the door.");
            Assert.IsFalse(room.Contains(new Vector2Int(20, 10)), "The doorway itself.");
        }

        [Test]
        public void ThePatrolCoversTheRoomFromItsPatrolPoints()
        {
            GridGraph grid = TwoRooms(out _);
            Vector2Int[] plan = RoomPatrolPlanner.Plan(grid, new[] { new Vector2Int(5, 5), new Vector2Int(8, 5) }, null);

            Assert.GreaterOrEqual(plan.Length, RoomPatrolPlanner.MinPoints);
            Assert.LessOrEqual(plan.Length, RoomPatrolPlanner.MaxPoints);
            HashSet<Vector2Int> room = RoomPatrolPlanner.FloodRoom(grid, new Vector2Int(5, 5));
            foreach (Vector2Int p in plan)
                Assert.IsTrue(room.Contains(p), $"{p} is in the room.");
            Assert.AreEqual(new Vector2Int(5, 5), plan[0], "It starts from its own first point.");

            // Spread: every pair at least 3 m apart, and the points span most of the 20 x 20 room.
            for (int i = 0; i < plan.Length; i++)
                for (int j = i + 1; j < plan.Length; j++)
                    Assert.GreaterOrEqual(BaseCostModel.OctileDistance(plan[i], plan[j]), RoomPatrolPlanner.MinSpacingCells);
            int spanX = plan.Max(p => p.x) - plan.Min(p => p.x);
            int spanY = plan.Max(p => p.y) - plan.Min(p => p.y);
            Assert.GreaterOrEqual(spanX, 10);
            Assert.GreaterOrEqual(spanY, 10);

            CollectionAssert.AreEqual(plan, RoomPatrolPlanner.Plan(grid, new[] { new Vector2Int(5, 5), new Vector2Int(8, 5) }, null), "The same every time.");
        }

        [Test]
        public void TwoSaboteursInOneRoomShareItOut()
        {
            GridGraph grid = TwoRooms(out _);
            var homeA = new Vector2Int(3, 10);
            var homeD = new Vector2Int(16, 10);
            Vector2Int[] a = RoomPatrolPlanner.Plan(grid, new[] { homeA }, new[] { homeD });
            Vector2Int[] d = RoomPatrolPlanner.Plan(grid, new[] { homeD }, new[] { homeA });

            foreach (Vector2Int p in a)
                Assert.LessOrEqual(BaseCostModel.OctileDistance(p, homeA), BaseCostModel.OctileDistance(p, homeD), $"A's {p} is on A's side.");
            foreach (Vector2Int p in d)
                Assert.LessOrEqual(BaseCostModel.OctileDistance(p, homeD), BaseCostModel.OctileDistance(p, homeA), $"D's {p} is on D's side.");
        }

        [Test]
        public void AHomeInAnotherRoomDoesNotShrinkThisOne()
        {
            GridGraph grid = TwoRooms(out _);
            Vector2Int[] alone = RoomPatrolPlanner.Plan(grid, new[] { new Vector2Int(5, 5) }, null);
            Vector2Int[] withNeighbour = RoomPatrolPlanner.Plan(grid, new[] { new Vector2Int(5, 5) }, new[] { new Vector2Int(30, 10) });
            CollectionAssert.AreEqual(alone, withNeighbour);
        }

        [Test]
        public void TheBrainPatrolsThePlannedLoopAndGoesRoundIt()
        {
            _grid = TwoRooms(out _);
            SaboteurBrain brain = Brain(SaboteurLetter.C, SaboteurOptions.Default, new Vector2Int(5, 5), new Vector2Int(8, 5));
            var world = new WorldBlackboard();

            AgentIntent first = brain.Tick(At(new Vector2Int(5, 5), world, 0f));
            Assert.AreEqual("Patrol", first.DebugState);
            Assert.Greater(brain.PatrolCells.Count, 2, "More than the two given points.");

            // Stand on each point in turn: the next route goes to the next point, round the loop.
            Assert.IsNotNull(first.Path);
            Vector2Int at = _grid.WorldToCell(first.Path[first.Path.Count - 1]);
            var visited = new List<Vector2Int> { at };
            float t = Frame;
            for (int i = 0; i < brain.PatrolCells.Count + 1; i++)
            {
                AgentIntent intent = brain.Tick(At(at, world, t));
                t += Frame;
                if (intent.Path != null && intent.Path.Count > 0)
                {
                    at = _grid.WorldToCell(intent.Path[intent.Path.Count - 1]);
                    visited.Add(at);
                }
            }

            Assert.GreaterOrEqual(visited.Distinct().Count(), 3, "It walks to several points, not back and forth.");
        }

        [Test]
        public void ASaboteurShutOutOfItsRoomPatrolsTheRoomItIsInThenGoesHomeWhenTheDoorOpens()
        {
            _grid = TwoRooms(out int doorId);
            var door = new Vector2Int(20, 10);
            SaboteurBrain brain = Brain(SaboteurLetter.C, SaboteurOptions.Default, new Vector2Int(5, 5));
            var world = new WorldBlackboard();
            HashSet<Vector2Int> home = RoomPatrolPlanner.FloodRoom(_grid, new Vector2Int(5, 5));

            // It is in the other room (it chased the player there) and the door behind it is shut: agents cannot open doors.
            _grid.SetDoor(door, doorId, true);
            AgentIntent stranded = brain.Tick(At(new Vector2Int(30, 10), world, Frame));

            Assert.IsTrue(brain.IsStranded);
            Assert.AreEqual("Patrol", stranded.DebugState, "It patrols rather than standing still.");
            Assert.IsNotNull(stranded.Path);
            HashSet<Vector2Int> here = RoomPatrolPlanner.FloodRoom(_grid, new Vector2Int(30, 10));
            foreach (Vector2Int p in brain.PatrolCells)
                Assert.IsTrue(here.Contains(p), $"{p} is in the room it stands in.");

            // The player opens the door: back to its own loop.
            _grid.SetDoor(door, doorId, false);
            brain.OnGraphChanged(new[] { door });
            AgentIntent back = brain.Tick(At(new Vector2Int(30, 10), world, 2 * Frame));

            Assert.IsFalse(brain.IsStranded);
            Assert.IsNotNull(back.Path);
            Assert.IsTrue(home.Contains(_grid.WorldToCell(back.Path[back.Path.Count - 1])), "Headed back to its own room.");
        }

        [Test]
        public void WithTheGivenWaypointsOnlyAShutOutSaboteurStillHolds()
        {
            _grid = TwoRooms(out int doorId);
            _grid.SetDoor(new Vector2Int(20, 10), doorId, true);
            SaboteurBrain brain = Brain(SaboteurLetter.C, SaboteurOptions.WaypointsOnly, new Vector2Int(5, 5), new Vector2Int(8, 5));

            AgentIntent intent = brain.Tick(At(new Vector2Int(30, 10), new WorldBlackboard(), 0f));

            Assert.AreEqual("Hold", intent.DebugState);
            Assert.IsFalse(brain.IsStranded);
        }

        [Test]
        public void WaypointsOnlyKeepsTheGivenPoints()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.C, SaboteurOptions.WaypointsOnly, new Vector2Int(5, 5), new Vector2Int(8, 5));
            brain.Tick(At(new Vector2Int(20, 20), new WorldBlackboard(), 0f));
            CollectionAssert.AreEqual(new[] { new Vector2Int(5, 5), new Vector2Int(8, 5) }, brain.PatrolCells);
        }

        // ---- Progress along the route

        [Test]
        public void ProgressIsMeasuredAlongTheRouteNotInAStraightLine()
        {
            // A U-shaped route: out east, round and back west, ending near the start.
            var route = new List<Vector2Int>();
            for (int x = 10; x <= 20; x++) route.Add(new Vector2Int(x, 10));
            for (int y = 11; y <= 14; y++) route.Add(new Vector2Int(20, y));
            for (int x = 19; x >= 10; x--) route.Add(new Vector2Int(x, 14));

            int index = 0;
            float atStart = SaboteurPaths.RemainingMetres(_grid, route, _grid.CellToWorld(new Vector2Int(10, 10)), ref index);
            float atTurn = SaboteurPaths.RemainingMetres(_grid, route, _grid.CellToWorld(new Vector2Int(20, 12)), ref index);

            Vector3 end = _grid.CellToWorld(route[route.Count - 1]);
            float straightStart = (end - _grid.CellToWorld(new Vector2Int(10, 10))).magnitude;
            float straightTurn = (end - _grid.CellToWorld(new Vector2Int(20, 12))).magnitude;

            Assert.Greater(straightTurn, straightStart, "In a straight line, the turn looks like going backwards.");
            Assert.Less(atTurn, atStart, "Along the route it is progress.");
        }

        [Test]
        public void APatrollingBodyThatMakesNoProgressRoutesAgainThenMovesOnToTheNextPoint()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.C, SaboteurOptions.WaypointsOnly, new Vector2Int(60, 40), new Vector2Int(40, 60));
            var world = new WorldBlackboard();
            AgentIntent first = brain.Tick(At(_home, world, 0f));
            Assert.AreEqual(new Vector2Int(60, 40), _grid.WorldToCell(first.Path[first.Path.Count - 1]));

            // The body never moves.
            var routes = new List<AgentIntent>();
            for (float t = Frame; t < SaboteurBrain.TravelStuckSeconds * 2f + 1f; t += Frame)
            {
                AgentIntent intent = brain.Tick(At(_home, world, t));
                if (intent.Path != null && intent.Path.Count > 0)
                    routes.Add(intent);
            }

            Assert.GreaterOrEqual(routes.Count, 2);
            Assert.AreEqual(new Vector2Int(60, 40), _grid.WorldToCell(routes[0].Path[routes[0].Path.Count - 1]), "Sent again to the same point.");
            Assert.AreEqual(new Vector2Int(40, 60), _grid.WorldToCell(routes[1].Path[routes[1].Path.Count - 1]), "Then on to the next.");
        }

        // ---- The squad shares failures

        [Test]
        public void ADoorOneSaboteurFailedAtIsLeftAloneByTheWholeSquad()
        {
            var squad = SquadCoordinator.For(_claims);
            Brain(SaboteurLetter.A, null, new Vector2Int(10, 10));
            var door = new ActionKey(SaboteurActionKind.CloseDoor, 1);

            squad.NoteFailure(door, 5f + SaboteurBrain.SquadFailureSeconds);
            Assert.IsTrue(squad.IsFailing(door, 6f));
            Assert.IsFalse(squad.IsFailing(door, 5f + SaboteurBrain.SquadFailureSeconds), "Over once the time is up.");
            Assert.IsFalse(squad.IsFailing(new ActionKey(SaboteurActionKind.CloseDoor, 2), 6f), "Only that door.");

            squad.NoteFailure(new ActionKey(SaboteurActionKind.AttackPlayer), 100f);
            Assert.IsFalse(squad.IsFailing(new ActionKey(SaboteurActionKind.AttackPlayer), 6f), "Untargeted actions are not shared.");
        }

        [Test]
        public void AFailureReportedToOneBrainVetoesTheTargetForItsSquadMate()
        {
            var cacheWorld = new WorldBlackboard();
            var fixedDoor = new ActionKey(SaboteurActionKind.CloseDoor, 1);
            var a = new SaboteurBrain(Identity(SaboteurLetter.A), _grid, new AStarSearch(_grid), _claims, new List<Vector3>(), null, 0,
                new FixedSource(fixedDoor, 0.8f), null, SaboteurOptions.WaypointsOnly);
            var b = new SaboteurBrain(Identity(SaboteurLetter.B), _grid, new AStarSearch(_grid), _claims, new List<Vector3>(), null, 0,
                new FixedSource(fixedDoor, 0.8f), null, SaboteurOptions.WaypointsOnly);

            a.Tick(At(_home, cacheWorld, 0f));
            Assert.AreEqual(fixedDoor, a.CurrentAction);
            a.OnActionResolved(AgentAction.CloseDoor, 1, false);
            a.Tick(At(_home, cacheWorld, 0.25f));   // A's next decision drops the door (its own cooldown) and its claim

            // B's first decision falls 1/16 s after its first tick.
            b.Tick(At(_home, cacheWorld, 1f));
            b.Tick(At(_home, cacheWorld, 1.1f));
            Assert.AreNotEqual(fixedDoor, b.CurrentAction, "B does not walk to the door A just failed at.");

            b.Tick(At(_home, cacheWorld, 1f + SaboteurBrain.SquadFailureSeconds + 1f));
            Assert.AreEqual(fixedDoor, b.CurrentAction, "After the squad's cooldown it may try.");
        }

        sealed class FixedSource : ICandidateSource
        {
            readonly ActionKey _key;
            readonly float _score;

            public FixedSource(ActionKey key, float score)
            {
                _key = key;
                _score = score;
            }

            public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates) =>
                candidates.Add(new ActionCandidate(_key, _score));
        }

        // Two 20 x 20 rooms side by side, joined by one door in the middle of the wall at x = 20.
        static GridGraph TwoRooms(out int doorId)
        {
            doorId = 1;
            var grid = new GridGraph(41, 21, Vector3.zero);
            using (GridGraph.Batch batch = grid.BeginBatch())
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    if (y == 10)
                        batch.SetDoor(new Vector2Int(20, y), doorId, false);
                    else
                        batch.SetWalkable(new Vector2Int(20, y), false);
                }

                batch.Commit();
            }

            return grid;
        }
    }
}
