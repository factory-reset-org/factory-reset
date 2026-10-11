using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class TacticalCostModelTests
    {
        const float Tolerance = 1e-4f;
        static readonly Vector2Int Origin = new Vector2Int(0, 0);
        static readonly Vector2Int Orthogonal = new Vector2Int(1, 0);
        static readonly Vector2Int Diagonal = new Vector2Int(1, 1);

        [Test]
        public void HiddenStepCostsTheSameAsTheBaseModel()
        {
            var model = new TacticalCostModel(_ => false);

            Assert.AreEqual(1f, model.StepCost(Origin, Orthogonal), Tolerance);
            Assert.AreEqual(1.41421356f, model.StepCost(Origin, Diagonal), Tolerance);
        }

        [Test]
        public void ExposedStepCostsBaseTimesOnePlusLambda()
        {
            var model = new TacticalCostModel(_ => true, lambda: 3f);

            Assert.AreEqual(1f * 4f, model.StepCost(Origin, Orthogonal), Tolerance);
            Assert.AreEqual(1.41421356f * 4f, model.StepCost(Origin, Diagonal), Tolerance);
        }

        [Test]
        public void DefaultLambdaIsThree()
        {
            var model = new TacticalCostModel(_ => true);

            Assert.AreEqual(4f, model.StepCost(Origin, Orthogonal), Tolerance);
        }

        [Test]
        public void CustomLambdaIsRespected()
        {
            var model = new TacticalCostModel(_ => true, lambda: 1f);

            Assert.AreEqual(2f, model.StepCost(Origin, Orthogonal), Tolerance);
        }

        [Test]
        public void ExposureIsQueriedWithTheDestinationCell()
        {
            Vector2Int? seen = null;
            var model = new TacticalCostModel(cell =>
            {
                seen = cell;
                return false;
            });

            model.StepCost(Origin, Orthogonal);

            Assert.AreEqual(Orthogonal, seen);
        }

        [Test]
        public void NullExposureDelegateThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new TacticalCostModel(null));
        }
    

        [Test]
        public void TacticalCost_HiddenRouteWithinLambdaBound_IsPreferred()
        {
            // A 21 x 9 room. A band of cells across the middle is in the player's sight, and
            // the straight route from west to east runs along its length.
            var grid = new GridGraph(21, 9, Vector3.zero);
            var search = new AStarSearch(grid);
            var start = new Vector2Int(0, 4);
            var goal = new Vector2Int(20, 4);
            Func<Vector2Int, bool> exposed = c => c.x >= 5 && c.x <= 15 && c.y >= 2 && c.y <= 6;

            PathResult plain = search.FindPath(start, goal, BaseCostModel.Instance);
            var tactical = new TacticalCostModel(exposed, 3f);
            PathResult careful = search.FindPath(start, goal, tactical);

            Assert.IsTrue(plain.Found);
            Assert.IsTrue(careful.Found);
            Assert.IsTrue(plain.Cells.Exists(c => exposed(c)), "the plain route should cross the exposed band");
            Assert.IsFalse(careful.Cells.Exists(c => exposed(c)), "the tactical route should go round it");

            // The detour is longer on the ground, but the exposure penalty makes it the cheaper
            // route. It can never be longer than (1 + lambda) times the shortest one: that is
            // the bound, because staying on the shortest route costs at most that much.
            float plainLength = PathLength(plain.Cells, BaseCostModel.Instance);
            float carefulLength = PathLength(careful.Cells, BaseCostModel.Instance);
            Assert.Greater(carefulLength, plainLength);
            Assert.LessOrEqual(carefulLength, plainLength * 4f);
            Assert.Less(PathLength(careful.Cells, tactical), PathLength(plain.Cells, tactical));
        }

        [Test]
        public void TacticalCost_NeverBelowBaseOctileStep_StaysAdmissible()
        {
            var random = new System.Random(20261010);
            var grid = new GridGraph(24, 24, Vector3.zero);
            var search = new AStarSearch(grid);

            for (int round = 0; round < 25; round++)
            {
                // A different random set of exposed cells each round.
                var exposedCells = new HashSet<Vector2Int>();
                for (int i = 0; i < 150; i++)
                    exposedCells.Add(new Vector2Int(random.Next(24), random.Next(24)));
                var model = new TacticalCostModel(exposedCells.Contains, 3f);

                // Every single step costs at least the base step.
                for (int i = 0; i < 40; i++)
                {
                    var from = new Vector2Int(random.Next(1, 23), random.Next(1, 23));
                    var to = from + new Vector2Int(random.Next(-1, 2), random.Next(-1, 2));
                    if (to == from)
                        continue;
                    Assert.GreaterOrEqual(model.StepCost(from, to), BaseCostModel.Instance.StepCost(from, to) - Tolerance);
                }

                // So a whole route is never cheaper than the octile distance, which is the A*
                // heuristic: the heuristic never overestimates and routes stay optimal.
                var start = new Vector2Int(random.Next(24), random.Next(24));
                var goal = new Vector2Int(random.Next(24), random.Next(24));
                PathResult result = search.FindPath(start, goal, model);
                Assert.IsTrue(result.Found);
                Assert.GreaterOrEqual(PathLength(result.Cells, model) + Tolerance,
                    BaseCostModel.OctileDistance(start, goal));
            }
        }

        static float PathLength(List<Vector2Int> cells, ICostModel model)
        {
            float total = 0f;
            for (int i = 1; i < cells.Count; i++)
                total += model.StepCost(cells[i - 1], cells[i]);
            return total;
        }
    }
}
