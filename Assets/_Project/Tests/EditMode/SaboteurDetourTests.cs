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
    /// The door-closure cost model, the shared detour cache and the CloseDoor action. The map is a
    /// wall at x = 20 with two doors: door 1 at y = 10, on the straight line between the player
    /// (5, 10) and the objective (35, 10), and door 2 at y = 0, far off that line.
    /// </summary>
    public class SaboteurDetourTests
    {
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

        // Prices one cell at infinity, like a closed door.
        sealed class BlockedCellCost : ICostModel
        {
            readonly Vector2Int _blocked;
            public BlockedCellCost(Vector2Int blocked) { _blocked = blocked; }

            public float StepCost(Vector2Int from, Vector2Int to) =>
                to == _blocked ? float.PositiveInfinity : BaseCostModel.Instance.StepCost(from, to);
        }

        static readonly Vector2Int Door1Cell = new Vector2Int(20, 10);
        static readonly Vector2Int Door2Cell = new Vector2Int(20, 0);
        static readonly Vector2Int PlayerCell = new Vector2Int(5, 10);
        static readonly Vector2Int ObjectiveCell = new Vector2Int(35, 10);

        GridGraph _grid;
        CountingPathfinder _pathfinder;
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

            _pathfinder = new CountingPathfinder(_grid);
            _claims = new TargetClaims();
            _world = new WorldBlackboard();
            SetPlayer(PlayerCell);
            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, ObjectiveCell, ObjectiveTargetKind.Task) });
        }

        void SetPlayer(Vector2Int cell, bool alive = true) =>
            _world.SetPlayer(new PlayerSnapshot(true, cell, _grid.CellToWorld(cell), Vector3.zero, Vector3.forward,
                5f, alive, 1f, 1f, false, 0f, 0f));

        DetourCache Cache() => new DetourCache(_grid, _pathfinder);

        static DoorDetour Door(DetourCache cache, int id)
        {
            Assert.IsTrue(cache.TryGet(id, out DoorDetour detour), $"Door {id} should have a result.");
            return detour;
        }

        // ---- A8: how A* treats an infinite step cost

        [Test]
        public void AStarRoutesAroundAnInfinitelyPricedCellWhenAnotherRouteExists()
        {
            var a = new Vector2Int(2, 10);
            var b = new Vector2Int(8, 10);
            var blocked = new BlockedCellCost(new Vector2Int(5, 10));

            PathResult result = new AStarSearch(_grid).FindPath(a, b, blocked);

            Assert.IsTrue(result.Found);
            Assert.IsFalse(result.Cells.Contains(new Vector2Int(5, 10)));
            Assert.IsFalse(float.IsInfinity(DoorClosureCostModel.PathCost(result.Cells, blocked)));
        }

        [Test]
        public void AStarThroughTheOnlyRouteReturnsARouteWhoseCostIsInfinite()
        {
            // The documented behaviour the cache relies on: A* does not fail on an infinite step, it
            // finds the route and the route's cost comes back infinite. A lockout is therefore read
            // from the summed cost, not from PathResult.Found.
            var model = new DoorClosureCostModel();
            model.SetClosed(new[] { Door1Cell, Door2Cell });

            PathResult result = new AStarSearch(_grid).FindPath(PlayerCell, ObjectiveCell, model);

            Assert.IsTrue(result.Found);
            Assert.IsTrue(float.IsPositiveInfinity(DoorClosureCostModel.PathCost(result.Cells, model)));
        }

        [Test]
        public void AStarWithInfiniteCostsTerminatesAndNeverReturnsNaN()
        {
            var model = new DoorClosureCostModel();
            model.SetClosed(new[] { Door1Cell, Door2Cell });

            for (int i = 0; i < 3; i++)
            {
                PathResult result = new AStarSearch(_grid).FindPath(PlayerCell, ObjectiveCell, model);
                Assert.IsFalse(float.IsNaN(DoorClosureCostModel.PathCost(result.Cells, model)));
            }
        }

        // ---- The cost model

        [Test]
        public void ClosureModelChargesTheBaseStepAwayFromTheDoor()
        {
            var model = new DoorClosureCostModel();
            model.SetClosed(new[] { Door1Cell });

            Assert.AreEqual(BaseCostModel.StraightCost, model.StepCost(new Vector2Int(3, 3), new Vector2Int(4, 3)));
            Assert.AreEqual(BaseCostModel.DiagonalCost, model.StepCost(new Vector2Int(3, 3), new Vector2Int(4, 4)));
        }

        [Test]
        public void ClosureModelPricesStepsIntoAndOutOfTheDoorAtInfinity()
        {
            var model = new DoorClosureCostModel();
            model.SetClosed(new[] { Door1Cell });

            Assert.IsTrue(float.IsPositiveInfinity(model.StepCost(new Vector2Int(19, 10), Door1Cell)));
            Assert.IsTrue(float.IsPositiveInfinity(model.StepCost(Door1Cell, new Vector2Int(21, 10))));
        }

        [Test]
        public void ClosureModelAlsoBlocksTheDiagonalThatCutsTheDoorsCorner()
        {
            var model = new DoorClosureCostModel();
            model.SetClosed(new[] { new Vector2Int(5, 5) });

            // (4,4) -> (5,6) is not diagonal; (4,5) -> (5,6) cuts past (5,5) as a side cell.
            Assert.IsTrue(float.IsPositiveInfinity(model.StepCost(new Vector2Int(4, 5), new Vector2Int(5, 6))));
            Assert.IsFalse(float.IsInfinity(model.StepCost(new Vector2Int(4, 6), new Vector2Int(5, 7))));
        }

        [Test]
        public void ClearedClosureModelPricesLikeTheBaseModel()
        {
            var model = new DoorClosureCostModel();
            model.SetClosed(new[] { Door1Cell });
            model.Clear();

            Assert.AreEqual(0, model.ClosedCellCount);
            Assert.AreEqual(BaseCostModel.StraightCost, model.StepCost(new Vector2Int(19, 10), Door1Cell));
        }

        [Test]
        public void PathCostSumsTheSteps()
        {
            var cells = new List<Vector2Int> { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 1) };

            Assert.AreEqual(BaseCostModel.StraightCost + BaseCostModel.DiagonalCost,
                DoorClosureCostModel.PathCost(cells, BaseCostModel.Instance), 1e-5f);
            Assert.AreEqual(0f, DoorClosureCostModel.PathCost(new List<Vector2Int> { new Vector2Int(1, 1) }, BaseCostModel.Instance));
        }

        [Test]
        public void ClosureModelRejectsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => new DoorClosureCostModel().SetClosed(null));
            Assert.Throws<System.ArgumentNullException>(() => DoorClosureCostModel.PathCost(null, BaseCostModel.Instance));
        }

        // ---- The gain formula

        [Test]
        public void GainIsTheRelativeIncreaseOfTheRoute()
        {
            Assert.AreEqual(0.6f, DetourCache.GainOf(10f, 16f), 1e-5f);
            Assert.AreEqual(0.2f, DetourCache.GainOf(10f, 12f), 1e-5f);
            Assert.AreEqual(0f, DetourCache.GainOf(10f, 10f));
        }

        [Test]
        public void ADetourOfTenToSixteenRanksAboveTenToTwelve()
        {
            Assert.Greater(DetourCache.GainScore(DetourCache.GainOf(10f, 16f)), DetourCache.GainScore(DetourCache.GainOf(10f, 12f)));
        }

        [Test]
        public void LockoutHasAnInfiniteGainButScoresZero()
        {
            float gain = DetourCache.GainOf(10f, float.PositiveInfinity);

            Assert.IsTrue(float.IsPositiveInfinity(gain));
            Assert.AreEqual(0f, DetourCache.GainScore(gain));
        }

        [Test]
        public void GainScoreSaturatesAtAHundredPercent()
        {
            Assert.AreEqual(1f, DetourCache.GainScore(1f));
            Assert.AreEqual(1f, DetourCache.GainScore(3f));
            Assert.AreEqual(0.4f, DetourCache.GainScore(0.4f), 1e-6f);
            Assert.AreEqual(0f, DetourCache.GainScore(-1f));
            Assert.AreEqual(0f, DetourCache.GainScore(float.NaN));
        }

        [Test]
        public void GainIsZeroWhenTheOpenRouteIsFree()
        {
            Assert.AreEqual(0f, DetourCache.GainOf(0f, 5f));
            Assert.AreEqual(0f, DetourCache.GainOf(float.PositiveInfinity, float.PositiveInfinity));
        }

        // ---- The cache

        [Test]
        public void DoorOnThePlayersRouteHasAPositiveGainAndADoorOffItHasNone()
        {
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            Assert.IsTrue(cache.HasObjective);
            DoorDetour onRoute = Door(cache, 1);
            DoorDetour offRoute = Door(cache, 2);
            Assert.Greater(onRoute.Gain, 0f);
            Assert.IsFalse(onRoute.IsLockout);
            Assert.Greater(onRoute.ClosedCost, onRoute.OpenCost);
            Assert.AreEqual(0f, offRoute.Gain, 1e-6f);
            Assert.AreEqual(offRoute.OpenCost, offRoute.ClosedCost);
        }

        [Test]
        public void OpenRouteCostMatchesTheStraightLine()
        {
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            Assert.AreEqual(30f, Door(cache, 1).OpenCost, 1e-4f);
            Assert.AreEqual(ObjectiveCell, cache.ObjectiveCell);
        }

        [Test]
        public void OnlyDoorsOnTheRouteAreSearched()
        {
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            // One search for the open route and one for door 1; door 2 is nowhere near it.
            Assert.AreEqual(2, _pathfinder.Calls);
            Assert.AreEqual(2, cache.Searches);
        }

        [Test]
        public void OnlyRouteForcesALockoutAndIsReported()
        {
            _grid.SetWalkable(Door2Cell, false);
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            DoorDetour door = Door(cache, 1);
            Assert.IsTrue(door.IsLockout);
            Assert.IsTrue(float.IsPositiveInfinity(door.Gain));
        }

        [Test]
        public void TheLiveGridIsNeverChanged()
        {
            int version = _grid.Version;
            DetourCache cache = Cache();

            cache.Refresh(_world, 0f);
            cache.Refresh(_world, 1f);

            Assert.AreEqual(version, _grid.Version);
            Assert.IsFalse(_grid.GetNode(Door1Cell).IsDoorClosed);
            Assert.IsFalse(_grid.GetNode(Door2Cell).IsDoorClosed);
            Assert.IsTrue(_grid.IsTraversable(Door1Cell));
        }

        [Test]
        public void ClosedDoorIsNotListed()
        {
            _grid.SetDoor(Door1Cell, 1, true);
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            Assert.IsFalse(cache.TryGet(1, out _));
            Assert.IsTrue(cache.TryGet(2, out _));
            Assert.IsTrue(cache.TryGetCells(1, out IReadOnlyList<Vector2Int> cells), "The cells of a closed door are still known.");
            Assert.AreEqual(Door1Cell, cells[0]);
        }

        [Test]
        public void RefreshRunsOnlyEveryHalfSecond()
        {
            DetourCache cache = Cache();

            cache.Refresh(_world, 0f);
            cache.Refresh(_world, 0.25f);
            cache.Refresh(_world, DetourCache.RefreshSeconds - 0.01f);
            Assert.AreEqual(1, cache.Refreshes);

            cache.Refresh(_world, DetourCache.RefreshSeconds);
            Assert.AreEqual(2, cache.Refreshes);
        }

        [Test]
        public void GridChangeRefreshesAtOnce()
        {
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            _grid.SetDoor(Door1Cell, 1, true);
            cache.Refresh(_world, 0.1f);

            Assert.AreEqual(2, cache.Refreshes);
            Assert.IsFalse(cache.TryGet(1, out _));
        }

        [Test]
        public void ObjectiveChangeRefreshesAtOnce()
        {
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(8, new Vector2Int(35, 15), ObjectiveTargetKind.Task) });
            cache.Refresh(_world, 0.1f);

            Assert.AreEqual(2, cache.Refreshes);
            Assert.AreEqual(new Vector2Int(35, 15), cache.ObjectiveCell);
        }

        [Test]
        public void FourSaboteursAskingCostTheSameSearchesAsOne()
        {
            DetourCache one = new DetourCache(_grid, _pathfinder);
            one.Refresh(_world, 0f);
            int single = _pathfinder.Calls;

            var pathfinder = new CountingPathfinder(_grid);
            var shared = new DetourCache(_grid, pathfinder);
            for (int saboteur = 0; saboteur < 4; saboteur++)
                shared.Refresh(_world, saboteur * SquadCoordinator.StaggerStep);

            Assert.AreEqual(single, pathfinder.Calls);
            Assert.AreEqual(1, shared.Refreshes);
        }

        [Test]
        public void ConfidentPredictionReplacesTheNearestObjective()
        {
            var predicted = new Vector2Int(35, 3);
            _world.SetPredictedGoal(new PredictedGoal(9, predicted, 0.8f, 0f));
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            Assert.AreEqual(predicted, cache.ObjectiveCell);
        }

        [Test]
        public void UnconfidentPredictionIsIgnored()
        {
            _world.SetPredictedGoal(new PredictedGoal(9, new Vector2Int(35, 3), DetourCache.MinPredictionConfidence - 0.01f, 0f));
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            Assert.AreEqual(ObjectiveCell, cache.ObjectiveCell);
        }

        [Test]
        public void NearestObjectiveIsChosenFromTheActiveTargets()
        {
            _world.SetObjectiveTargets(new[]
            {
                new ObjectiveTarget(1, new Vector2Int(38, 18), ObjectiveTargetKind.Task),
                new ObjectiveTarget(2, new Vector2Int(30, 10), ObjectiveTargetKind.Task)
            });
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            Assert.AreEqual(new Vector2Int(30, 10), cache.ObjectiveCell);
        }

        [Test]
        public void NoObjectiveMeansNoDetours()
        {
            _world.SetObjectiveTargets(new ObjectiveTarget[0]);
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            Assert.IsFalse(cache.HasObjective);
            Assert.IsEmpty(cache.Doors);
        }

        [Test]
        public void DeadOrUnknownPlayerMeansNoDetours()
        {
            SetPlayer(PlayerCell, alive: false);
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);
            Assert.IsFalse(cache.HasObjective);

            _world = new WorldBlackboard();
            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, ObjectiveCell, ObjectiveTargetKind.Task) });
            cache.Refresh(_world, 1f);
            Assert.IsFalse(cache.HasObjective, "An unknown player has no route.");
        }

        [Test]
        public void PlayerAlreadyAtTheObjectiveMeansNoDetours()
        {
            SetPlayer(ObjectiveCell);
            DetourCache cache = Cache();
            cache.Refresh(_world, 0f);

            Assert.IsFalse(cache.HasObjective);
        }

        [Test]
        public void CacheRejectsNullArguments()
        {
            Assert.Throws<System.ArgumentNullException>(() => new DetourCache(null, _pathfinder));
            Assert.Throws<System.ArgumentNullException>(() => new DetourCache(_grid, null));
            Assert.Throws<System.ArgumentNullException>(() => Cache().Refresh(null, 0f));
        }

        // ---- The CloseDoor source

        List<ActionCandidate> Offers(DetourCache cache, Vector2Int saboteurCell, float time = 0f)
        {
            var list = new List<ActionCandidate>();
            var ctx = new AgentContext(saboteurCell, _grid.CellToWorld(saboteurCell), Vector3.forward, time, _world, default);
            new CloseDoorSource(cache).AddCandidates(ctx, new SaboteurIdentity(1, SaboteurLetter.A), list);
            return list;
        }

        [Test]
        public void SourceOffersTheDoorOnThePlayersRoute()
        {
            List<ActionCandidate> list = Offers(Cache(), new Vector2Int(14, 10));

            Assert.AreEqual(1, list.Count, "Door 2 is off the route and door 1 is the only useful one.");
            Assert.AreEqual(new ActionKey(SaboteurActionKind.CloseDoor, 1), list[0].Key);
            Assert.Greater(list[0].BaseScore, SaboteurBrain.IdleScore);
        }

        [Test]
        public void NearerSaboteurScoresHigher()
        {
            DetourCache cache = Cache();
            float near = Offers(cache, new Vector2Int(18, 10))[0].BaseScore;
            float far = Offers(cache, new Vector2Int(8, 10))[0].BaseScore;

            Assert.Greater(near, far);
        }

        [Test]
        public void DistanceToTheDoorIsMeasuredInMetres()
        {
            // 20 cells of 0.5 m.
            Assert.AreEqual(10f, CloseDoorSource.NearestDistanceMetres(new Vector2Int(0, 10), new[] { Door1Cell }), 1e-4f);
            Assert.AreEqual(0f, CloseDoorSource.NearestDistanceMetres(Door1Cell, new[] { Door1Cell }));
            Assert.AreEqual(5f, CloseDoorSource.NearestDistanceMetres(new Vector2Int(10, 10), new[] { Door2Cell, Door1Cell }), 1e-4f,
                "The nearer of a door's cells counts.");
        }

        [Test]
        public void LockoutDoorIsNotOffered()
        {
            _grid.SetWalkable(Door2Cell, false);

            Assert.IsEmpty(Offers(Cache(), new Vector2Int(14, 10)));
        }

        [Test]
        public void DoorWithNoDetourIsNotOffered()
        {
            // The player is past the wall already: neither door is on the way to the objective.
            SetPlayer(new Vector2Int(30, 10));

            Assert.IsEmpty(Offers(Cache(), new Vector2Int(14, 10)));
        }

        [Test]
        public void DoorThePlayerStandsInIsNotOffered()
        {
            SetPlayer(new Vector2Int(19, 10));

            Assert.IsEmpty(Offers(Cache(), new Vector2Int(14, 10)));
        }

        [Test]
        public void ClosedDoorIsNotOffered()
        {
            _grid.SetDoor(Door1Cell, 1, true);

            Assert.IsEmpty(Offers(Cache(), new Vector2Int(14, 10)));
        }

        [Test]
        public void DoorwayCheckUsesAOneCellClearance()
        {
            Assert.IsTrue(CloseDoorSource.IsInDoorway(new Vector2Int(21, 11), new[] { Door1Cell }));
            Assert.IsFalse(CloseDoorSource.IsInDoorway(new Vector2Int(22, 10), new[] { Door1Cell }));
        }

        [Test]
        public void SourceWithoutAWorldOffersNothing()
        {
            var list = new List<ActionCandidate>();
            var ctx = new AgentContext(new Vector2Int(14, 10), Vector3.zero, Vector3.forward, 0f, null, default);

            new CloseDoorSource(Cache()).AddCandidates(ctx, new SaboteurIdentity(1, SaboteurLetter.A), list);

            Assert.IsEmpty(list);
        }

        [Test]
        public void CompositeSourceCombinesItsSourcesInOrder()
        {
            var composite = new CompositeCandidateSource(new CloseDoorSource(Cache()), new AttackPlayerSource(new AlwaysSight()));
            var list = new List<ActionCandidate>();
            SetPlayer(new Vector2Int(14, 11));
            var ctx = new AgentContext(new Vector2Int(14, 10), _grid.CellToWorld(new Vector2Int(14, 10)), Vector3.forward, 0f, _world, default);

            composite.AddCandidates(ctx, new SaboteurIdentity(1, SaboteurLetter.A), list);

            Assert.AreEqual(SaboteurActionKind.CloseDoor, list[0].Key.Kind);
            Assert.AreEqual(SaboteurActionKind.AttackPlayer, list[list.Count - 1].Key.Kind);
            Assert.Throws<System.ArgumentException>(() => new CompositeCandidateSource(new ICandidateSource[] { null }));
        }

        sealed class AlwaysSight : IPlayerSight
        {
            public bool CanSeeCell(Vector2Int cell) => true;
        }

        // ---- The brain carrying out CloseDoor

        SaboteurBrain Brain(SaboteurLetter letter, DetourCache cache, Vector2Int patrolCell)
        {
            var identity = new SaboteurIdentity((int)letter + 1, letter);
            return new SaboteurBrain(identity, _grid, _pathfinder, _claims,
                new List<Vector3> { _grid.CellToWorld(patrolCell) }, null, 0, new CloseDoorSource(cache), cache);
        }

        AgentContext At(Vector2Int cell, float time) =>
            new AgentContext(cell, _grid.CellToWorld(cell), Vector3.forward, time, _world, default);

        [Test]
        public void SaboteurFarFromTheDoorWalksToIt()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache, new Vector2Int(2, 2));

            AgentIntent first = brain.Tick(At(new Vector2Int(10, 10), 0f));

            Assert.AreEqual(new ActionKey(SaboteurActionKind.CloseDoor, 1), brain.CurrentAction);
            Assert.AreEqual("CloseDoor", first.DebugState);
            Assert.AreEqual(AgentAction.None, first.Action);
            Assert.IsNotNull(first.Path);
            Assert.AreEqual(_grid.CellToWorld(Door1Cell), first.Path[first.Path.Count - 1]);
            Assert.AreEqual(1, _claims.ClaimedBy(1), "The Saboteur claimed the door.");

            AgentIntent second = brain.Tick(At(new Vector2Int(11, 10), 0.1f));
            Assert.IsNull(second.Path, "The route is sent once.");
            Assert.AreEqual("CloseDoor", second.DebugState);
        }

        [Test]
        public void SaboteurWithinReachStopsAndAsksToCloseTheDoor()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache, new Vector2Int(2, 2));

            AgentIntent intent = brain.Tick(At(new Vector2Int(19, 10), 0f));

            Assert.AreEqual(AgentAction.CloseDoor, intent.Action);
            Assert.AreEqual(1, intent.ActionTargetId, "The door id.");
            Assert.AreEqual("CloseDoor", intent.DebugState);
            Assert.AreEqual(0, intent.Path.Count, "The body stops once at the door.");
            Assert.AreEqual(_grid.CellToWorld(Door1Cell), intent.LookTarget);

            AgentIntent again = brain.Tick(At(new Vector2Int(19, 10), 0.3f));
            Assert.AreEqual(AgentAction.CloseDoor, again.Action);
            Assert.IsNull(again.Path);
        }

        [Test]
        public void ClosedDoorEndsThePlanReleasesTheClaimAndSendsTheSaboteurBackToPatrol()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache, new Vector2Int(2, 2));
            brain.Tick(At(new Vector2Int(19, 10), 0f));
            Assert.AreEqual(1, _claims.ClaimedBy(1));

            _grid.SetDoor(Door1Cell, 1, true);
            AgentIntent intent = brain.Tick(At(new Vector2Int(19, 10), 0.3f));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNotNull(intent.Path, "A fresh patrol route.");
            Assert.IsNull(_claims.ClaimedBy(1));
            Assert.AreNotEqual(SaboteurActionKind.CloseDoor, brain.CurrentAction.Kind);
        }

        [Test]
        public void DoorThatStaysOpenIsGivenUpOnAndNotRetriedAtOnce()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache, new Vector2Int(2, 2));
            Vector2Int at = new Vector2Int(19, 10);
            brain.Tick(At(at, 0f));

            AgentIntent intent = default;
            float t = 0f;
            for (; t <= SaboteurBrain.CloseDoorTimeoutSeconds + 1f; t += 0.25f)
                intent = brain.Tick(At(at, t));

            Assert.AreNotEqual(AgentAction.CloseDoor, intent.Action, "It gave up asking.");
            Assert.IsNull(_claims.ClaimedBy(1), "The claim is released.");

            // The door is still open and still useful, but is on cooldown for a while.
            for (float later = t; later < t + 5f; later += 0.25f)
            {
                brain.Tick(At(at, later));
                Assert.AreNotEqual(SaboteurActionKind.CloseDoor, brain.CurrentAction.Kind, $"At {later:F2}.");
            }
        }

        [Test]
        public void SecondSaboteurDoesNotTakeTheDoorTheFirstHasClaimed()
        {
            DetourCache cache = Cache();
            SaboteurBrain a = Brain(SaboteurLetter.A, cache, new Vector2Int(2, 2));
            SaboteurBrain b = Brain(SaboteurLetter.B, cache, new Vector2Int(2, 3));

            a.Tick(At(new Vector2Int(12, 10), 0f));
            b.Tick(At(new Vector2Int(12, 11), 0f));
            b.Tick(At(new Vector2Int(12, 11), SquadCoordinator.StaggerStep));

            Assert.AreEqual(new ActionKey(SaboteurActionKind.CloseDoor, 1), a.CurrentAction);
            Assert.AreEqual(SaboteurActionKind.Idle, b.CurrentAction.Kind);
            Assert.AreEqual(1, _claims.ClaimedBy(1));
        }

        [Test]
        public void RouteIsSentAgainWhenADoorOnItChanges()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache, new Vector2Int(2, 2));
            AgentIntent first = brain.Tick(At(new Vector2Int(10, 10), 0f));
            Assert.IsNotNull(first.Path);

            brain.OnGraphChanged(new[] { new Vector2Int(15, 10) });
            AgentIntent next = brain.Tick(At(new Vector2Int(11, 10), 0.1f));

            Assert.IsNotNull(next.Path, "A changed cell on the route means a new route.");
        }

        [Test]
        public void StunDuringTheDoorPlanReleasesTheClaimAndResumesAtTheDoor()
        {
            DetourCache cache = Cache();
            SaboteurBrain brain = Brain(SaboteurLetter.A, cache, new Vector2Int(2, 2));
            brain.Tick(At(new Vector2Int(19, 10), 0f));

            brain.OnStunned(2f);
            Assert.IsNull(_claims.ClaimedBy(1), "The claim is released while the Saboteur is down.");

            AgentIntent intent = brain.Tick(At(new Vector2Int(19, 10), 3f));
            Assert.AreEqual(AgentAction.CloseDoor, intent.Action, "The door is still worth closing.");
            Assert.IsNotNull(intent.Path, "The body is stopped at the door again after the stun.");
        }

        [Test]
        public void SelectorFailureStartsTheSameCooldownAsSuccess()
        {
            var selector = new ActionSelector();
            var key = new ActionKey(SaboteurActionKind.CloseDoor, 1);

            selector.NotifyFailure(key, 5f);

            Assert.IsTrue(selector.IsOnCooldown(key, 5f + 1f));
            Assert.IsFalse(selector.IsOnCooldown(key, 5f + SelectorSettingsDoorCooldown() + 0.1f));
        }

        static float SelectorSettingsDoorCooldown() => new SelectorSettings().DoorCooldownSeconds;
    }
}
