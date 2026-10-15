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
    /// <summary>Flee: when it is offered and how it scores, where the Saboteur runs, and what the brain does with it.</summary>
    public class SaboteurFleeTests
    {
        sealed class FakeSight : IPlayerSight
        {
            public bool Visible = true;
            public bool CanSeeCell(Vector2Int cell) => Visible;
        }

        sealed class CountingPathfinder : IPathfinder
        {
            public int Calls;
            public PathResult FindPath(Vector2Int start, Vector2Int goal, ICostModel cost)
            {
                Calls++;
                return PathResult.NotFound(0, 0f, 0);
            }
        }

        static readonly ActionKey Flee = new ActionKey(SaboteurActionKind.Flee);
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
            _home = new Vector2Int(30, 30);
        }

        static SaboteurIdentity Identity(SaboteurLetter letter) => new SaboteurIdentity((int)letter + 1, letter);

        Vector3 HomeWorld => _grid.CellToWorld(_home);

        // The player this many metres east of home. By default it faces west, straight at the Saboteur.
        PlayerSnapshot PlayerAt(float metresEast, Vector3? forward = null, bool alive = true, bool known = true)
        {
            Vector3 position = HomeWorld + new Vector3(metresEast, 0f, 0f);
            return new PlayerSnapshot(known, _home, position, Vector3.zero, forward ?? Vector3.left, 5f,
                alive, 1f, 1f, false, 0f, 0f);
        }

        AgentContext Ctx(WorldBlackboard world, float time, Vector2Int? cell = null, Vector3? position = null) =>
            new AgentContext(cell ?? _home, position ?? HomeWorld, Vector3.forward, time, world, default);

        static WorldBlackboard WorldWith(PlayerSnapshot player)
        {
            var world = new WorldBlackboard();
            world.SetPlayer(player);
            return world;
        }

        List<ActionCandidate> Candidates(PlayerSnapshot player, float health = 0.25f, SaboteurLetter letter = SaboteurLetter.B)
        {
            var source = new FleeSource(_sight);
            source.OnHealthChanged(Mathf.RoundToInt(health * 4f), 4);
            var list = new List<ActionCandidate>();
            source.AddCandidates(Ctx(WorldWith(player), 0f), Identity(letter), list);
            return list;
        }

        // ---- The source: eligibility

        [Test]
        public void HurtSaboteurFacedByThePlayerInSightIsAFleeCandidate()
        {
            List<ActionCandidate> list = Candidates(PlayerAt(5f));

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(Flee, list[0].Key);
        }

        [Test]
        public void ScoreMatchesTheHandWorkedCurves()
        {
            // Health 0.25 lost 0.75: logistic 1 / (1 + e^-2.5) = 0.9241. Aimed dead on: 1. Player 5 m of 14: 1 - 5/14 = 0.6429.
            // raw 0.5941; three considerations, so modFactor 2/3: 0.5941 + 0.4059 x 0.6667 x 0.5941 = 0.7549.
            Assert.AreEqual(0.7549f, Candidates(PlayerAt(5f))[0].BaseScore, 2e-3f);
        }

        [Test]
        public void PlayerLookingAwayIsNotAReasonToRun()
        {
            Assert.IsEmpty(Candidates(PlayerAt(5f, forward: Vector3.right)));
            Assert.IsEmpty(Candidates(PlayerAt(5f, forward: Vector3.forward)), "Facing sideways is outside the cone.");
        }

        [Test]
        public void JustOutsideTheAimConeIsNotACandidateButJustInsideIs()
        {
            Vector3 atEdge = Quaternion.Euler(0f, FleeSource.AimHalfAngleDegrees + 0.5f, 0f) * Vector3.left;
            Vector3 inside = Quaternion.Euler(0f, FleeSource.AimHalfAngleDegrees - 5f, 0f) * Vector3.left;

            Assert.IsEmpty(Candidates(PlayerAt(5f, forward: atEdge)));
            Assert.AreEqual(1, Candidates(PlayerAt(5f, forward: inside)).Count);
        }

        [Test]
        public void AimingStraightAtTheSaboteurScoresHigherThanNearlyMissing()
        {
            Vector3 off = Quaternion.Euler(0f, 30f, 0f) * Vector3.left;

            float dead = Candidates(PlayerAt(5f))[0].BaseScore;
            float glancing = Candidates(PlayerAt(5f, forward: off))[0].BaseScore;

            Assert.Greater(dead, glancing);
        }

        [Test]
        public void AimIsReadOnTheGroundPlane()
        {
            // The player looks slightly down at the Saboteur: the pitch does not matter.
            Assert.AreEqual(1, Candidates(PlayerAt(5f, forward: new Vector3(-1f, -0.5f, 0f))).Count);
        }

        [TestCase(14f)]
        [TestCase(20f)]
        public void PlayerAtOrBeyondFourteenMetresIsNotACandidate(float metres)
        {
            Assert.IsEmpty(Candidates(PlayerAt(metres)));
        }

        [Test]
        public void JustInsideFourteenMetresIsACandidate()
        {
            Assert.AreEqual(1, Candidates(PlayerAt(13.9f)).Count);
        }

        [Test]
        public void ClosePlayerScoresHigherThanFarPlayer()
        {
            Assert.Greater(Candidates(PlayerAt(2f))[0].BaseScore, Candidates(PlayerAt(10f))[0].BaseScore);
        }

        [Test]
        public void SaboteurThePlayerCannotSeeHasNothingToRunFrom()
        {
            _sight.Visible = false;

            Assert.IsEmpty(Candidates(PlayerAt(5f)));
        }

        [Test]
        public void DeadOrUnknownPlayerIsNotACandidate()
        {
            Assert.IsEmpty(Candidates(PlayerAt(5f, alive: false)));
            Assert.IsEmpty(Candidates(PlayerAt(5f, known: false)));
        }

        [Test]
        public void ContextWithoutABlackboardIsNotACandidate()
        {
            var list = new List<ActionCandidate>();
            var ctx = new AgentContext(_home, HomeWorld, Vector3.forward, 0f, null, default);

            new FleeSource(_sight).AddCandidates(ctx, Identity(SaboteurLetter.A), list);

            Assert.IsEmpty(list);
        }

        [Test]
        public void SourceNeedsASightQuery()
        {
            Assert.Throws<System.ArgumentNullException>(() => new FleeSource(null));
        }

        // ---- The source: health and the keycard

        [Test]
        public void HealthyAimedSaboteurScoresBelowIdle()
        {
            List<ActionCandidate> list = Candidates(PlayerAt(2f), health: 1f);

            Assert.Less(list.Count == 0 ? 0f : list[0].BaseScore, SaboteurBrain.IdleScore, "A Saboteur that has not been hurt does not run.");
        }

        [Test]
        public void ScoreRisesAsTheSaboteurIsHurtMore()
        {
            float three = Candidates(PlayerAt(5f), health: 0.75f)[0].BaseScore;
            float two = Candidates(PlayerAt(5f), health: 0.5f)[0].BaseScore;
            float one = Candidates(PlayerAt(5f), health: 0.25f)[0].BaseScore;

            Assert.Less(three, two);
            Assert.Less(two, one);
        }

        [Test]
        public void HealthIsTakenFromTheControllersReports()
        {
            var source = new FleeSource(_sight);
            Assert.AreEqual(1f, source.OwnHealth);

            source.OnHealthChanged(1, 4);
            Assert.AreEqual(0.25f, source.OwnHealth);

            source.OnHealthChanged(0, 0);
            Assert.AreEqual(1f, source.OwnHealth, "No maximum reads as full health.");
        }

        [Test]
        public void KeycardCarrierFleesWithABonus()
        {
            float other = Candidates(PlayerAt(5f), health: 0.5f, letter: SaboteurLetter.B)[0].BaseScore;
            float carrier = Candidates(PlayerAt(5f), health: 0.5f, letter: SaboteurLetter.A)[0].BaseScore;

            Assert.AreEqual(other * (1f + FleeSource.KeycardCarrierBonus), carrier, 1e-4f);
        }

        [Test]
        public void KeycardBonusNeverPushesAScoreAboveOne()
        {
            float carrier = Candidates(PlayerAt(0.5f), health: 0f, letter: SaboteurLetter.A)[0].BaseScore;

            Assert.AreEqual(1f, carrier, 1e-5f);
        }

        [Test]
        public void KeycardBonusDoesNotMakeAnUnhurtCarrierRun()
        {
            List<ActionCandidate> list = Candidates(PlayerAt(2f), health: 1f, letter: SaboteurLetter.A);

            Assert.Less(list.Count == 0 ? 0f : list[0].BaseScore, SaboteurBrain.IdleScore);
        }

        [Test]
        public void AimAtHandlesDegenerateDirections()
        {
            PlayerSnapshot player = PlayerAt(0f, forward: Vector3.zero);
            Assert.AreEqual(0f, FleeSource.AimAt(player, HomeWorld + Vector3.right), "No facing direction, no aim.");

            PlayerSnapshot facing = PlayerAt(0f, forward: Vector3.forward);
            Assert.AreEqual(1f, FleeSource.AimAt(facing, HomeWorld), "A Saboteur on the spot is aimed at.");
        }

        // ---- The planner

        [Test]
        public void RetreatEndsFartherFromThePlayerThanTheSaboteurStands()
        {
            Vector3 player = HomeWorld + new Vector3(3f, 0f, 0f);

            bool found = FleePlanner.TryChooseRetreat(_grid, new AStarSearch(_grid), _home, HomeWorld, player, null, out List<Vector2Int> route);

            Assert.IsTrue(found);
            Assert.AreEqual(_home, route[0], "The route starts where the Saboteur stands.");
            Vector3 end = _grid.CellToWorld(route[route.Count - 1]);
            Assert.GreaterOrEqual(Vector3.Distance(end, player), 3f + FleePlanner.MinGainMetres - 0.01f);
        }

        [Test]
        public void RetreatHeadsAwayFromThePlayer()
        {
            Vector3 player = HomeWorld + new Vector3(3f, 0f, 0f);

            FleePlanner.TryChooseRetreat(_grid, new AStarSearch(_grid), _home, HomeWorld, player, null, out List<Vector2Int> route);

            Assert.Less(_grid.CellToWorld(route[route.Count - 1]).x, HomeWorld.x + 1f, "Not towards the east, where the player is.");
        }

        [Test]
        public void RetreatDoesNotRunPastThePlayer()
        {
            // A one-cell-wide corridor along row 30, walled off to the west: the only way away is east, past the player.
            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                for (int x = 0; x < _grid.Width; x++)
                for (int y = 0; y < _grid.Height; y++)
                    if (y != _home.y || x < _home.x - 1)
                        batch.SetWalkable(new Vector2Int(x, y), false);
                batch.Commit();
            }

            Vector3 player = HomeWorld + new Vector3(2f, 0f, 0f);

            bool found = FleePlanner.TryChooseRetreat(_grid, new AStarSearch(_grid), _home, HomeWorld, player, null, out _);

            Assert.IsFalse(found, "The only way out leads through the player.");
        }

        [Test]
        public void CorneredSaboteurFindsNoRetreat()
        {
            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                for (int x = 0; x < _grid.Width; x++)
                for (int y = 0; y < _grid.Height; y++)
                    if (Mathf.Abs(x - _home.x) > 1 || Mathf.Abs(y - _home.y) > 1)
                        batch.SetWalkable(new Vector2Int(x, y), false);
                batch.Commit();
            }

            bool found = FleePlanner.TryChooseRetreat(_grid, new AStarSearch(_grid), _home, HomeWorld,
                HomeWorld + new Vector3(3f, 0f, 0f), null, out List<Vector2Int> route);

            Assert.IsFalse(found);
            Assert.IsNull(route);
        }

        [Test]
        public void KnownSpotsAreConsidered()
        {
            // Walls leave only the known spot a long way west as somewhere to go.
            var spot = new Vector2Int(5, 30);
            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                for (int x = 0; x < _grid.Width; x++)
                for (int y = 0; y < _grid.Height; y++)
                    if (y != _home.y)
                        batch.SetWalkable(new Vector2Int(x, y), false);
                batch.Commit();
            }

            Vector3 player = HomeWorld + new Vector3(3f, 0f, 0f);
            bool found = FleePlanner.TryChooseRetreat(_grid, new AStarSearch(_grid), _home, HomeWorld, player, new[] { spot }, out List<Vector2Int> route);

            Assert.IsTrue(found);
            Assert.AreEqual(spot, route[route.Count - 1]);
        }

        [Test]
        public void RetreatUsesAtMostThreeSearches()
        {
            var counting = new CountingPathfinder();

            bool found = FleePlanner.TryChooseRetreat(_grid, counting, _home, HomeWorld, HomeWorld + new Vector3(3f, 0f, 0f), null, out _);

            Assert.IsFalse(found);
            Assert.AreEqual(FleePlanner.MaxSearches, counting.Calls);
        }

        [Test]
        public void RetreatChoiceIsRepeatable()
        {
            Vector3 player = HomeWorld + new Vector3(3f, 0f, 2f);
            FleePlanner.TryChooseRetreat(_grid, new AStarSearch(_grid), _home, HomeWorld, player, null, out List<Vector2Int> first);
            FleePlanner.TryChooseRetreat(_grid, new AStarSearch(_grid), _home, HomeWorld, player, null, out List<Vector2Int> second);

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void PlannerNeedsAGridAndAPathfinder()
        {
            Assert.Throws<System.ArgumentNullException>(() => FleePlanner.TryChooseRetreat(null, new AStarSearch(_grid), _home, HomeWorld, HomeWorld, null, out _));
            Assert.Throws<System.ArgumentNullException>(() => FleePlanner.TryChooseRetreat(_grid, null, _home, HomeWorld, HomeWorld, null, out _));
        }

        // ---- The brain

        SaboteurBrain Brain(SaboteurLetter letter, params Vector2Int[] patrolCells)
        {
            var points = new List<Vector3>();
            foreach (Vector2Int cell in patrolCells)
                points.Add(_grid.CellToWorld(cell));
            var source = new CompositeCandidateSource(new AttackPlayerSource(_sight), new FleeSource(_sight));
            return new SaboteurBrain(Identity(letter), _grid, new AStarSearch(_grid), _claims, points, null, 0, source);
        }

        // The Saboteur has been hit three times out of four.
        SaboteurBrain HurtBrain(SaboteurLetter letter = SaboteurLetter.A)
        {
            SaboteurBrain brain = Brain(letter, new Vector2Int(10, 10));
            brain.OnHealthChanged(1, 4);
            return brain;
        }

        [Test]
        public void HurtSaboteurFacedByThePlayerRunsAway()
        {
            SaboteurBrain brain = HurtBrain(SaboteurLetter.A);
            PlayerSnapshot player = PlayerAt(3f);

            AgentIntent intent = brain.Tick(Ctx(WorldWith(player), 0f));

            Assert.AreEqual(Flee, brain.CurrentAction);
            Assert.AreEqual("Flee", intent.DebugState);
            Assert.AreEqual(AgentAction.None, intent.Action, "Running, not shooting.");
            Assert.IsNotNull(intent.Path);
            Assert.Greater(intent.Path.Count, 1);
            Assert.Greater(Vector3.Distance(intent.Path[intent.Path.Count - 1], player.Position), 3f + FleePlanner.MinGainMetres - 0.01f);
        }

        [Test]
        public void TheRunKeepsGoingWithoutANewRouteUntilItIsTimeToChooseAgain()
        {
            SaboteurBrain brain = HurtBrain(SaboteurLetter.A);
            WorldBlackboard world = WorldWith(PlayerAt(3f));
            brain.Tick(Ctx(world, 0f));

            AgentIntent keep = brain.Tick(Ctx(world, Frame));
            Assert.AreEqual("Flee", keep.DebugState);
            Assert.IsNull(keep.Path, "The route already sent is still being run.");

            AgentIntent again = brain.Tick(Ctx(world, SaboteurBrain.FleeReplanSeconds));
            Assert.AreEqual("Flee", again.DebugState);
            Assert.IsNotNull(again.Path, "A second after the first choice the player may have moved, so it chooses again.");
        }

        [Test]
        public void HealthySaboteurFightsInsteadOfRunning()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, new Vector2Int(10, 10));

            AgentIntent intent = brain.Tick(Ctx(WorldWith(PlayerAt(1.5f)), 0f));

            Assert.AreEqual(Attack, brain.CurrentAction);
            Assert.AreEqual(AgentAction.Shoot, intent.Action);
        }

        [Test]
        public void PlayerLookingAwayStopsTheFlight()
        {
            SaboteurBrain brain = HurtBrain(SaboteurLetter.A);
            Assert.AreEqual("Flee", brain.Tick(Ctx(WorldWith(PlayerAt(3f)), 0f)).DebugState);

            WorldBlackboard away = WorldWith(PlayerAt(3f, forward: Vector3.right));
            AgentIntent intent = default;
            for (float t = Frame; t <= 3f; t += Frame)
            {
                intent = brain.Tick(Ctx(away, t));
                if (intent.DebugState != "Flee")
                    break;
            }

            Assert.AreNotEqual("Flee", intent.DebugState);
        }

        [Test]
        public void DeadPlayerEndsTheFlightAtOnce()
        {
            SaboteurBrain brain = HurtBrain(SaboteurLetter.A);
            Assert.AreEqual("Flee", brain.Tick(Ctx(WorldWith(PlayerAt(3f)), 0f)).DebugState);

            AgentIntent intent = brain.Tick(Ctx(WorldWith(PlayerAt(3f, alive: false)), Frame));

            Assert.AreNotEqual("Flee", intent.DebugState);
            Assert.AreEqual(AgentAction.None, intent.Action);
        }

        [Test]
        public void CorneredSaboteurFightsBackInsteadOfFreezing()
        {
            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                for (int x = 0; x < _grid.Width; x++)
                for (int y = 0; y < _grid.Height; y++)
                    if (Mathf.Abs(x - _home.x) > 1 || Mathf.Abs(y - _home.y) > 1)
                        batch.SetWalkable(new Vector2Int(x, y), false);
                batch.Commit();
            }

            SaboteurBrain brain = Brain(SaboteurLetter.A);
            brain.OnHealthChanged(1, 4);

            AgentIntent intent = brain.Tick(Ctx(WorldWith(PlayerAt(1.5f)), 0f));

            Assert.AreEqual(Flee, brain.CurrentAction, "It still wants to flee.");
            Assert.AreEqual("AttackPlayer", intent.DebugState);
            Assert.AreEqual(AgentAction.Shoot, intent.Action, "Cornered with the player in reach, it swipes.");
        }

        [Test]
        public void StunEndsTheFlightAndTheNextTickRoutesAgain()
        {
            SaboteurBrain brain = HurtBrain(SaboteurLetter.A);
            Assert.AreEqual("Flee", brain.Tick(Ctx(WorldWith(PlayerAt(3f)), 0f)).DebugState);

            brain.OnStunned(2f);
            AgentIntent intent = brain.Tick(Ctx(WorldWith(PlayerAt(40f)), 5f));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNotNull(intent.Path);
        }

        [Test]
        public void BrainWithoutAFleeSourceNeverFlees()
        {
            var points = new List<Vector3> { _grid.CellToWorld(new Vector2Int(10, 10)) };
            var brain = new SaboteurBrain(Identity(SaboteurLetter.A), _grid, new AStarSearch(_grid), _claims, points, null, 0,
                new AttackPlayerSource(_sight));
            brain.OnHealthChanged(1, 4);

            AgentIntent intent = brain.Tick(Ctx(WorldWith(PlayerAt(3f)), 0f));

            Assert.AreNotEqual("Flee", intent.DebugState);
        }

        [Test]
        public void HurtSaboteurThatCannotBeSeenDoesNotFlee()
        {
            _sight.Visible = false;
            SaboteurBrain brain = HurtBrain(SaboteurLetter.A);

            AgentIntent intent = brain.Tick(Ctx(WorldWith(PlayerAt(3f)), 0f));

            Assert.AreNotEqual("Flee", intent.DebugState);
        }

        [Test]
        public void FleeIsNotAClaimableAction()
        {
            Assert.IsFalse(SquadCoordinator.IsClaimable(SaboteurActionKind.Flee));
        }
    }
}
