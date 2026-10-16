using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// Two doors that are each safe to close alone can be a lockout together, like the two entrances of
    /// one room, and the detour cache can be half a second old. The brain therefore asks
    /// <see cref="DetourCache.WouldLockPlayerOut"/> once, just before it sends the close request. The map: a
    /// wall at x = 20 with two doors, door 1 at y = 5 and door 2 at y = 15, so the player at (5, 5) and the
    /// objective at (35, 5) always have a way through, unless both doors are shut.
    /// </summary>
    public class SaboteurDoorSafetyTests
    {
        static readonly Vector2Int Door1Cell = new Vector2Int(20, 5);
        static readonly Vector2Int Door2Cell = new Vector2Int(20, 15);
        static readonly Vector2Int PlayerCell = new Vector2Int(5, 5);
        static readonly Vector2Int ObjectiveCell = new Vector2Int(35, 5);

        // Exactly representable, so slots and timers line up with whole frames.
        const float Frame = 1f / 64f;

        GridGraph _grid;
        AStarSearch _pathfinder;
        WorldBlackboard _world;
        TargetClaims _claims;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(40, 20, Vector3.zero);
            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                for (int y = 0; y < _grid.Height; y++)
                {
                    if (y != Door1Cell.y && y != Door2Cell.y)
                        batch.SetWalkable(new Vector2Int(20, y), false);
                }
                batch.SetDoor(Door1Cell, 1, false);
                batch.SetDoor(Door2Cell, 2, false);
                batch.Commit();
            }

            _pathfinder = new AStarSearch(_grid);
            _claims = new TargetClaims();
            _world = new WorldBlackboard();
            SetPlayer(PlayerCell);
            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, ObjectiveCell, ObjectiveTargetKind.Task) });
        }

        void SetPlayer(Vector2Int cell, bool alive = true, bool known = true) =>
            _world.SetPlayer(new PlayerSnapshot(known, cell, _grid.CellToWorld(cell), Vector3.zero, Vector3.right,
                5f, alive, 1f, 1f, false, 0f, 0f));

        DetourCache Cache() => new DetourCache(_grid, _pathfinder);

        SaboteurBrain Brain(SaboteurLetter letter, DetourCache cache) =>
            new SaboteurBrain(new SaboteurIdentity((int)letter + 1, letter), _grid, _pathfinder, _claims,
                new List<Vector3> { _grid.CellToWorld(new Vector2Int(2, 2)) }, null, 0, new CloseDoorSource(cache), cache);

        AgentContext At(Vector2Int cell, float time) =>
            new AgentContext(cell, _grid.CellToWorld(cell), Vector3.forward, time, _world, default);

        // ---- The check itself

        [Test]
        public void WithBothDoorsOpenNeitherIsALockout()
        {
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            Assert.IsFalse(cache.WouldLockPlayerOut(_world, 1));
            Assert.IsFalse(cache.WouldLockPlayerOut(_world, 2));
        }

        [Test]
        public void WithTheOtherDoorShutTheLastOneIsALockout()
        {
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);
            _grid.SetDoor(Door2Cell, 2, true);

            Assert.IsTrue(cache.WouldLockPlayerOut(_world, 1), "Door 1 is now the only way through.");
            Assert.IsFalse(cache.WouldLockPlayerOut(_world, 2), "Door 2 is already shut: nothing to lock.");
        }

        [Test]
        public void TheCheckIsFreshWhileTheCachedResultIsStale()
        {
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);
            Assert.IsTrue(cache.TryGet(1, out DoorDetour cached));
            Assert.IsFalse(cached.IsLockout);

            _grid.SetDoor(Door2Cell, 2, true);

            Assert.IsTrue(cache.TryGet(1, out DoorDetour stale));
            Assert.IsFalse(stale.IsLockout, "The cached result is from before door 2 shut (at most half a second old).");
            Assert.IsTrue(cache.WouldLockPlayerOut(_world, 1), "The fresh check sees it.");
        }

        [Test]
        public void TheCheckNeverChangesTheGridAndCostsOneSearchCountedApartFromTheRefreshes()
        {
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);
            int version = _grid.Version;
            int searches = cache.Searches;

            cache.WouldLockPlayerOut(_world, 1);

            Assert.AreEqual(version, _grid.Version);
            Assert.IsFalse(_grid.GetNode(Door1Cell).IsDoorClosed);
            Assert.AreEqual(searches, cache.Searches, "Refresh searches are unchanged.");
            Assert.AreEqual(1, cache.LockoutChecks);
        }

        [Test]
        public void NothingToCompareIsNotALockout()
        {
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);
            _grid.SetDoor(Door2Cell, 2, true);

            Assert.IsFalse(cache.WouldLockPlayerOut(_world, 99), "An unknown door.");

            SetPlayer(PlayerCell, alive: false);
            Assert.IsFalse(cache.WouldLockPlayerOut(_world, 1), "A dead player.");

            SetPlayer(PlayerCell, known: false);
            Assert.IsFalse(cache.WouldLockPlayerOut(_world, 1), "An unknown player.");

            SetPlayer(PlayerCell);
            _world.SetObjectiveTargets(new ObjectiveTarget[0]);
            Assert.IsFalse(cache.WouldLockPlayerOut(_world, 1), "No objective.");

            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, PlayerCell, ObjectiveTargetKind.Task) });
            Assert.IsFalse(cache.WouldLockPlayerOut(_world, 1), "The player is already at the objective.");

            Assert.Throws<System.ArgumentNullException>(() => cache.WouldLockPlayerOut(null, 1));
        }

        // ---- The brain

        [Test]
        public void SaboteurAsksToCloseTheDoorWhenTheOtherEntranceIsStillOpen()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache);
            Assert.AreEqual("CloseDoor", brain.Tick(At(new Vector2Int(10, 5), 0f)).DebugState);

            AgentIntent intent = brain.Tick(At(new Vector2Int(19, 5), Frame));

            Assert.AreEqual(AgentAction.CloseDoor, intent.Action);
            Assert.AreEqual(1, intent.ActionTargetId);
        }

        [Test]
        public void SaboteurDoesNotShutTheLastEntranceAnotherSaboteurJustClosedTheOther()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache);
            Assert.AreEqual("CloseDoor", brain.Tick(At(new Vector2Int(10, 5), 0f)).DebugState);
            Assert.AreEqual(1, _claims.ClaimedBy(1));

            // While it walks, another Saboteur shuts door 2. The next tick is not a decision slot, so the
            // brain's cache and its plan still say door 1 is a good door.
            _grid.SetDoor(Door2Cell, 2, true);
            AgentIntent intent = brain.Tick(At(new Vector2Int(19, 5), Frame));

            Assert.AreNotEqual(AgentAction.CloseDoor, intent.Action, "Shutting door 1 now would lock the player out.");
            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNull(_claims.ClaimedBy(1), "The claim is released.");
            Assert.IsFalse(_grid.GetNode(Door1Cell).IsDoorClosed);
        }

        [Test]
        public void ARefusedDoorStartsTheFailureCooldownSoItIsNotTriedAgainAtOnce()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache);
            brain.Tick(At(new Vector2Int(10, 5), 0f));
            _grid.SetDoor(Door2Cell, 2, true);
            brain.Tick(At(new Vector2Int(19, 5), Frame));

            for (float t = 0.25f; t < 5f; t += 0.25f)
            {
                AgentIntent intent = brain.Tick(At(new Vector2Int(19, 5), t));
                Assert.AreNotEqual(AgentAction.CloseDoor, intent.Action, $"At {t:F2}.");
            }
        }

        [Test]
        public void TwoSaboteursOnTwoDoorsNeverSealThePlayerIn()
        {
            // The player's route flips between the doors, so each Saboteur can be sent at a different door.
            DetourCache cache = Cache();
            SaboteurBrain a = Brain(SaboteurLetter.A, cache);
            SaboteurBrain b = Brain(SaboteurLetter.B, cache);
            Assert.AreEqual("CloseDoor", a.Tick(At(new Vector2Int(10, 5), 0f)).DebugState);   // A: door 1
            a.Tick(At(new Vector2Int(12, 5), 0.25f));   // A's next decision is now at 0.5

            SetPlayer(new Vector2Int(5, 15));
            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, new Vector2Int(35, 15), ObjectiveTargetKind.Task) });
            b.Tick(At(new Vector2Int(10, 15), 0.3f));
            Assert.AreEqual("CloseDoor", b.Tick(At(new Vector2Int(10, 15), 0.3f + SquadCoordinator.StaggerStep)).DebugState);   // B: door 2

            // Both reach their door in the same instant, before A decides again, so it still holds its plan:
            // the first shuts, the second must refuse.
            AgentIntent first = a.Tick(At(new Vector2Int(19, 5), 0.4f));
            Assert.AreEqual(AgentAction.CloseDoor, first.Action);
            _grid.SetDoor(Door1Cell, 1, true);   // the controller carried A's request out
            AgentIntent second = b.Tick(At(new Vector2Int(19, 15), 0.4f + Frame));

            Assert.AreNotEqual(AgentAction.CloseDoor, second.Action, "The second door would seal the wall.");
            Assert.IsFalse(_grid.GetNode(Door2Cell).IsDoorClosed);
        }

        // ---- A walk that gets nowhere

        [Test]
        public void ASaboteurThatMakesNoProgressTowardTheDoorSendsItsRouteAgainThenGivesUp()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache);
            Assert.AreEqual("CloseDoor", brain.Tick(At(new Vector2Int(10, 5), 0f)).DebugState);
            Assert.AreEqual(1, _claims.ClaimedBy(1));

            // The body does not move (shoved aside, wedged against something the grid does not know about).
            AgentIntent intent = default;
            bool sentAgain = false;
            float gaveUpAt = -1f;
            for (float t = 0.5f; t <= 12f; t += 0.5f)
            {
                intent = brain.Tick(At(new Vector2Int(10, 5), t));
                if (intent.DebugState == "CloseDoor" && intent.Path != null)
                {
                    Assert.IsFalse(sentAgain, "The route is sent again only once.");
                    Assert.GreaterOrEqual(t, SaboteurBrain.TravelStuckSeconds, "Not before it has been stuck long enough.");
                    sentAgain = true;
                }

                if (intent.DebugState != "CloseDoor" && gaveUpAt < 0f)
                    gaveUpAt = t;
            }

            Assert.IsTrue(sentAgain, "Stuck for 4 s, the route is sent again from where the body is.");
            Assert.Greater(gaveUpAt, SaboteurBrain.TravelStuckSeconds, "It gave up only after the second stuck period.");
            Assert.LessOrEqual(gaveUpAt, 2 * SaboteurBrain.TravelStuckSeconds + 1f);
            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNull(_claims.ClaimedBy(1), "The claim is released.");
        }

        [Test]
        public void ASaboteurGivingUpOnTheWalkIsNotSentBackAtOnce()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache);
            brain.Tick(At(new Vector2Int(10, 5), 0f));
            float t = 0.5f;
            for (; t <= 9f; t += 0.5f)
                brain.Tick(At(new Vector2Int(10, 5), t));

            for (float later = t; later < t + 6f; later += 0.25f)
            {
                AgentIntent intent = brain.Tick(At(new Vector2Int(10, 5), later));
                Assert.AreNotEqual("CloseDoor", intent.DebugState, $"The failure cooldown holds at {later:F2}.");
            }
        }

        [Test]
        public void ASaboteurThatKeepsGettingCloserIsNeverSentAgainOrGivenUp()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache);
            Assert.AreEqual("CloseDoor", brain.Tick(At(new Vector2Int(2, 5), 0f)).DebugState);

            // Two cells (1 m) a second, for 8 s: slower than it can walk, but always getting closer.
            for (int second = 1; second <= 8; second++)
            {
                AgentIntent intent = brain.Tick(At(new Vector2Int(2 + 2 * second, 5), second));
                if (second < 8)
                {
                    Assert.AreEqual("CloseDoor", intent.DebugState, $"Second {second}.");
                    Assert.IsNull(intent.Path, $"Second {second}: the route is not sent again.");
                }
            }
        }

        [Test]
        public void ThePauseOfACutsceneNeverCountsAsBeingStuck()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache);
            brain.Tick(At(new Vector2Int(10, 5), 100f));

            // Nothing ticks during a cutscene and game time stands still, so the next tick carries on from 100.5.
            AgentIntent after = brain.Tick(At(new Vector2Int(10, 5), 100.5f));

            Assert.AreEqual("CloseDoor", after.DebugState);
            Assert.IsNull(after.Path, "No new route: nothing has been lost.");
        }
    }
}
