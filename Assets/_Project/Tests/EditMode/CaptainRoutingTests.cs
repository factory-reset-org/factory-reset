using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The Captain's routes and sight in a crowded factory: it sees a player standing by a shelf
    /// (the grid's walking clearance round the shelf is not a wall), keeps clear of where its
    /// shoulders would scrape unless going round is much longer, and walks round other agents, a
    /// standing one most of all.
    /// </summary>
    public sealed class CaptainRoutingTests
    {
        // '#' wall or shelf (not walkable, blocks sight), 'x' walking clearance (not walkable,
        // sight clear), '.' floor. Built with a sight layer, as the level's grid is.
        static GridGraph Grid(params string[] rows)
        {
            var grid = new GridGraph(rows[0].Length, rows.Length, Vector3.zero);
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    var cell = new Vector2Int(x, rows.Length - 1 - y);   // first row is the top (high Z)
                    char c = rows[y][x];
                    if (c != '.')
                        grid.SetWalkable(cell, false);
                    grid.SetBlocksSight(cell, c == '#');
                }
            return grid;
        }

        static AgentContext Context(GridGraph grid, Vector2Int cell, float time, Vector3 forward, WorldBlackboard world) =>
            new AgentContext(cell, grid.CellToWorld(cell), forward, time, world, default(SensorSnapshot));

        static void PlacePlayer(WorldBlackboard world, GridGraph grid, Vector2Int cell) =>
            world.SetPlayer(new PlayerSnapshot(true, cell, grid.CellToWorld(cell), Vector3.zero, Vector3.right,
                5f, true, 1f, 1f, false, 0f, -1f));

        // ---- Sight -------------------------------------------------------------------------

        [Test]
        public void ItSeesAPlayerStandingByAShelfThroughTheShelfsWalkingClearance()
        {
            // A shelf along the aisle's north side; its clearance band is the 'x' row. The player
            // stands in the band at the far end; the line to them runs along it.
            GridGraph grid = Grid(
                "##############",
                "#............#",
                "#xxxxxxxxxxxx#",
                "##############",
                "#xxxxxxxxxxxx#",
                "#............#",
                "##############");
            var world = new WorldBlackboard();
            var brain = new CaptainBrain(grid, new AStarSearch(grid), world, startAwake: true);
            var captain = new Vector2Int(2, 1);    // the south aisle, facing east
            PlacePlayer(world, grid, new Vector2Int(11, 2));   // in the band, 4.6 m away

            Assert.IsFalse(GridLineCheck.IsSightClear(grid, grid.CellToWorld(captain),
                    grid.CellToWorld(new Vector2Int(10, 2))),
                "Walking clearance alone hides the player (the bug).");

            AgentIntent intent = default;
            for (int i = 0; i < 3; i++)
                intent = brain.Tick(Context(grid, captain, i * 0.5f, Vector3.right, world));
            Assert.AreEqual("Engage", intent.DebugState, "It sees them and fights.");
        }

        [Test]
        public void TheShelfItselfStillHidesThePlayer()
        {
            GridGraph grid = Grid(
                "##############",
                "#............#",
                "#xxxxxxxxxxxx#",
                "##############",
                "#xxxxxxxxxxxx#",
                "#............#",
                "##############");
            var world = new WorldBlackboard();
            var brain = new CaptainBrain(grid, new AStarSearch(grid), world, startAwake: true);
            var captain = new Vector2Int(2, 1);
            PlacePlayer(world, grid, new Vector2Int(5, 5));   // the north aisle, behind the shelf

            AgentIntent intent = default;
            for (int i = 0; i < 3; i++)
                intent = brain.Tick(Context(grid, captain, i * 0.5f, Vector3.up, world));
            Assert.AreNotEqual("Engage", intent.DebugState);
        }

        // ---- Shoulders ----------------------------------------------------------------------

        static GridGraph Open(int width, int height)
        {
            var grid = new GridGraph(width, height, Vector3.zero);
            return grid;
        }

        static List<Vector2Int> Route(GridGraph grid, ICostModel cost, Vector2Int from, Vector2Int to)
        {
            PathResult result = new AStarSearch(grid).FindPath(from, to, cost);
            Assert.IsTrue(result.Found);
            return result.Cells;
        }

        [Test]
        public void ItKeepsToWhereItsShouldersFit()
        {
            // A 20 x 9 room; along row 4 (the straight line) its shoulders would scrape
            // something for 10 cells, as along a shelf edge. Rows 2 and 6 are clear.
            GridGraph grid = Open(20, 9);
            var fit = new BodyFit(grid.Width, grid.Height);
            for (int x = 5; x <= 14; x++)
                fit.Set(new Vector2Int(x, 4), alongX: false, alongZ: false);
            var cost = new CaptainRouteCost(grid) { Fit = fit };

            List<Vector2Int> route = Route(grid, cost, new Vector2Int(1, 4), new Vector2Int(18, 4));
            foreach (Vector2Int cell in route)
                Assert.IsFalse(cell.y == 4 && cell.x >= 5 && cell.x <= 14, $"Its shoulders scraped along at {cell} instead of stepping aside.");
        }

        [Test]
        public void ThroughATightGapOnlyWhenGoingRoundIsMuchLonger()
        {
            // A wall across the room with a one-cell gap its capsule fits but its shoulders
            // scrape, and a wide opening 7 cells further along.
            GridGraph grid = Open(30, 12);
            for (int y = 0; y < 12; y++)
                if (y != 2 && y != 9 && y != 10)
                    grid.SetWalkable(new Vector2Int(15, y), false);
            var fit = new BodyFit(grid.Width, grid.Height);
            fit.Set(new Vector2Int(15, 2), alongX: false, alongZ: false);
            var cost = new CaptainRouteCost(grid) { Fit = fit };

            // Right by the gap: through it (scraping) beats a 14-cell detour.
            List<Vector2Int> near = Route(grid, cost, new Vector2Int(13, 2), new Vector2Int(17, 2));
            CollectionAssert.Contains(near, new Vector2Int(15, 2));

            // From up by the wide opening: round through it.
            List<Vector2Int> far = Route(grid, cost, new Vector2Int(10, 9), new Vector2Int(20, 6));
            CollectionAssert.DoesNotContain(far, new Vector2Int(15, 2));
        }

        [Test]
        public void AStepWhereItsShouldersScrapeCostsFourTimesAsMuch()
        {
            GridGraph grid = Open(5, 5);
            var fit = new BodyFit(5, 5);
            fit.Set(new Vector2Int(2, 2), alongX: false, alongZ: true);
            var cost = new CaptainRouteCost(grid) { Fit = fit };
            // The base step plus CaptainRouteCost.ScrapePenalty (3) base steps.
            Assert.AreEqual(1f + CaptainRouteCost.ScrapePenalty, cost.StepCost(new Vector2Int(1, 2), new Vector2Int(2, 2)), 1e-4f);
            Assert.AreEqual(1f, cost.StepCost(new Vector2Int(2, 1), new Vector2Int(2, 2)), 1e-4f, "Along Z it fits.");
            Assert.AreEqual(1.41421f, cost.StepCost(new Vector2Int(1, 1), new Vector2Int(2, 2)), 1e-4f,
                "Diagonally it can angle its shoulders.");
        }

        // ---- Other agents -------------------------------------------------------------------

        [Test]
        public void ItWalksRoundAStandingAgentInTheWay()
        {
            GridGraph grid = Open(20, 9);
            var cost = new CaptainRouteCost(grid);
            Vector3 standing = grid.CellToWorld(new Vector2Int(10, 4));
            cost.SetCrowd(new[] { new CrowdMember(standing, 0f) });

            List<Vector2Int> route = Route(grid, cost, new Vector2Int(1, 4), new Vector2Int(18, 4));
            foreach (Vector2Int cell in route)
                Assert.Greater(Vector3.Distance(grid.CellToWorld(cell), standing), CaptainRouteCost.CloseRadius,
                    $"It walked into the standing agent at {cell}.");
        }

        [Test]
        public void AMovingAgentCostsLessThanAStandingOne()
        {
            GridGraph grid = Open(9, 9);
            var cost = new CaptainRouteCost(grid);
            Vector3 at = grid.CellToWorld(new Vector2Int(4, 4));
            cost.SetCrowd(new[] { new CrowdMember(at, 0f) });
            float standing = cost.CrowdPenalty(new Vector2Int(4, 4));
            cost.SetCrowd(new[] { new CrowdMember(at, 2.5f) });
            float moving = cost.CrowdPenalty(new Vector2Int(4, 4));
            Assert.Greater(standing, moving * 4f);
            Assert.Greater(moving, 0f);
            Assert.AreEqual(0f, cost.CrowdPenalty(new Vector2Int(0, 0)), "Far from it: nothing.");
        }

        [Test]
        public void ThroughADoorItStillWalksRoundAStandingAgent()
        {
            GridGraph grid = Open(20, 9);
            for (int y = 0; y < 9; y++)
                if (y < 2 || y > 6)
                    grid.SetWalkable(new Vector2Int(10, y), false);
            for (int y = 2; y <= 6; y++)
                grid.SetDoor(new Vector2Int(10, y), 7, isClosed: true);
            var cost = new CaptainRouteCost(grid);
            Vector3 standing = grid.CellToWorld(new Vector2Int(14, 4));
            cost.SetCrowd(new[] { new CrowdMember(standing, 0f) });

            List<Vector2Int> route = new DoorRouter(grid).FindPath(new Vector2Int(2, 4), new Vector2Int(18, 4), null, cost);
            Assert.IsNotNull(route);
            foreach (Vector2Int cell in route)
                Assert.Greater(Vector3.Distance(grid.CellToWorld(cell), standing), CaptainRouteCost.CloseRadius);
        }

        [Test]
        public void TheBrainTakesTheBodyFitAndTheCrowd()
        {
            GridGraph grid = Open(10, 10);
            var brain = new CaptainBrain(grid, new AStarSearch(grid), new WorldBlackboard(), startAwake: true);
            Assert.IsNull(brain.BodyFit);
            ((IBodyFitAware)brain).OnBodyFit(new BodyFit(10, 10));
            Assert.IsNotNull(brain.BodyFit);
            ((IBodyFitAware)brain).OnBodyFit(new BodyFit(4, 4));
            Assert.IsNull(brain.BodyFit, "A map for another grid is not used.");
        }
    }
}
