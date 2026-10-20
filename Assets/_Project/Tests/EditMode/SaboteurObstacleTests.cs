using System.Collections.Generic;
using System.Linq;
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
    /// A Saboteur that meets a body in its way (another agent knocked down, a squad mate standing still) routes
    /// round it, at once when it sees it on the route ahead; one that gets nowhere for another reason prices the
    /// spot ahead as blocked and routes round that. The map is an open 40 x 21 grid; the Saboteur patrols from
    /// (5, 10) to (35, 10), straight along row 10.
    /// </summary>
    public class SaboteurObstacleTests
    {
        static readonly Vector2Int Start = new Vector2Int(5, 10);
        static readonly Vector2Int Goal = new Vector2Int(35, 10);

        // Exactly representable, so timers line up with whole frames.
        const float Frame = 1f / 64f;

        GridGraph _grid;
        WorldBlackboard _world;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(40, 21, Vector3.zero);
            _world = new WorldBlackboard();
        }

        SaboteurBrain Brain() =>
            new SaboteurBrain(new SaboteurIdentity(1, SaboteurLetter.A), _grid, new AStarSearch(_grid), new TargetClaims(),
                new List<Vector3> { _grid.CellToWorld(Goal) }, null, 0, null, null, SaboteurOptions.WaypointsOnly);

        AgentContext At(Vector2Int cell, float time) =>
            new AgentContext(cell, _grid.CellToWorld(cell), Vector3.right, time, _world, default);

        CrowdMember Body(Vector2Int cell, float speed = 0f) => new CrowdMember(_grid.CellToWorld(cell), speed);

        bool PassesNear(IReadOnlyList<Vector3> path, Vector2Int cell, float metres)
        {
            Vector3 at = _grid.CellToWorld(cell);
            return path.Any(p => new Vector2(p.x - at.x, p.z - at.z).magnitude <= metres);
        }

        // ---- The cost model

        [Test]
        public void AStepIntoAStandingBodyOrABlockedSpotCostsMoreAndNoStepCostsLessThanTheBase()
        {
            var cost = new SaboteurRouteCost(_grid);
            cost.SetCrowd(new[] { Body(new Vector2Int(20, 10)), Body(new Vector2Int(20, 15), speed: 2f) });
            cost.SetTime(0f);
            cost.Block(new Vector2Int(10, 5), 8f);

            float straight = BaseCostModel.Instance.StepCost(new Vector2Int(19, 10), new Vector2Int(20, 10));
            Assert.AreEqual(straight * (1f + SaboteurRouteCost.BlockedPenalty), cost.StepCost(new Vector2Int(19, 10), new Vector2Int(20, 10)), 1e-4f);
            Assert.AreEqual(straight, cost.StepCost(new Vector2Int(19, 15), new Vector2Int(20, 15)), 1e-4f, "A moving agent will have moved on.");
            Assert.IsTrue(cost.InTheWay(new Vector2Int(10, 5)), "The learned spot.");
            Assert.IsFalse(cost.InTheWay(new Vector2Int(30, 10)));

            for (int x = 0; x < _grid.Width - 1; x++)
                for (int y = 0; y < _grid.Height; y++)
                    Assert.GreaterOrEqual(cost.StepCost(new Vector2Int(x, y), new Vector2Int(x + 1, y)),
                        BaseCostModel.Instance.StepCost(new Vector2Int(x, y), new Vector2Int(x + 1, y)) - 1e-5f);
        }

        [Test]
        public void ABlockedSpotIsForgottenAfterItsTime()
        {
            var cost = new SaboteurRouteCost(_grid);
            cost.SetTime(0f);
            cost.Block(new Vector2Int(10, 5), SaboteurRouteCost.BlockedSeconds);

            cost.SetTime(SaboteurRouteCost.BlockedSeconds - 0.1f);
            Assert.IsTrue(cost.IsBlocked(new Vector2Int(10, 5)));
            cost.SetTime(SaboteurRouteCost.BlockedSeconds + 0.1f);
            Assert.IsFalse(cost.IsBlocked(new Vector2Int(10, 5)));
        }

        // ---- Routing round a body

        [Test]
        public void TheRouteGoesRoundAKnockedDownAgentLyingInTheWay()
        {
            SaboteurBrain brain = Brain();
            brain.OnCrowd(new[] { Body(new Vector2Int(20, 10)) });

            AgentIntent intent = brain.Tick(At(Start, 0f));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNotNull(intent.Path);
            Assert.IsFalse(PassesNear(intent.Path, new Vector2Int(20, 10), SaboteurRouteCost.BodyRadius),
                "It walks round the body, not into it.");
        }

        [Test]
        public void ADetourRoundABodyIsSentAsTwoWaypointLegsTheRuntimeCannotPullStraightThroughIt()
        {
            SaboteurBrain brain = Brain();
            brain.OnCrowd(new[] { Body(new Vector2Int(20, 10)) });

            AgentIntent first = brain.Tick(At(Start, 0f));

            // Two waypoints: the runtime only smooths a longer path, and smoothing over the grid would cut the corner
            // back through the body, which the grid does not know about.
            Assert.AreEqual(2, first.Path.Count, "A leg, not the whole route.");
            var cost = new SaboteurRouteCost(_grid);
            cost.SetCrowd(new[] { Body(new Vector2Int(20, 10)) });
            Assert.IsTrue(cost.ClearLine(_grid.CellToWorld(Start), first.Path[1]), "The leg is a straight walk clear of the body.");

            // At the end of the leg, the next one is sent.
            Vector2Int legEnd = _grid.WorldToCell(first.Path[1]);
            AgentIntent next = brain.Tick(At(legEnd, 1f));
            Assert.IsNotNull(next.Path, "The next leg.");
            Assert.Greater(next.Path.Count, 0);
            Assert.AreNotEqual(first.Path[1], next.Path[next.Path.Count - 1], "It goes on from there.");
        }

        [Test]
        public void ARouteFarFromAnyBodyIsSentWhole()
        {
            SaboteurBrain brain = Brain();
            brain.OnCrowd(new[] { Body(new Vector2Int(20, 18)) });   // 4 m off the route

            AgentIntent intent = brain.Tick(At(Start, 0f));

            Assert.Greater(intent.Path.Count, 2, "The whole route, as before.");
        }
        [Test]
        public void AnAgentWalkingAcrossTheRouteIsNotWalkedRound()
        {
            SaboteurBrain brain = Brain();
            brain.OnCrowd(new[] { Body(new Vector2Int(20, 10), speed: 3f) });

            AgentIntent intent = brain.Tick(At(Start, 0f));

            Assert.IsTrue(PassesNear(intent.Path, new Vector2Int(20, 10), 0.3f), "The straight line: it will have moved on.");
        }

        [Test]
        public void ABodyThatFallsOntoTheRouteAheadIsRoutedRoundAtOnceNotAfterTheStuckWait()
        {
            SaboteurBrain brain = Brain();
            AgentIntent first = brain.Tick(At(Start, 0f));
            Assert.IsTrue(PassesNear(first.Path, new Vector2Int(9, 10), 0.3f), "The straight line, at first.");

            // An agent is knocked down 2 m ahead, on the route.
            brain.OnCrowd(new[] { Body(new Vector2Int(9, 10)) });
            AgentIntent next = brain.Tick(At(new Vector2Int(6, 10), Frame));

            Assert.IsNotNull(next.Path, "A new route straight away, well before the 4 s stuck wait.");
            Assert.Greater(next.Path.Count, 0);
            Assert.IsFalse(PassesNear(next.Path, new Vector2Int(9, 10), SaboteurRouteCost.BodyRadius));

            // Whatever it is sent next (the same leg kept, or the next leg once it stands at the end of this one) stays clear.
            var cost = new SaboteurRouteCost(_grid);
            cost.SetCrowd(new[] { Body(new Vector2Int(9, 10)) });
            AgentIntent after = brain.Tick(At(new Vector2Int(6, 11), 2 * Frame));
            if (after.Path != null && after.Path.Count > 0)
                Assert.IsTrue(cost.ClearLine(_grid.CellToWorld(new Vector2Int(6, 11)), after.Path[after.Path.Count - 1]));
        }

        void Corridor()
        {
            // A corridor one cell wide along row 10, and a second one along row 4 joined to it at x = 5.
            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                for (int x = 0; x < _grid.Width; x++)
                    for (int y = 0; y < _grid.Height; y++)
                        if (y != 10 && !(y == 4 && x >= 5) && !(x == 5 && y >= 4 && y <= 10))
                            batch.SetWalkable(new Vector2Int(x, y), false);
                batch.Commit();
            }
        }

        [Test]
        public void WithNoWayRoundItWaitsInsteadOfPushingIntoTheBodyAndGoesOnOnceItGetsUp()
        {
            Corridor();
            SaboteurBrain brain = Brain();
            brain.OnCrowd(new[] { Body(new Vector2Int(20, 10)) });

            AgentIntent waiting = brain.Tick(At(Start, 0f));
            Assert.AreEqual("Hold", waiting.DebugState, "The only way is through the body: it waits rather than push into it.");

            AgentIntent still = brain.Tick(At(Start, SaboteurBrain.BodyReplanSeconds * 0.5f));
            Assert.AreEqual("Hold", still.DebugState);

            // The knocked-down agent reboots and walks off.
            brain.OnCrowd(new CrowdMember[0]);
            AgentIntent going = brain.Tick(At(Start, SaboteurBrain.BodyReplanSeconds + Frame));
            Assert.AreEqual("Patrol", going.DebugState, "Looked again after a second: the way is clear.");
            Assert.IsNotNull(going.Path);
        }

        [Test]
        public void APatrolPointBehindABodyInAnAisleIsSkippedForOneThatCanBeReached()
        {
            Corridor();
            var behind = new Vector2Int(35, 10);
            var other = new Vector2Int(35, 4);
            var brain = new SaboteurBrain(new SaboteurIdentity(1, SaboteurLetter.A), _grid, new AStarSearch(_grid), new TargetClaims(),
                new List<Vector3> { _grid.CellToWorld(behind), _grid.CellToWorld(other) }, null, 0, null, null, SaboteurOptions.WaypointsOnly);
            brain.OnCrowd(new[] { Body(new Vector2Int(20, 10)) });

            AgentIntent intent = brain.Tick(At(Start, 0f));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.AreEqual(_grid.CellToWorld(other), intent.Path[intent.Path.Count - 1], "The other point, by the other corridor.");
        }

        [Test]
        public void ARouteThatStartsPressedAgainstABodyAndTurnsAwayIsNotThroughIt()
        {
            var cost = new SaboteurRouteCost(_grid);
            cost.SetCrowd(new[] { Body(new Vector2Int(20, 10)) });
            Vector3 pressed = _grid.CellToWorld(new Vector2Int(18, 10));   // 1 m off, touching it

            var away = new List<Vector2Int> { new Vector2Int(18, 10), new Vector2Int(17, 10), new Vector2Int(16, 10) };
            var into = new List<Vector2Int> { new Vector2Int(18, 10), new Vector2Int(19, 10), new Vector2Int(20, 10), new Vector2Int(21, 10), new Vector2Int(25, 10) };

            Assert.IsFalse(cost.GoesThroughBody(away, pressed));
            Assert.IsTrue(cost.GoesThroughBody(into, pressed));
        }
        // ---- Something the runtime does not report

        [Test]
        public void AWalkThatGetsNowherePricesTheSpotAheadAsBlockedAndTheRouteSentAgainGoesRoundIt()
        {
            SaboteurBrain brain = Brain();
            brain.Tick(At(Start, 0f));

            // Something it cannot see (a scrapped body, a crate the grid does not know) holds it at (8, 10).
            var held = new Vector2Int(8, 10);
            AgentIntent resent = default;
            float t = 0.5f;
            for (; t <= SaboteurBrain.TravelStuckSeconds + 1f; t += 0.5f)
            {
                resent = brain.Tick(At(held, t));
                if (resent.Path != null && resent.Path.Count > 0)
                    break;
            }

            Assert.IsNotNull(resent.Path, "Sent again after the stuck wait.");
            Assert.IsTrue(brain.RouteCost.IsBlocked(new Vector2Int(9, 10)) || brain.RouteCost.IsBlocked(new Vector2Int(10, 10)),
                "The cells just ahead are priced as blocked.");
            Assert.IsFalse(PassesNear(resent.Path, new Vector2Int(10, 10), 0.3f), "The route sent again goes round them.");
        }
    }
}
