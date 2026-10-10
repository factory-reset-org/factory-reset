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
    /// The runtime's answers to the Saboteur: <see cref="IActionFeedback"/> for a door request and
    /// <see cref="IHealthAware"/> for its own health. Same map as the detour tests: a wall at x = 20
    /// with door 1 on the player's straight route and door 2 far off it.
    /// </summary>
    public class SaboteurFeedbackTests
    {
        sealed class AlwaysSight : IPlayerSight
        {
            public bool CanSeeCell(Vector2Int cell) => true;
        }

        // The player is never in sight, so only the door is on offer in the door tests.
        sealed class NeverSight : IPlayerSight
        {
            public bool CanSeeCell(Vector2Int cell) => false;
        }

        static readonly Vector2Int Door1Cell = new Vector2Int(20, 10);
        static readonly Vector2Int Door2Cell = new Vector2Int(20, 0);
        static readonly Vector2Int PlayerCell = new Vector2Int(5, 10);
        static readonly Vector2Int ObjectiveCell = new Vector2Int(35, 10);
        static readonly Vector2Int AtDoor = new Vector2Int(19, 10);
        static readonly ActionKey Door1 = new ActionKey(SaboteurActionKind.CloseDoor, 1);

        GridGraph _grid;
        TargetClaims _claims;
        WorldBlackboard _world;

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

            _claims = new TargetClaims();
            _world = new WorldBlackboard();
            _world.SetPlayer(new PlayerSnapshot(true, PlayerCell, _grid.CellToWorld(PlayerCell), Vector3.zero, Vector3.forward,
                5f, true, 1f, 1f, false, 0f, 0f));
            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, ObjectiveCell, ObjectiveTargetKind.Task) });
        }

        SaboteurBrain Brain(out DetourCache cache, SaboteurLetter letter = SaboteurLetter.A)
        {
            var pathfinder = new AStarSearch(_grid);
            cache = DetourCache.For(_world, _grid, pathfinder);
            var source = new CompositeCandidateSource(new AttackPlayerSource(new NeverSight()), new CloseDoorSource(cache));
            return new SaboteurBrain(new SaboteurIdentity((int)letter + 1, letter), _grid, pathfinder, _claims,
                new List<Vector3> { _grid.CellToWorld(new Vector2Int(2, 2)) }, null, 0, source, cache);
        }

        AgentContext At(Vector2Int cell, float time) =>
            new AgentContext(cell, _grid.CellToWorld(cell), Vector3.forward, time, _world, default);

        // ---- Door feedback

        [Test]
        public void SuccessEndsThePlanOnTheNextTickAndReleasesTheClaim()
        {
            SaboteurBrain brain = Brain(out _);
            AgentIntent asking = brain.Tick(At(AtDoor, 0f));
            Assert.AreEqual(AgentAction.CloseDoor, asking.Action);
            Assert.AreEqual(1, _claims.ClaimedBy(1));

            // The door really closes, as the controller's Execute would do, and the answer follows.
            _grid.SetDoor(Door1Cell, 1, true);
            brain.OnActionResolved(AgentAction.CloseDoor, 1, true);
            AgentIntent next = brain.Tick(At(AtDoor, 0.1f));

            Assert.AreNotEqual(AgentAction.CloseDoor, next.Action);
            Assert.AreEqual("Patrol", next.DebugState);
            Assert.IsNull(_claims.ClaimedBy(1));
        }

        [Test]
        public void SuccessStartsTheDoorCooldownEvenIfTheDoorIsOpenedAgain()
        {
            SaboteurBrain brain = Brain(out _);
            brain.Tick(At(AtDoor, 0f));
            _grid.SetDoor(Door1Cell, 1, true);
            brain.OnActionResolved(AgentAction.CloseDoor, 1, true);
            brain.Tick(At(AtDoor, 0.1f));

            // The player opens it again at once: the Saboteur leaves it alone for the cooldown.
            _grid.SetDoor(Door1Cell, 1, false);
            for (float t = 0.25f; t < 8f; t += 0.25f)
            {
                brain.Tick(At(AtDoor, t));
                Assert.AreNotEqual(Door1, brain.CurrentAction, $"At {t:F2} s.");
            }
        }

        [Test]
        public void FailureEndsThePlanAtOnceWithoutWaitingForTheTimeout()
        {
            SaboteurBrain brain = Brain(out _);
            brain.Tick(At(AtDoor, 0f));

            brain.OnActionResolved(AgentAction.CloseDoor, 1, false);
            AgentIntent next = brain.Tick(At(AtDoor, 0.3f));

            Assert.Less(0.3f, SaboteurBrain.CloseDoorTimeoutSeconds);
            Assert.AreNotEqual(AgentAction.CloseDoor, next.Action);
            Assert.IsNull(_claims.ClaimedBy(1));
        }

        [Test]
        public void FailureStartsTheCooldownSoTheDoorIsNotRetriedAtOnce()
        {
            SaboteurBrain brain = Brain(out _);
            brain.Tick(At(AtDoor, 0f));
            brain.OnActionResolved(AgentAction.CloseDoor, 1, false);
            brain.Tick(At(AtDoor, 0.3f));

            for (float t = 0.5f; t < 8f; t += 0.25f)
            {
                brain.Tick(At(AtDoor, t));
                Assert.AreNotEqual(Door1, brain.CurrentAction, $"At {t:F2} s.");
            }
        }

        [Test]
        public void DoorIsTriedAgainOnceTheCooldownHasPassed()
        {
            SaboteurBrain brain = Brain(out _);
            brain.Tick(At(AtDoor, 0f));
            brain.OnActionResolved(AgentAction.CloseDoor, 1, false);
            brain.Tick(At(AtDoor, 0.3f));

            float after = 0.3f + new SelectorSettings().DoorCooldownSeconds + 0.5f;
            brain.Tick(At(AtDoor, after));
            brain.Tick(At(AtDoor, after + 0.25f));

            Assert.AreEqual(Door1, brain.CurrentAction);
        }

        [Test]
        public void AnswerForAnotherDoorDoesNotEndThisPlanButStillStartsThatDoorsCooldown()
        {
            SaboteurBrain brain = Brain(out _);
            brain.Tick(At(AtDoor, 0f));

            brain.OnActionResolved(AgentAction.CloseDoor, 2, true);
            AgentIntent next = brain.Tick(At(AtDoor, 0.1f));

            Assert.AreEqual(AgentAction.CloseDoor, next.Action, "Door 1 is still being closed.");
            Assert.AreEqual(1, _claims.ClaimedBy(1));
        }

        [Test]
        public void AnswersForOtherActionsAreIgnored()
        {
            SaboteurBrain brain = Brain(out _);
            brain.Tick(At(AtDoor, 0f));

            brain.OnActionResolved(AgentAction.ArmTrap, 1, true);
            brain.OnActionResolved(AgentAction.StealBattery, 1, false);
            brain.OnActionResolved(AgentAction.OpenDoor, 1, true);
            AgentIntent next = brain.Tick(At(AtDoor, 0.1f));

            Assert.AreEqual(AgentAction.CloseDoor, next.Action);
        }

        [Test]
        public void AnswerAfterTheSaboteurIsDestroyedIsIgnored()
        {
            SaboteurBrain brain = Brain(out _);
            brain.Tick(At(AtDoor, 0f));
            brain.OnDestroyed();

            Assert.DoesNotThrow(() => brain.OnActionResolved(AgentAction.CloseDoor, 1, true));
        }

        [Test]
        public void ARequestAnsweredAfterTheSaboteurMovedOnStillSetsTheCooldown()
        {
            SaboteurBrain brain = Brain(out _);
            brain.Tick(At(AtDoor, 0f));
            brain.OnStunned(2f);   // the plan is dropped

            brain.OnActionResolved(AgentAction.CloseDoor, 1, false);   // the controller answers a knocked-out request
            brain.Tick(At(AtDoor, 3f));
            brain.Tick(At(AtDoor, 3.25f));

            Assert.AreNotEqual(Door1, brain.CurrentAction);
        }

        [Test]
        public void TheTimeoutStillEndsAPlanThatNeverGetsAnAnswer()
        {
            SaboteurBrain brain = Brain(out _);
            brain.Tick(At(AtDoor, 0f));

            AgentIntent intent = default;
            for (float t = 0.25f; t <= SaboteurBrain.CloseDoorTimeoutSeconds + 1f; t += 0.25f)
                intent = brain.Tick(At(AtDoor, t));

            Assert.AreNotEqual(AgentAction.CloseDoor, intent.Action);
            Assert.IsNull(_claims.ClaimedBy(1));
        }

        // ---- The shared cache

        [Test]
        public void SquadMatesShareOneDetourCache()
        {
            var pathfinder = new AStarSearch(_grid);

            DetourCache a = DetourCache.For(_world, _grid, pathfinder);
            DetourCache b = DetourCache.For(_world, _grid, pathfinder);
            DetourCache other = DetourCache.For(new WorldBlackboard(), _grid, pathfinder);

            Assert.AreSame(a, b);
            Assert.AreNotSame(a, other, "A new level's blackboard gets its own cache.");
            Assert.Throws<System.ArgumentNullException>(() => DetourCache.For(null, _grid, pathfinder));
        }

        // ---- Own health

        [Test]
        public void BrainKeepsItsHealthFractionFromTheController()
        {
            SaboteurBrain brain = Brain(out _);
            Assert.AreEqual(1f, brain.HealthFraction);

            brain.OnHealthChanged(2, 3);
            Assert.AreEqual(2f / 3f, brain.HealthFraction, 1e-5f);

            brain.OnHealthChanged(0, 3);
            Assert.AreEqual(0f, brain.HealthFraction);

            brain.OnHealthChanged(5, 0);
            Assert.AreEqual(1f, brain.HealthFraction, "An unknown maximum reads as full health.");
        }

        List<ActionCandidate> AttackCandidates(AttackPlayerSource source, float metresEast)
        {
            var list = new List<ActionCandidate>();
            Vector3 saboteur = _grid.CellToWorld(new Vector2Int(10, 10));
            _world.SetPlayer(new PlayerSnapshot(true, new Vector2Int(10, 10), saboteur + new Vector3(metresEast, 0f, 0f), Vector3.zero,
                Vector3.forward, 5f, true, 1f, 1f, false, 0f, 0f));
            var ctx = new AgentContext(new Vector2Int(10, 10), saboteur, Vector3.forward, 0f, _world, default);
            source.AddCandidates(ctx, new SaboteurIdentity(1, SaboteurLetter.A), list);
            return list;
        }

        [Test]
        public void AttackSourceStartsAtFullHealth()
        {
            Assert.AreEqual(1f, new AttackPlayerSource(new AlwaysSight()).OwnHealth);
        }

        [Test]
        public void ADamagedSaboteurWantsToAttackLess()
        {
            var healthy = new AttackPlayerSource(new AlwaysSight());
            var hurt = new AttackPlayerSource(new AlwaysSight());
            hurt.OnHealthChanged(1, 3);

            float full = AttackCandidates(healthy, 3f)[0].BaseScore;
            float damaged = AttackCandidates(hurt, 3f)[0].BaseScore;

            Assert.Greater(full, damaged);
        }

        [Test]
        public void AttackScoreKeepsFallingAsHealthDrops()
        {
            var source = new AttackPlayerSource(new AlwaysSight());
            float previous = float.MaxValue;
            foreach (int hp in new[] { 6, 4, 2, 1 })
            {
                source.OnHealthChanged(hp, 6);
                float score = AttackCandidates(source, 2f)[0].BaseScore;
                Assert.Less(score, previous, $"At {hp} of 6.");
                previous = score;
            }
        }

        [Test]
        public void ADestroyedHealthBarVetoesTheAttack()
        {
            var source = new AttackPlayerSource(new AlwaysSight());
            source.OnHealthChanged(0, 3);

            Assert.IsEmpty(AttackCandidates(source, 2f));
        }

        [Test]
        public void AttackStaysAtItsOldScoreForAFullHealthSaboteur()
        {
            // Two considerations at full health: raw 0.75 at 2 m, compensated to 0.84375.
            Assert.AreEqual(0.84375f, AttackCandidates(new AttackPlayerSource(new AlwaysSight()), 2f)[0].BaseScore, 1e-4f);
        }

        [Test]
        public void BrainPassesHealthOnToItsAttackSource()
        {
            var attack = new AttackPlayerSource(new AlwaysSight());
            var composite = new CompositeCandidateSource(attack, new CloseDoorSource(new DetourCache(_grid, new AStarSearch(_grid))));
            var brain = new SaboteurBrain(new SaboteurIdentity(1, SaboteurLetter.A), _grid, new AStarSearch(_grid), _claims,
                new List<Vector3>(), null, 0, composite);

            brain.OnHealthChanged(1, 3);

            Assert.AreEqual(1f / 3f, attack.OwnHealth, 1e-5f);
        }

        [Test]
        public void BrainWithoutASourceTakesHealthWithoutFailing()
        {
            var brain = new SaboteurBrain(new SaboteurIdentity(1, SaboteurLetter.A), _grid, new AStarSearch(_grid), _claims,
                new List<Vector3>());

            Assert.DoesNotThrow(() => brain.OnHealthChanged(1, 3));
            Assert.AreEqual(1f / 3f, brain.HealthFraction, 1e-5f);
        }
    }
}
