using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// A new route given mid-walk is joined with a curve from the current heading when it turns
    /// 25-120 degrees, never through a cell the route avoided; standing, gentle turns and turns
    /// back are left as they are.
    /// </summary>
    public class PathBlenderTests
    {
        static GridGraph Open() => new GridGraph(40, 40, Vector3.zero);

        // Heading east at (10, 10), and a new route that goes north from there.
        static List<Vector3> NorthRoute(GridGraph grid) => new List<Vector3>
        {
            grid.CellToWorld(new Vector2Int(10, 10)),
            grid.CellToWorld(new Vector2Int(10, 14)),
            grid.CellToWorld(new Vector2Int(10, 24)),
        };

        [Test]
        public void ARightAngleTurnCurvesFromTheHeadingIntoTheRoute()
        {
            GridGraph grid = Open();
            Vector3 here = grid.CellToWorld(new Vector2Int(10, 10));
            var route = NorthRoute(grid);
            var result = new List<Vector3>();

            Assert.IsTrue(PathBlender.Blend(grid, here, Vector3.right, 4f, route, result));

            // It sets off still a little east, then bends north onto the route.
            Assert.Greater(result[0].x, here.x, "Leaves along the old heading, no pivot.");
            Assert.Greater(result[0].z, here.z);
            Vector3 join = result[3];
            Assert.AreEqual(here.x, join.x, 1e-3f, "Joins the route.");
            Assert.AreEqual(here.z + 4f * PathBlender.BlendSeconds, join.z, 1e-3f, "About 0.35 s of travel along it.");
            Assert.AreEqual(route[route.Count - 1], result[result.Count - 1], "The rest of the route is kept.");

            // A smooth bend: no step turns more than the whole turn would in one go.
            Vector3 previous = Vector3.right;
            Vector3 from = here;
            foreach (Vector3 point in result.GetRange(0, 4))
            {
                Vector3 step = point - from;
                Assert.Less(Vector3.Angle(previous, step), 60f);
                previous = step;
                from = point;
            }
        }

        [Test]
        public void StandingGentleTurnsAndTurnsBackAreLeftAsTheyAre()
        {
            GridGraph grid = Open();
            Vector3 here = grid.CellToWorld(new Vector2Int(10, 10));
            var result = new List<Vector3>();

            Assert.IsFalse(PathBlender.Blend(grid, here, Vector3.right, 0.2f, NorthRoute(grid), result), "Standing: just turn.");
            CollectionAssert.AreEqual(NorthRoute(grid), result);

            Assert.IsFalse(PathBlender.Blend(grid, here, new Vector3(0.2f, 0f, 1f), 4f, NorthRoute(grid), result), "11 degrees: no help needed.");
            Assert.IsFalse(PathBlender.Blend(grid, here, Vector3.back, 4f, NorthRoute(grid), result), "A turn back is not looped round.");
        }

        [Test]
        public void ItNeverCutsACornerTheRouteAvoided()
        {
            GridGraph grid = Open();
            // A wall just north-east of the agent: the curve would clip it.
            for (int x = 11; x <= 14; x++)
                for (int y = 11; y <= 13; y++)
                    grid.SetWalkable(new Vector2Int(x, y), false);
            Vector3 here = grid.CellToWorld(new Vector2Int(10, 10));
            var route = NorthRoute(grid);
            var result = new List<Vector3>();

            Assert.IsFalse(PathBlender.Blend(grid, here, Vector3.right, 4f, route, result));
            CollectionAssert.AreEqual(route, result, "The route as it is.");
        }
    }
}
