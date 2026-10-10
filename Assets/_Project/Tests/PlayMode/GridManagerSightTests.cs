using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The grid's sight layer, measured when the grid is built: a cell blocks sight only when
    /// something at least 1.3 m tall stands over its middle. Low props, the agent clearance
    /// round obstacles and the cells beside a tall one stay clear.
    /// </summary>
    public sealed class GridManagerSightTests : GridWorldFixture
    {
        // Cell k spans grid-local [0.5k, 0.5k + 0.5), so a 0.5 m box centred at 0.5k + 0.25
        // fills exactly cell k on that axis.
        static readonly Vector2Int TallCell = new Vector2Int(2, 2);
        static readonly Vector2Int LowCell = new Vector2Int(6, 6);

        Vector3 CellCentre(Vector2Int cell) => Origin + new Vector3(cell.x * 0.5f + 0.25f, 0f, cell.y * 0.5f + 0.25f);

        GridGraph Build()
        {
            AddBox("Tall", CellCentre(TallCell) + Vector3.up, new Vector3(0.5f, 2f, 0.5f));
            AddBox("Low", CellCentre(LowCell) + Vector3.up * 0.5f, new Vector3(0.5f, 1f, 0.5f));
            Bake();
            return CreateManager().BuildGrid();
        }

        [Test]
        public void ABuiltGridHasASightLayer()
        {
            Assert.That(Build().HasSightLayer, Is.True);
        }

        [Test]
        public void SomethingTallBlocksSightAndSomethingLowDoesNot()
        {
            GridGraph grid = Build();

            Assert.That(grid.BlocksSight(TallCell), Is.True, "A 2 m box hides what is behind it.");
            Assert.That(grid.BlocksSight(LowCell), Is.False, "A 1 m box is seen over.");
            Assert.That(grid.IsTraversable(LowCell), Is.False, "It still blocks walking.");
        }

        [Test]
        public void TheClearanceRoundATallObstacleDoesNotBlockSight()
        {
            GridGraph grid = Build();

            foreach (Vector2Int offset in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
            {
                Vector2Int beside = TallCell + offset;
                Assert.That(grid.IsTraversable(beside), Is.False, $"{beside} is inside the walking clearance.");
                Assert.That(grid.BlocksSight(beside), Is.False, $"{beside} is seen through.");
            }
        }
    }
}
