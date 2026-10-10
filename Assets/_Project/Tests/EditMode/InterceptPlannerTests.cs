using System;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class InterceptPlannerTests
    {
        const float Tolerance = 1e-3f;
        const float Margin = InterceptPlanner.DefaultMarginSeconds;

        // Builds a real GridGraph from text rows, '.' walkable and '#' blocked. Row 0 is y = 0.
        static GridGraph GridFromRows(params string[] rows)
        {
            var grid = new GridGraph(rows[0].Length, rows.Length, Vector3.zero);
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] == '#')
                        grid.SetWalkable(new Vector2Int(x, y), false);
            return grid;
        }

        // Three rooms in a row joined by one-cell doorways at (10,3) and (20,3). The doorway
        // cells have two walkable neighbours, so the grid marks them as chokepoints.
        static GridGraph ThreeRooms() => GridFromRows(
            "##############################",
            "#.........#.........#........#",
            "#.........#.........#........#",
            "#............................#",
            "#.........#.........#........#",
            "#.........#.........#........#",
            "##############################");

        static readonly Vector2Int DoorA = new Vector2Int(10, 3);
        static readonly Vector2Int DoorB = new Vector2Int(20, 3);

        static DijkstraField FieldTo(GridGraph grid, Vector2Int goal)
        {
            var field = new DijkstraField(grid);
            field.Compute(goal, BaseCostModel.Instance);
            return field;
        }

        // Arrival times computed independently of the planner, with fresh unbounded fields.
        static float PlayerSeconds(DijkstraField goalField, Vector2Int player, Vector2Int cell, float speed) =>
            (goalField.Cost(player) - goalField.Cost(cell)) * GridGraph.CellSize / speed;

        static float CaptainSeconds(GridGraph grid, Vector2Int captain, Vector2Int cell, float speed) =>
            FieldTo(grid, captain).Cost(cell) * GridGraph.CellSize / speed;

        [Test]
        public void DoorwayCellsInTheTestLevelAreChokepoints()
        {
            GridGraph grid = ThreeRooms();
            Assert.IsTrue(grid.GetNode(DoorA).IsChokepoint);
            Assert.IsTrue(grid.GetNode(DoorB).IsChokepoint);
            Assert.IsFalse(grid.GetNode(new Vector2Int(5, 3)).IsChokepoint, "An open room cell is not a chokepoint.");
        }

        [Test]
        public void PredictedRouteDescendsTheGoalFieldToTheGoal()
        {
            GridGraph grid = ThreeRooms();
            var goal = new Vector2Int(27, 1);
            DijkstraField goalField = FieldTo(grid, goal);
            var planner = new InterceptPlanner(grid);

            planner.Plan(goalField, new Vector2Int(2, 5), new Vector2Int(15, 3), 7f, 4.6f);

            var route = planner.PredictedRoute;
            Assert.AreEqual(new Vector2Int(2, 5), route[0], "The route starts at the player.");
            Assert.AreEqual(goal, route[route.Count - 1], "The route ends at the goal.");
            for (int i = 1; i < route.Count; i++)
            {
                // Each step lowers the remaining cost by exactly its own length, which is
                // what a shortest route does.
                float step = BaseCostModel.Instance.StepCost(route[i - 1], route[i]);
                Assert.AreEqual(goalField.Cost(route[i - 1]) - step, goalField.Cost(route[i]), Tolerance,
                    $"Step {i} is not on a shortest route.");
            }
        }

        [Test]
        public void FirstQualifyingChokepointIsChosenOverEarlierCellsAndLaterChokepoints()
        {
            // Player sprints east along y = 3 at 2 m/s; the Captain stands just past the
            // first doorway. Cells before the doorway also qualify, and so does the second
            // doorway, but the first chokepoint wins.
            GridGraph grid = ThreeRooms();
            var player = new Vector2Int(1, 3);
            var captain = new Vector2Int(12, 3);
            DijkstraField goalField = FieldTo(grid, new Vector2Int(28, 3));
            var planner = new InterceptPlanner(grid);

            InterceptPlan plan = planner.Plan(goalField, player, captain, 2f, 5f);

            Assert.AreEqual(InterceptKind.Chokepoint, plan.Kind);
            Assert.AreEqual(DoorA, plan.Cell);
            Assert.AreEqual(9, plan.RouteIndex);

            var earlier = new Vector2Int(8, 3);
            Assert.LessOrEqual(CaptainSeconds(grid, captain, earlier, 5f) + Margin, PlayerSeconds(goalField, player, earlier, 2f),
                "An earlier ordinary cell qualifies too, so the chokepoint was preferred on purpose.");
            Assert.LessOrEqual(CaptainSeconds(grid, captain, DoorB, 5f) + Margin, PlayerSeconds(goalField, player, DoorB, 2f),
                "The second doorway qualifies too, so the first one was preferred on purpose.");
        }

        [Test]
        public void ChosenCellSatisfiesTheArrivalTimeInequality()
        {
            GridGraph grid = ThreeRooms();
            var player = new Vector2Int(1, 3);
            var captain = new Vector2Int(12, 3);
            DijkstraField goalField = FieldTo(grid, new Vector2Int(28, 3));

            InterceptPlan plan = new InterceptPlanner(grid).Plan(goalField, player, captain, 2f, 5f);

            float playerSeconds = PlayerSeconds(goalField, player, plan.Cell, 2f);
            float captainSeconds = CaptainSeconds(grid, captain, plan.Cell, 5f);
            Assert.AreEqual(playerSeconds, plan.PlayerArrival, Tolerance);
            Assert.AreEqual(captainSeconds, plan.CaptainArrival, Tolerance);
            Assert.LessOrEqual(plan.CaptainArrival + Margin, plan.PlayerArrival);
            Assert.GreaterOrEqual(plan.Lead, Margin - Tolerance);
        }

        [Test]
        public void InequalityHoldsAndNoEarlierCellWasSkippedOnRandomGrids()
        {
            var rng = new System.Random(1234);
            for (int trial = 0; trial < 50; trial++)
            {
                var grid = new GridGraph(25, 25, Vector3.zero);
                for (int y = 0; y < 25; y++)
                    for (int x = 0; x < 25; x++)
                        if (rng.NextDouble() < 0.2)
                            grid.SetWalkable(new Vector2Int(x, y), false);

                Vector2Int player = RandomWalkable(grid, rng);
                Vector2Int captain = RandomWalkable(grid, rng);
                Vector2Int goal = RandomWalkable(grid, rng);
                DijkstraField goalField = FieldTo(grid, goal);
                DijkstraField captainField = FieldTo(grid, captain);
                var planner = new InterceptPlanner(grid);

                InterceptPlan plan = planner.Plan(goalField, player, captain, 7f, 4.6f);
                if (!goalField.IsReachable(player))
                {
                    Assert.AreEqual(InterceptKind.None, plan.Kind, $"Trial {trial}: unreachable goal must give no plan.");
                    continue;
                }
                if (!captainField.IsReachable(goal))
                {
                    // Every route cell leads to the goal, so the Captain reaches none of them either.
                    Assert.AreEqual(InterceptKind.None, plan.Kind, $"Trial {trial}: a goal the Captain cannot reach must give no plan.");
                    continue;
                }

                var route = planner.PredictedRoute;
                if (plan.Kind == InterceptKind.DefendGoal)
                {
                    Assert.AreEqual(route[route.Count - 1], plan.Cell, $"Trial {trial}: defend means the goal cell.");
                    continue;
                }

                Assert.LessOrEqual(plan.CaptainArrival + Margin, plan.PlayerArrival + Tolerance, $"Trial {trial}: {plan}");
                Assert.AreEqual(captainField.Cost(plan.Cell) * GridGraph.CellSize / 4.6f, plan.CaptainArrival, Tolerance,
                    $"Trial {trial}: the bounded field must agree with a full one where it matters.");

                // The rule: the first qualifying chokepoint, else the first qualifying cell.
                for (int i = 0; i < plan.RouteIndex; i++)
                {
                    bool qualifies = captainField.Cost(route[i]) * GridGraph.CellSize / 4.6f + Margin
                                     <= PlayerSeconds(goalField, route[0], route[i], 7f);
                    bool chokepoint = grid.GetNode(route[i]).IsChokepoint;
                    if (plan.Kind == InterceptKind.Chokepoint)
                        Assert.IsFalse(qualifies && chokepoint, $"Trial {trial}: an earlier chokepoint at {route[i]} qualified.");
                    else
                        Assert.IsFalse(qualifies, $"Trial {trial}: an earlier cell at {route[i]} qualified.");
                }
            }
        }

        [Test]
        public void NoQualifyingCellDefendsTheGoal()
        {
            // The player is two cells from the goal and the Captain is a room away: nothing
            // gives a one-second lead, so the Captain goes to guard the goal itself.
            GridGraph grid = ThreeRooms();
            var goal = new Vector2Int(28, 3);
            DijkstraField goalField = FieldTo(grid, goal);

            InterceptPlan plan = new InterceptPlanner(grid).Plan(goalField, new Vector2Int(26, 3), new Vector2Int(2, 3), 7f, 4.6f);

            Assert.AreEqual(InterceptKind.DefendGoal, plan.Kind);
            Assert.AreEqual(goal, plan.Cell);
            Assert.IsFalse(float.IsPositiveInfinity(plan.CaptainArrival), "The goal is timed with a full field, not the bounded one.");
            Assert.AreEqual(CaptainSeconds(grid, new Vector2Int(2, 3), goal, 4.6f), plan.CaptainArrival, Tolerance);
        }

        [Test]
        public void ReservedCellIsSkippedForTheNextChokepoint()
        {
            GridGraph grid = ThreeRooms();
            DijkstraField goalField = FieldTo(grid, new Vector2Int(28, 3));

            InterceptPlan plan = new InterceptPlanner(grid).Plan(goalField, new Vector2Int(1, 3), new Vector2Int(12, 3),
                2f, 5f, cell => cell == DoorA);

            Assert.AreEqual(InterceptKind.Chokepoint, plan.Kind);
            Assert.AreEqual(DoorB, plan.Cell);
        }

        [Test]
        public void OpenRoomWithoutChokepointsUsesTheFirstQualifyingRouteCell()
        {
            GridGraph grid = GridFromRows(
                "####################",
                "#..................#",
                "#..................#",
                "#..................#",
                "#..................#",
                "#..................#",
                "####################");
            var player = new Vector2Int(2, 3);
            var captain = new Vector2Int(12, 3);
            DijkstraField goalField = FieldTo(grid, new Vector2Int(17, 3));
            var planner = new InterceptPlanner(grid);

            InterceptPlan plan = planner.Plan(goalField, player, captain, 2f, 5f);

            Assert.AreEqual(InterceptKind.RouteCell, plan.Kind);
            Assert.LessOrEqual(plan.CaptainArrival + Margin, plan.PlayerArrival + Tolerance);
            var before = planner.PredictedRoute[plan.RouteIndex - 1];
            Assert.Greater(CaptainSeconds(grid, captain, before, 5f) + Margin, PlayerSeconds(goalField, player, before, 2f),
                "The cell before the chosen one must not qualify.");
        }

        [Test]
        public void UnreachableGoalGivesNoPlan()
        {
            GridGraph grid = GridFromRows(
                "#######",
                "#..#..#",
                "#..#..#",
                "#######");
            DijkstraField goalField = FieldTo(grid, new Vector2Int(5, 1));

            InterceptPlan plan = new InterceptPlanner(grid).Plan(goalField, new Vector2Int(1, 1), new Vector2Int(2, 2), 7f, 4.6f);

            Assert.AreEqual(InterceptKind.None, plan.Kind);
            Assert.IsFalse(plan.HasPlan);
        }

        [Test]
        public void TwoCloseGoalsBehindTheSameDoorwayShareTheChokepoint()
        {
            // Both goals lie in the right-hand rooms, so both predicted routes pass the
            // first doorway: the Captain can wait there without choosing between them.
            GridGraph grid = ThreeRooms();
            DijkstraField goalA = FieldTo(grid, new Vector2Int(27, 1));
            DijkstraField goalB = FieldTo(grid, new Vector2Int(15, 5));

            InterceptPlan plan = new InterceptPlanner(grid).PlanShared(goalA, goalB, new Vector2Int(1, 3),
                new Vector2Int(12, 3), 2f, 5f);

            Assert.AreEqual(InterceptKind.Chokepoint, plan.Kind);
            Assert.AreEqual(DoorA, plan.Cell);
        }

        [Test]
        public void TwoCloseGoalsWithNoSharedChokepointGiveNoPlan()
        {
            // One goal through the doorway, one in the player's own room: the routes split
            // before any chokepoint, so the brain should keep observing.
            GridGraph grid = ThreeRooms();
            DijkstraField goalA = FieldTo(grid, new Vector2Int(27, 1));
            DijkstraField goalB = FieldTo(grid, new Vector2Int(2, 1));

            InterceptPlan plan = new InterceptPlanner(grid).PlanShared(goalA, goalB, new Vector2Int(5, 3),
                new Vector2Int(12, 3), 2f, 5f);

            Assert.AreEqual(InterceptKind.None, plan.Kind);
        }

        [Test]
        public void CaptainFieldIsBoundedByThePlayersWalk()
        {
            // A large open grid: the player is 10 cells from the goal, so the Captain's
            // field never needs to spread across the whole level.
            var grid = new GridGraph(80, 80, Vector3.zero);
            DijkstraField goalField = FieldTo(grid, new Vector2Int(50, 40));
            var planner = new InterceptPlanner(grid);

            InterceptPlan plan = planner.Plan(goalField, new Vector2Int(40, 40), new Vector2Int(45, 42), 2f, 5f);

            Assert.IsTrue(plan.Kind == InterceptKind.RouteCell || plan.Kind == InterceptKind.Chokepoint);
            Assert.Less(planner.LastFieldNodesExpanded, grid.CellCount / 4,
                "The bound should keep the Captain's search local.");
        }

        [Test]
        public void InvalidArgumentsThrow()
        {
            GridGraph grid = ThreeRooms();
            DijkstraField field = FieldTo(grid, DoorB);
            var planner = new InterceptPlanner(grid);

            Assert.Throws<ArgumentNullException>(() => new InterceptPlanner(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InterceptPlanner(grid, -1f));
            Assert.Throws<ArgumentNullException>(() => planner.Plan(null, Vector2Int.one, Vector2Int.one, 7f, 4.6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => planner.Plan(field, Vector2Int.one, Vector2Int.one, 0f, 4.6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => planner.Plan(field, Vector2Int.one, Vector2Int.one, 7f, float.NaN));
        }

        [Test]
        public void RepeatedPlansAllocateZeroBytes()
        {
            GridGraph grid = ThreeRooms();
            DijkstraField goalField = FieldTo(grid, new Vector2Int(28, 3));
            var planner = new InterceptPlanner(grid);

            // The first plans size the route list.
            for (int i = 0; i < 3; i++)
                planner.Plan(goalField, new Vector2Int(1 + i, 3), new Vector2Int(12, 3), 2f, 5f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                planner.Plan(goalField, new Vector2Int(1 + i % 5, 3), new Vector2Int(12, 3), 2f, 5f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated);
        }

        static Vector2Int RandomWalkable(GridGraph grid, System.Random rng)
        {
            while (true)
            {
                var cell = new Vector2Int(rng.Next(grid.Width), rng.Next(grid.Height));
                if (grid.IsTraversable(cell))
                    return cell;
            }
        }
    }
}
