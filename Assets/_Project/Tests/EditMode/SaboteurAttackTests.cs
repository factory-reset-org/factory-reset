using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>AttackPlayer: when it is offered, how it scores, and what the brain does with it.</summary>
    public class SaboteurAttackTests
    {
        sealed class FakeSight : IPlayerSight
        {
            public bool Visible = true;
            public bool CanSeeCell(Vector2Int cell) => Visible;
        }

        sealed class FakeVisibility : ICoverVisibility
        {
            public bool Blocked;
            public float LastHeight;
            public bool IsBlocked(Vector2Int cell, float height)
            {
                LastHeight = height;
                return Blocked;
            }
        }

        static readonly ActionKey Attack = new ActionKey(SaboteurActionKind.AttackPlayer);

        // Exactly representable, like the squad tests, so the 62.5 ms stagger lines up with whole frames.
        const float Frame = 1f / 64f;

        GridGraph _grid;
        TargetClaims _claims;
        FakeSight _sight;
        Vector2Int _home;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(60, 60, Vector3.zero);
            _claims = new TargetClaims();
            _sight = new FakeSight();
            _home = new Vector2Int(10, 10);
        }

        static SaboteurIdentity Identity(SaboteurLetter letter) => new SaboteurIdentity((int)letter + 1, letter);

        SaboteurBrain Brain(SaboteurLetter letter, params Vector2Int[] patrolCells)
        {
            var points = new List<Vector3>();
            foreach (Vector2Int cell in patrolCells)
                points.Add(_grid.CellToWorld(cell));
            return new SaboteurBrain(Identity(letter), _grid, new AStarSearch(_grid), _claims, points, null, 0,
                new AttackPlayerSource(_sight));
        }

        // The player standing this many metres east of the Saboteur's home cell.
        PlayerSnapshot PlayerAt(float metresEast, bool alive = true, bool known = true, float height = 0f)
        {
            Vector3 position = _grid.CellToWorld(_home) + new Vector3(metresEast, height, 0f);
            return new PlayerSnapshot(known, _home, position, Vector3.zero, Vector3.forward, 5f,
                alive, 1f, 1f, false, 0f, 0f);
        }

        AgentContext Ctx(WorldBlackboard world, float time) =>
            new AgentContext(_home, _grid.CellToWorld(_home), Vector3.forward, time, world, default);

        static WorldBlackboard WorldWith(PlayerSnapshot player)
        {
            var world = new WorldBlackboard();
            world.SetPlayer(player);
            return world;
        }

        List<ActionCandidate> Candidates(PlayerSnapshot player)
        {
            var list = new List<ActionCandidate>();
            new AttackPlayerSource(_sight).AddCandidates(Ctx(WorldWith(player), 0f), Identity(SaboteurLetter.A), list);
            return list;
        }

        // ---- The source

        [Test]
        public void PlayerInRangeAndInSightIsAnAttackCandidate()
        {
            List<ActionCandidate> list = Candidates(PlayerAt(2f));

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(Attack, list[0].Key);
            Assert.AreEqual(0.75f, list[0].BaseScore, 1e-4f, "1 - d / 8 at 2 m.");
        }

        [Test]
        public void ClosePlayerScoresHigherThanFarPlayer()
        {
            float near = Candidates(PlayerAt(1f))[0].BaseScore;
            float far = Candidates(PlayerAt(6f))[0].BaseScore;

            Assert.Greater(near, far);
        }

        [Test]
        public void PlayerOnTheSpotScoresOne()
        {
            Assert.AreEqual(1f, Candidates(PlayerAt(0f))[0].BaseScore, 1e-4f);
        }

        [TestCase(8f)]
        [TestCase(8.5f)]
        [TestCase(30f)]
        public void PlayerAtOrBeyondEightMetresIsNotACandidate(float metres)
        {
            Assert.IsEmpty(Candidates(PlayerAt(metres)));
        }

        [Test]
        public void JustInsideEightMetresIsACandidate()
        {
            List<ActionCandidate> list = Candidates(PlayerAt(7.9f));

            Assert.AreEqual(1, list.Count);
            Assert.Greater(list[0].BaseScore, 0f);
        }

        [Test]
        public void RangeIsMeasuredOnTheGroundPlane()
        {
            // 2 m away and 10 m up (a catwalk, a jump): still 2 m by the design's ground distance.
            Assert.AreEqual(1, Candidates(PlayerAt(2f, height: 10f)).Count);
        }

        [Test]
        public void PlayerOutOfSightIsNotACandidate()
        {
            _sight.Visible = false;

            Assert.IsEmpty(Candidates(PlayerAt(2f)));
        }

        [Test]
        public void DeadPlayerIsNotACandidate()
        {
            Assert.IsEmpty(Candidates(PlayerAt(2f, alive: false)));
        }

        [Test]
        public void UnknownPlayerIsNotACandidate()
        {
            Assert.IsEmpty(Candidates(PlayerAt(2f, known: false)));
        }

        [Test]
        public void ContextWithoutABlackboardIsNotACandidate()
        {
            var list = new List<ActionCandidate>();
            var ctx = new AgentContext(_home, _grid.CellToWorld(_home), Vector3.forward, 0f, null, default);

            new AttackPlayerSource(_sight).AddCandidates(ctx, Identity(SaboteurLetter.A), list);

            Assert.IsEmpty(list);
        }

        [Test]
        public void SourceNeedsASightQuery()
        {
            Assert.Throws<System.ArgumentNullException>(() => new AttackPlayerSource(null));
        }

        // ---- The sight adapter

        [Test]
        public void SightIsClearWhenTheCoverQueryIsNotBlocked()
        {
            var visibility = new FakeVisibility();
            var sight = new CoverVisibilitySight(visibility);

            Assert.IsTrue(sight.CanSeeCell(_home));
            Assert.AreEqual(CoverVisibilitySight.TargetHeight, visibility.LastHeight);

            visibility.Blocked = true;
            Assert.IsFalse(sight.CanSeeCell(_home));
        }

        [Test]
        public void SightAdapterNeedsAVisibilityQuery()
        {
            Assert.Throws<System.ArgumentNullException>(() => new CoverVisibilitySight(null));
        }

        // ---- The brain

        [Test]
        public void FirstAttackTickStopsTheBodyFacesThePlayerAndShoots()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, new Vector2Int(30, 30));
            PlayerSnapshot player = PlayerAt(3f);

            AgentIntent intent = brain.Tick(Ctx(WorldWith(player), 0f));

            Assert.AreEqual(Attack, brain.CurrentAction);
            Assert.AreEqual(AgentAction.Shoot, intent.Action);
            Assert.AreEqual("AttackPlayer", intent.DebugState);
            Assert.IsNotNull(intent.Path, "The first attack tick stops the body.");
            Assert.AreEqual(0, intent.Path.Count);
            Assert.AreEqual(player.Position, intent.LookTarget);
        }

        [Test]
        public void ShotsArePacedByTheAttackInterval()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, new Vector2Int(30, 30));
            WorldBlackboard world = WorldWith(PlayerAt(3f));

            Assert.AreEqual(AgentAction.Shoot, brain.Tick(Ctx(world, 0f)).Action);

            AgentIntent between = brain.Tick(Ctx(world, SaboteurBrain.AttackIntervalSeconds - Frame));
            Assert.AreEqual(AgentAction.None, between.Action, "Not yet.");
            Assert.IsNull(between.Path, "The body stays stopped; no new path.");
            Assert.AreEqual("AttackPlayer", between.DebugState);

            Assert.AreEqual(AgentAction.Shoot, brain.Tick(Ctx(world, SaboteurBrain.AttackIntervalSeconds)).Action);
        }

        [Test]
        public void PlayerLeavingRangeSendsTheSaboteurBackToPatrol()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, new Vector2Int(30, 30));
            Assert.AreEqual(AgentAction.Shoot, brain.Tick(Ctx(WorldWith(PlayerAt(3f)), 0f)).Action);

            // Out of range: the next decision drops the attack and the body is routed again.
            AgentIntent intent = default;
            WorldBlackboard far = WorldWith(PlayerAt(20f));
            for (float t = Frame; t <= 2f; t += Frame)
            {
                intent = brain.Tick(Ctx(far, t));
                if (intent.DebugState == "Patrol")
                    break;
            }

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.AreEqual(AgentAction.None, intent.Action);
            Assert.IsNotNull(intent.Path, "A fresh patrol route, because the body had been stopped.");
            Assert.Greater(intent.Path.Count, 0);
        }

        [Test]
        public void PlayerDyingMidAttackStopsTheShootingAtOnce()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, new Vector2Int(30, 30));
            Assert.AreEqual(AgentAction.Shoot, brain.Tick(Ctx(WorldWith(PlayerAt(3f)), 0f)).Action);

            // A dead player must not be shot at, whether or not this tick is a decision slot.
            AgentIntent intent = brain.Tick(Ctx(WorldWith(PlayerAt(3f, alive: false)), SaboteurBrain.AttackIntervalSeconds));

            Assert.AreNotEqual(AgentAction.Shoot, intent.Action);
            Assert.AreEqual("Patrol", intent.DebugState);
        }

        [Test]
        public void StunDuringAnAttackResumesWithAFreshRoute()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, new Vector2Int(30, 30));
            WorldBlackboard far = WorldWith(PlayerAt(20f));
            brain.Tick(Ctx(WorldWith(PlayerAt(3f)), 0f));

            brain.OnStunned(2f);
            AgentIntent intent = brain.Tick(Ctx(far, 5f));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNotNull(intent.Path);
            Assert.Greater(intent.Path.Count, 0);
        }

        [Test]
        public void ThirdAttackerIsSaturatedAndPrefersToIdleWhenItsScoreIsLow()
        {
            // 6.4 m: score 0.2, above Idle (0.1). Saturated by two attackers: 0.2 x 0.45 = 0.09, below it.
            SaboteurBrain a = Brain(SaboteurLetter.A);
            SaboteurBrain b = Brain(SaboteurLetter.B);
            SaboteurBrain c = Brain(SaboteurLetter.C);
            WorldBlackboard near = WorldWith(PlayerAt(2f));
            WorldBlackboard edge = WorldWith(PlayerAt(6.4f));

            // A and B attack from 2 m.
            a.Tick(Ctx(near, 0f));
            b.Tick(Ctx(near, 0f));
            b.Tick(Ctx(near, SquadCoordinator.StaggerStep));
            Assert.AreEqual(Attack, a.CurrentAction);
            Assert.AreEqual(Attack, b.CurrentAction);

            // C, 6.4 m away, sees two attackers and idles.
            c.Tick(Ctx(edge, 0f));
            c.Tick(Ctx(edge, 2 * SquadCoordinator.StaggerStep));
            Assert.AreEqual(SaboteurActionKind.Idle, c.CurrentAction.Kind);

            // Without the saturation the same Saboteur would attack.
            _claims = new TargetClaims();
            SaboteurBrain alone = Brain(SaboteurLetter.A);
            alone.Tick(Ctx(edge, 0f));
            Assert.AreEqual(Attack, alone.CurrentAction);
        }

        [Test]
        public void BrainWithNoAttackSourceNeverShoots()
        {
            var brain = new SaboteurBrain(Identity(SaboteurLetter.A), _grid, new AStarSearch(_grid), _claims,
                new List<Vector3> { _grid.CellToWorld(new Vector2Int(30, 30)) });

            AgentIntent intent = brain.Tick(Ctx(WorldWith(PlayerAt(1f)), 0f));

            Assert.AreEqual(AgentAction.None, intent.Action);
            Assert.AreEqual("Patrol", intent.DebugState);
        }
    }
}
