using System;
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
    public class SaboteurBrainTests
    {
        // Wraps the real A* so tests can count how often the brain searches.
        sealed class CountingPathfinder : IPathfinder
        {
            readonly IPathfinder _inner;
            public int Calls;

            public CountingPathfinder(GridGraph grid) { _inner = new AStarSearch(grid); }

            public PathResult FindPath(Vector2Int start, Vector2Int goal, ICostModel cost)
            {
                Calls++;
                return _inner.FindPath(start, goal, cost);
            }
        }

        static readonly SaboteurIdentity IdentityA = new SaboteurIdentity(1, SaboteurLetter.A);

        GridGraph _grid;
        CountingPathfinder _pathfinder;
        TargetClaims _claims;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(10, 10, Vector3.zero);
            _pathfinder = new CountingPathfinder(_grid);
            _claims = new TargetClaims();
        }

        SaboteurBrain Brain(params Vector2Int[] patrolCells)
        {
            var points = new List<Vector3>();
            foreach (Vector2Int cell in patrolCells)
                points.Add(_grid.CellToWorld(cell));
            return new SaboteurBrain(IdentityA, _grid, _pathfinder, _claims, points);
        }

        AgentContext At(Vector2Int cell, float time) =>
            new AgentContext(cell, _grid.CellToWorld(cell), Vector3.forward, time, new WorldBlackboard(), default);

        [Test]
        public void FirstTickSendsAnAStarRouteToTheFirstPatrolPoint()
        {
            SaboteurBrain brain = Brain(new Vector2Int(5, 0), new Vector2Int(5, 5));

            AgentIntent intent = brain.Tick(At(new Vector2Int(0, 0), 0f));

            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(0, 0)), intent.Path[0]);
            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(5, 0)), intent.Path[intent.Path.Count - 1]);
            Assert.AreEqual(SaboteurBrain.MoveSpeed, intent.DesiredSpeed);
            Assert.AreEqual(AgentAction.None, intent.Action);
            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.AreEqual(SaboteurActionKind.Idle, brain.CurrentAction.Kind);
        }

        [Test]
        public void LaterTicksKeepTheCurrentRouteWithoutSearchingAgain()
        {
            SaboteurBrain brain = Brain(new Vector2Int(5, 0), new Vector2Int(5, 5));
            brain.Tick(At(new Vector2Int(0, 0), 0f));

            AgentIntent intent = brain.Tick(At(new Vector2Int(2, 0), 0.5f));

            Assert.IsNull(intent.Path);
            Assert.AreEqual(1, _pathfinder.Calls);
        }

        [Test]
        public void ReachingAPatrolPointRoutesToTheNextAndLoops()
        {
            var first = new Vector2Int(5, 0);
            var second = new Vector2Int(5, 5);
            SaboteurBrain brain = Brain(first, second);
            brain.Tick(At(new Vector2Int(0, 0), 0f));

            AgentIntent toSecond = brain.Tick(At(first, 1f));
            AgentIntent backToFirst = brain.Tick(At(second, 2f));

            Assert.AreEqual(_grid.CellToWorld(second), toSecond.Path[toSecond.Path.Count - 1]);
            Assert.AreEqual(_grid.CellToWorld(first), backToFirst.Path[backToFirst.Path.Count - 1]);
        }

        [Test]
        public void NoPatrolPointsHoldsInPlaceOnce()
        {
            SaboteurBrain brain = Brain();

            AgentIntent first = brain.Tick(At(new Vector2Int(3, 3), 0f));
            AgentIntent second = brain.Tick(At(new Vector2Int(3, 3), 0.1f));

            Assert.IsNotNull(first.Path);
            Assert.AreEqual(0, first.Path.Count);
            Assert.AreEqual("Hold", first.DebugState);
            Assert.IsNull(second.Path);
        }

        [Test]
        public void PatrolPointsOutsideTheGridAreIgnored()
        {
            var points = new List<Vector3> { new Vector3(-50f, 0f, -50f), _grid.CellToWorld(new Vector2Int(4, 4)) };
            var brain = new SaboteurBrain(IdentityA, _grid, _pathfinder, _claims, points);

            AgentIntent intent = brain.Tick(At(new Vector2Int(0, 0), 0f));

            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(4, 4)), intent.Path[intent.Path.Count - 1]);
        }

        [Test]
        public void BlockedPatrolPointSnapsToANearbyFreeCell()
        {
            var blocked = new Vector2Int(6, 6);
            _grid.SetWalkable(blocked, false);
            SaboteurBrain brain = Brain(blocked);

            AgentIntent intent = brain.Tick(At(new Vector2Int(0, 0), 0f));

            Vector2Int end = _grid.WorldToCell(intent.Path[intent.Path.Count - 1]);
            Assert.IsTrue(_grid.IsTraversable(end));
            Assert.LessOrEqual(Vector2Int.Distance(end, blocked), SaboteurBrain.PatrolSnapRadius * 1.5f);
        }

        [Test]
        public void UnreachablePatrolPointIsSkipped()
        {
            // Wall off the right-hand column so (9, 9) cannot be reached.
            for (int y = 0; y < 10; y++)
                _grid.SetWalkable(new Vector2Int(8, y), false);
            SaboteurBrain brain = Brain(new Vector2Int(9, 9), new Vector2Int(3, 5));

            AgentIntent intent = brain.Tick(At(new Vector2Int(0, 0), 0f));

            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(3, 5)), intent.Path[intent.Path.Count - 1]);
        }

        [Test]
        public void WhenNoPointIsReachableItHoldsAndDoesNotSearchEveryTick()
        {
            for (int y = 0; y < 10; y++)
                _grid.SetWalkable(new Vector2Int(8, y), false);
            SaboteurBrain brain = Brain(new Vector2Int(9, 9));

            AgentIntent first = brain.Tick(At(new Vector2Int(0, 0), 0f));
            int callsAfterFirst = _pathfinder.Calls;
            brain.Tick(At(new Vector2Int(0, 0), 0.05f));
            brain.Tick(At(new Vector2Int(0, 0), 0.10f));

            Assert.AreEqual("Hold", first.DebugState);
            Assert.AreEqual(0, first.Path.Count);
            Assert.AreEqual(callsAfterFirst, _pathfinder.Calls, "No search between selection passes.");

            brain.Tick(At(new Vector2Int(0, 0), 0.25f));
            Assert.Greater(_pathfinder.Calls, callsAfterFirst, "The next selection pass retries.");
        }

        [Test]
        public void GridChangeOnTheRouteTriggersANewRoute()
        {
            SaboteurBrain brain = Brain(new Vector2Int(6, 0));
            brain.Tick(At(new Vector2Int(0, 0), 0f));

            brain.OnGraphChanged(new[] { new Vector2Int(3, 0) });
            AgentIntent intent = brain.Tick(At(new Vector2Int(1, 0), 0.1f));

            Assert.IsNotNull(intent.Path);
            Assert.AreEqual(2, _pathfinder.Calls);
        }

        [Test]
        public void GridChangeOffTheRouteIsIgnored()
        {
            SaboteurBrain brain = Brain(new Vector2Int(6, 0));
            brain.Tick(At(new Vector2Int(0, 0), 0f));

            brain.OnGraphChanged(new[] { new Vector2Int(9, 9) });
            AgentIntent intent = brain.Tick(At(new Vector2Int(1, 0), 0.1f));

            Assert.IsNull(intent.Path);
            Assert.AreEqual(1, _pathfinder.Calls);
        }

        [Test]
        public void StunStopsOnceThenWaitsAndResumesWithAFreshRoute()
        {
            SaboteurBrain brain = Brain(new Vector2Int(6, 0));
            brain.Tick(At(new Vector2Int(0, 0), 0f));

            brain.OnStunned(2f);
            AgentIntent stopped = brain.Tick(At(new Vector2Int(2, 0), 1f));
            AgentIntent stillStunned = brain.Tick(At(new Vector2Int(2, 0), 2.5f));
            AgentIntent resumed = brain.Tick(At(new Vector2Int(2, 0), 3f));

            Assert.AreEqual(0, stopped.Path.Count);
            Assert.AreEqual("Stunned", stopped.DebugState);
            Assert.IsNull(stillStunned.Path);
            Assert.AreEqual("Stunned", stillStunned.DebugState);
            Assert.IsNotNull(resumed.Path);
            Assert.AreEqual("Patrol", resumed.DebugState);
        }

        [Test]
        public void StunReleasesThisInstancesClaims()
        {
            SaboteurBrain brain = Brain(new Vector2Int(6, 0));
            _claims.TryClaim(10, IdentityA.AgentId, 0.6f);

            brain.OnStunned(1f);

            Assert.IsNull(_claims.ClaimedBy(10));
        }

        [Test]
        public void DestructionReleasesOnlyThisInstancesClaims()
        {
            SaboteurBrain brain = Brain(new Vector2Int(6, 0));
            _claims.TryClaim(10, IdentityA.AgentId, 0.6f);
            _claims.TryClaim(11, 2, 0.6f);

            brain.OnDestroyed();

            Assert.IsTrue(brain.IsDestroyed);
            Assert.IsNull(_claims.ClaimedBy(10));
            Assert.AreEqual(2, _claims.ClaimedBy(11));
        }

        [Test]
        public void ADestroyedBrainOnlyEverStops()
        {
            SaboteurBrain brain = Brain(new Vector2Int(6, 0));
            brain.OnDestroyed();
            brain.OnStunned(1f);

            AgentIntent intent = brain.Tick(At(new Vector2Int(0, 0), 0f));

            Assert.AreEqual(0, intent.Path.Count);
            Assert.AreEqual(AgentAction.None, intent.Action);
            Assert.AreEqual("Destroyed", intent.DebugState);
            Assert.AreEqual(0, _pathfinder.Calls);
        }

        [Test]
        public void SaboteurAIsTheKeycardCarrier()
        {
            Assert.IsTrue(new SaboteurIdentity(0, SaboteurLetter.A).CarriesKeycard);
            Assert.IsFalse(new SaboteurIdentity(1, SaboteurLetter.B).CarriesKeycard);
        }

        [Test]
        public void IdentityRejectsANegativeId()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaboteurIdentity(-1, SaboteurLetter.C));
        }

        [Test]
        public void MissingDependenciesAreRejected()
        {
            var points = new List<Vector3>();
            Assert.Throws<ArgumentNullException>(() => new SaboteurBrain(IdentityA, null, _pathfinder, _claims, points));
            Assert.Throws<ArgumentNullException>(() => new SaboteurBrain(IdentityA, _grid, null, _claims, points));
            Assert.Throws<ArgumentNullException>(() => new SaboteurBrain(IdentityA, _grid, _pathfinder, null, points));
        }
    }
}
