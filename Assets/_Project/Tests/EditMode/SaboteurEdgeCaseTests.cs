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
    /// The Saboteur's edge cases (plan C3): a door closed or occupied under the plan, scores that all lose
    /// to Idle, a missing or dead player, a claim holder or the whole squad destroyed, and a pause in the
    /// middle of an action. The map is the one in <see cref="SaboteurDetourTests"/>: a wall at x = 20 with
    /// door 1 at y = 10, on the line between the player (5, 10) and the objective (35, 10), and door 2 at
    /// y = 0 off it. Trap 11 lies at (30, 10) on the player's route, beyond the wall.
    /// </summary>
    public class SaboteurEdgeCaseTests
    {
        sealed class FixedSource : ICandidateSource
        {
            readonly ActionCandidate[] _candidates;
            public FixedSource(params ActionCandidate[] candidates) { _candidates = candidates; }

            public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates)
            {
                candidates.AddRange(_candidates);
            }
        }

        sealed class AlwaysSight : IPlayerSight
        {
            public bool CanSeeCell(Vector2Int cell) => true;
        }

        static readonly Vector2Int Door1Cell = new Vector2Int(20, 10);
        static readonly Vector2Int PlayerCell = new Vector2Int(5, 10);
        static readonly Vector2Int ObjectiveCell = new Vector2Int(35, 10);
        static readonly ActionKey Door1 = new ActionKey(SaboteurActionKind.CloseDoor, 1);
        static readonly ActionKey Trap11 = new ActionKey(SaboteurActionKind.ArmTrap, 11);

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
                    if (y != Door1Cell.y && y != 0)
                        batch.SetWalkable(new Vector2Int(20, y), false);
                }
                batch.SetDoor(Door1Cell, 1, false);
                batch.SetDoor(new Vector2Int(20, 0), 2, false);
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

        void SetTrap(int id, int x, int y, bool available = true) =>
            _world.SetSabotageFacts(new[] { new SabotageFact(id, new Vector2Int(x, y), SabotageFactKind.Trap, available) });

        DetourCache Cache() => new DetourCache(_grid, _pathfinder);

        ICandidateSource EverySource(DetourCache cache) => new CompositeCandidateSource(
            new AttackPlayerSource(new AlwaysSight()),
            new FleeSource(new AlwaysSight()),
            new CloseDoorSource(cache),
            new ArmTrapSource(_grid, cache),
            new StealBatterySource(_grid));

        SaboteurBrain Brain(SaboteurLetter letter, ICandidateSource source, DetourCache cache = null) =>
            new SaboteurBrain(new SaboteurIdentity((int)letter + 1, letter), _grid, _pathfinder, _claims,
                new List<Vector3> { _grid.CellToWorld(new Vector2Int(2, 2)) }, null, 0, source, cache);

        AgentContext At(Vector2Int cell, float time, bool noWorld = false) =>
            new AgentContext(cell, _grid.CellToWorld(cell), Vector3.forward, time, noWorld ? null : _world, default);

        // ---- A door closed or occupied under the plan

        [Test]
        public void DoorClosedBySomeoneElseBeforeTheSaboteurGetsThereEndsThePlanWithNoCooldown()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, new CloseDoorSource(cache), cache);
            Assert.AreEqual("CloseDoor", brain.Tick(At(new Vector2Int(10, 10), 0f)).DebugState);
            Assert.AreEqual(1, _claims.ClaimedBy(1));

            _grid.SetDoor(Door1Cell, 1, true);
            AgentIntent intent = brain.Tick(At(new Vector2Int(11, 10), Frame));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.AreEqual(AgentAction.None, intent.Action);
            Assert.IsNull(_claims.ClaimedBy(1), "The claim is released.");

            // The door opens again: nobody asked for the close, so there is no cooldown to wait out.
            _grid.SetDoor(Door1Cell, 1, false);
            brain.Tick(At(new Vector2Int(11, 10), 0.5f));
            Assert.AreEqual(Door1, brain.CurrentAction);
        }

        [Test]
        public void PlayerStepsIntoTheDoorwayWhileTheSaboteurIsAskingToCloseItAndTheCloseIsWithdrawn()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, new CloseDoorSource(cache), cache);
            Assert.AreEqual(AgentAction.CloseDoor, brain.Tick(At(new Vector2Int(19, 10), 0f)).Action);

            // Between two decisions: the door must not shut on the player.
            SetPlayer(new Vector2Int(21, 10));
            AgentIntent intent = brain.Tick(At(new Vector2Int(19, 10), Frame));

            Assert.AreNotEqual(AgentAction.CloseDoor, intent.Action, "A player in the doorway is never shut in.");
            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNull(_claims.ClaimedBy(1), "The claim is released.");
        }

        [Test]
        public void PlayerEnteringTheDoorwayWhileTheSaboteurIsStillWalkingReleasesTheClaim()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, new CloseDoorSource(cache), cache);
            Assert.AreEqual("CloseDoor", brain.Tick(At(new Vector2Int(10, 10), 0f)).DebugState);

            SetPlayer(new Vector2Int(19, 10));
            AgentIntent intent = brain.Tick(At(new Vector2Int(12, 10), Frame));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNull(_claims.ClaimedBy(1));
        }

        [Test]
        public void DoorIsOfferedAgainOnceThePlayerLeavesTheDoorwayAndNoCooldownWasStarted()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, new CloseDoorSource(cache), cache);
            brain.Tick(At(new Vector2Int(19, 10), 0f));
            SetPlayer(new Vector2Int(21, 10));
            brain.Tick(At(new Vector2Int(19, 10), Frame));

            SetPlayer(PlayerCell);
            brain.Tick(At(new Vector2Int(19, 10), 0.75f));

            Assert.AreEqual(Door1, brain.CurrentAction);
        }

        [Test]
        public void PlayerDyingDuringTheDoorPlanEndsItOnceTheRouteIsKnownToBeGone()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, new CloseDoorSource(cache), cache);
            Assert.AreEqual(AgentAction.CloseDoor, brain.Tick(At(new Vector2Int(19, 10), 0f)).Action);

            SetPlayer(PlayerCell, alive: false);
            AgentIntent intent = default;
            for (float t = 0.25f; t <= 1f; t += 0.25f)
                intent = brain.Tick(At(new Vector2Int(19, 10), t));

            Assert.AreNotEqual(AgentAction.CloseDoor, intent.Action, "The detour cache stops offering doors once the player is dead.");
            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNull(_claims.ClaimedBy(1));
        }

        // ---- Scores that lose to Idle

        [Test]
        public void EveryCandidateBelowIdleLeavesTheSaboteurPatrollingWithNoClaim()
        {
            var source = new FixedSource(
                new ActionCandidate(Trap11, 0.05f),
                new ActionCandidate(new ActionKey(SaboteurActionKind.StealBattery, 21), 0.09f),
                new ActionCandidate(Door1, 0.02f));
            SaboteurBrain brain = Brain(SaboteurLetter.A, source);

            AgentIntent intent = brain.Tick(At(new Vector2Int(10, 10), 0f));

            Assert.AreEqual(SaboteurActionKind.Idle, brain.CurrentAction.Kind, "Idle scores 0.1 and beats all three.");
            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.AreEqual(AgentAction.None, intent.Action);
            Assert.IsNull(_claims.ClaimedBy(11));
            Assert.IsNull(_claims.ClaimedBy(21));
            Assert.IsNull(_claims.ClaimedBy(1));
        }

        [Test]
        public void ACandidateJustAboveIdleStillWins()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, new FixedSource(new ActionCandidate(Trap11, 0.11f)));
            SetTrap(11, 30, 10);

            brain.Tick(At(new Vector2Int(28, 12), 0f));

            Assert.AreEqual(Trap11, brain.CurrentAction);
        }

        // ---- A missing, unknown or dead player

        [Test]
        public void WithoutABlackboardEverySourceIsSilentAndTheSaboteurPatrols()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, EverySource(cache), cache);

            AgentIntent intent = default;
            for (float t = 0f; t < 2f; t += 0.25f)
                intent = brain.Tick(At(new Vector2Int(10, 10), t, noWorld: true));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.AreEqual(SaboteurActionKind.Idle, brain.CurrentAction.Kind);
        }

        [Test]
        public void AnUnknownPlayerMakesEveryPlayerDependentActionIneligible()
        {
            SetPlayer(PlayerCell, known: false);
            SetTrap(11, 30, 10);
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, EverySource(cache), cache);

            AgentIntent intent = default;
            for (float t = 0f; t < 2f; t += 0.25f)
                intent = brain.Tick(At(new Vector2Int(28, 12), t));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.AreEqual(SaboteurActionKind.Idle, brain.CurrentAction.Kind);
        }

        [Test]
        public void PlayerDyingDuringATrapWalkEndsThePlanAtTheNextDecision()
        {
            SetTrap(11, 30, 10);
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, EverySource(cache), cache);
            Assert.AreEqual("ArmTrap", brain.Tick(At(new Vector2Int(26, 12), 0f)).DebugState);
            Assert.IsNotNull(_claims.ClaimedBy(11), "The trap is claimed.");

            SetPlayer(PlayerCell, alive: false);
            AgentIntent intent = brain.Tick(At(new Vector2Int(27, 12), 0.25f));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNull(_claims.ClaimedBy(11));
        }

        [Test]
        public void ADeadOrUnknownPlayerOffersNoDoorEvenFromAStaleCache()
        {
            DetourCache cache = Cache();
            var source = new CloseDoorSource(cache);
            var offered = new List<ActionCandidate>();
            source.AddCandidates(At(new Vector2Int(12, 10), 0f), new SaboteurIdentity(1, SaboteurLetter.A), offered);
            Assert.AreEqual(1, offered.Count, "The door is offered while the player lives.");

            // 0.1 s later the cache has not refreshed, but the player is gone.
            SetPlayer(PlayerCell, alive: false);
            offered.Clear();
            source.AddCandidates(At(new Vector2Int(12, 10), 0.1f), new SaboteurIdentity(1, SaboteurLetter.A), offered);
            Assert.IsEmpty(offered, "Dead player.");

            SetPlayer(PlayerCell, known: false);
            source.AddCandidates(At(new Vector2Int(12, 10), 0.1f), new SaboteurIdentity(1, SaboteurLetter.A), offered);
            Assert.IsEmpty(offered, "Unknown player.");
        }

        // ---- A claim holder destroyed

        [Test]
        public void ClaimHolderDestroyedOnTheWayFreesTheTrapForTheNextInstance()
        {
            SetTrap(11, 30, 10);
            DetourCache cache = Cache();
            SaboteurBrain a = Brain(SaboteurLetter.A, new ArmTrapSource(_grid, cache), cache);
            SaboteurBrain b = Brain(SaboteurLetter.B, new ArmTrapSource(_grid, cache), cache);

            a.Tick(At(new Vector2Int(26, 12), 0f));
            b.Tick(At(new Vector2Int(26, 13), 0f));
            b.Tick(At(new Vector2Int(26, 13), SquadCoordinator.StaggerStep));
            Assert.AreEqual(Trap11, a.CurrentAction);
            Assert.AreNotEqual(Trap11, b.CurrentAction, "A holds the claim.");

            a.OnDestroyed();
            Assert.IsNull(_claims.ClaimedBy(11), "Destruction releases the claim at once.");

            for (float t = 0.5f; t < 1.5f; t += SquadCoordinator.StaggerStep)
                b.Tick(At(new Vector2Int(26, 13), t));
            Assert.AreEqual(Trap11, b.CurrentAction, "B takes the free trap at its next decision.");
        }

        // ---- The whole squad destroyed

        [Test]
        public void AllFourDestroyedReleasesEveryClaimStopsTheCacheAndOnlyAStopIsEverReturned()
        {
            SetTrap(11, 30, 10);
            DetourCache cache = Cache();
            var letters = new[] { SaboteurLetter.A, SaboteurLetter.B, SaboteurLetter.C, SaboteurLetter.D };
            var brains = new SaboteurBrain[4];
            for (int i = 0; i < 4; i++)
                brains[i] = Brain(letters[i], EverySource(cache), cache);

            for (float t = 0f; t < 1f; t += SquadCoordinator.StaggerStep)
            {
                for (int i = 0; i < 4; i++)
                    brains[i].Tick(At(new Vector2Int(10 + 2 * i, 10), t));
            }

            Assert.IsNotNull(_claims.ClaimedBy(1) ?? _claims.ClaimedBy(11), "The squad was holding something.");

            foreach (SaboteurBrain brain in brains)
                brain.OnDestroyed();

            Assert.IsNull(_claims.ClaimedBy(1));
            Assert.IsNull(_claims.ClaimedBy(11));
            int searches = cache.Searches;
            foreach (SaboteurBrain brain in brains)
            {
                AgentIntent intent = brain.Tick(At(new Vector2Int(10, 10), 5f));
                Assert.AreEqual("Destroyed", intent.DebugState);
                Assert.AreEqual(AgentAction.None, intent.Action);
                Assert.AreEqual(0, intent.Path.Count);
                Assert.AreEqual(0f, intent.DesiredSpeed);
            }

            Assert.AreEqual(searches, cache.Searches, "No destroyed instance asks the detour cache for anything.");

            var drops = new List<ItemDrop>();
            Assert.AreEqual(1, brains[0].GetDrops(drops), "Saboteur A drops the keycard, once.");
            Assert.AreEqual(ItemDropKind.Keycard, drops[0].Kind);
            Assert.AreEqual(0, brains[1].GetDrops(drops) + brains[2].GetDrops(drops) + brains[3].GetDrops(drops));
        }

        [Test]
        public void DestroyingTwiceOrStunningAfterwardsChangesNothing()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, EverySource(cache), cache);
            brain.Tick(At(new Vector2Int(12, 10), 0f));
            brain.OnDestroyed();
            var first = new List<ItemDrop>();
            brain.GetDrops(first);

            brain.OnDestroyed();
            brain.OnStunned(2f);
            brain.OnActionResolved(AgentAction.CloseDoor, 1, true);
            brain.OnGraphChanged(new[] { new Vector2Int(15, 10) });
            var second = new List<ItemDrop>();
            brain.GetDrops(second);

            Assert.AreEqual(first[0].Cell, second[0].Cell, "The drop is fixed at the first destruction.");
            Assert.AreEqual("Destroyed", brain.Tick(At(new Vector2Int(12, 10), 1f)).DebugState);
        }

        // ---- A pause in the middle of an action

        [Test]
        public void ARequestInReachSurvivesAPauseBecauseTimersFollowGameTime()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, new CloseDoorSource(cache), cache);
            Assert.AreEqual(AgentAction.CloseDoor, brain.Tick(At(new Vector2Int(19, 10), 10f)).Action);

            // A cutscene: nothing ticks, and game time does not advance, so the next tick carries on at 10.1.
            AgentIntent resumed = brain.Tick(At(new Vector2Int(19, 10), 10.1f));
            Assert.AreEqual(AgentAction.CloseDoor, resumed.Action, "Not timed out by the pause.");
            Assert.AreEqual(1, _claims.ClaimedBy(1));

            // Only game time running past the timeout gives the request up.
            AgentIntent late = brain.Tick(At(new Vector2Int(19, 10), 10f + SaboteurBrain.CloseDoorTimeoutSeconds + 0.5f));
            Assert.AreNotEqual(AgentAction.CloseDoor, late.Action);
        }

        [Test]
        public void TickingAgainAtTheSameGameTimeNeitherSwipesTwiceNorDecidesTwice()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, new AttackPlayerSource(new AlwaysSight()));
            SetPlayer(new Vector2Int(11, 10));
            AgentContext ctx = At(new Vector2Int(10, 10), 3f);

            Assert.AreEqual(AgentAction.Shoot, brain.Tick(ctx).Action);
            AgentIntent again = brain.Tick(ctx);

            Assert.AreEqual(AgentAction.None, again.Action, "The swipe is paced by game time, which has not moved.");
            Assert.AreEqual(SaboteurActionKind.AttackPlayer, brain.CurrentAction.Kind);
        }

        [Test]
        public void AttackPlanSurvivesAPauseAndResumesTheSwipePacingFromTheSameValues()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, new AttackPlayerSource(new AlwaysSight()));
            SetPlayer(new Vector2Int(11, 10));
            Assert.AreEqual(AgentAction.Shoot, brain.Tick(At(new Vector2Int(10, 10), 3f)).Action);

            // Paused for however long: the next tick is still 0.5 s of game time after the swipe.
            Assert.AreEqual(AgentAction.None, brain.Tick(At(new Vector2Int(10, 10), 3.5f)).Action);
            Assert.AreEqual(AgentAction.Shoot, brain.Tick(At(new Vector2Int(10, 10), 3f + SaboteurBrain.AttackIntervalSeconds)).Action);
        }
    }
}
