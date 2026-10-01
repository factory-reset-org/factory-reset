using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.Tests.EditMode
{
    public class GoalInferenceTests
    {
        const float FullAmmo = 1f;

        static CandidateGoal Task(int id, int x, int y) => new CandidateGoal(id, new Vector2Int(x, y), GoalCategory.Task);

        static float SumOfPosteriors(GoalInference inference)
        {
            float total = 0f;
            for (int i = 0; i < inference.GoalCount; i++)
                total += inference.Posterior(i);
            return total;
        }

        static GridGraph RandomGrid(int width, int height, float blockedChance, System.Random rng)
        {
            var grid = new GridGraph(width, height, Vector3.zero);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (rng.NextDouble() < blockedChance)
                        grid.SetWalkable(new Vector2Int(x, y), false);
            return grid;
        }

        static Vector2Int RandomWalkableCell(GridGraph grid, System.Random rng)
        {
            while (true)
            {
                var cell = new Vector2Int(rng.Next(grid.Width), rng.Next(grid.Height));
                if (grid.IsTraversable(cell))
                    return cell;
            }
        }

        [Test]
        public void WorkedExampleFromTheDesignDocument()
        {
            // Three tasks with equal priors; detours 0 m, 4 m and 6 m (CaptainBot.md).
            var priors = new[] { 1f / 3f, 1f / 3f, 1f / 3f };
            var detours = new[] { 0f, 4f, 6f };
            var included = new[] { true, true, true };
            var posteriors = new float[3];

            int best = GoalInference.Normalise(priors, detours, included, 3, GoalInference.DefaultBeta, posteriors);

            Assert.AreEqual(0, best);
            Assert.AreEqual(0.84f, posteriors[0], 0.005f);
            Assert.AreEqual(0.11f, posteriors[1], 0.005f);
            Assert.AreEqual(0.04f, posteriors[2], 0.005f);
        }

        [Test]
        public void HugeDetoursDoNotUnderflowToNaN()
        {
            // e^(-0.5 * 1000) is far below the smallest float, so without the log-space shift
            // both weights would round to 0 and the division would give NaN.
            var priors = new[] { 0.5f, 0.5f };
            var detours = new[] { 1000f, 2000f };
            var included = new[] { true, true };
            var posteriors = new float[2];

            int best = GoalInference.Normalise(priors, detours, included, 2, GoalInference.DefaultBeta, posteriors);

            Assert.AreEqual(0, best);
            Assert.IsFalse(float.IsNaN(posteriors[0]) || float.IsNaN(posteriors[1]));
            Assert.AreEqual(1f, posteriors[0] + posteriors[1], 1e-5f);
            Assert.AreEqual(1f, posteriors[0], 1e-5f);
        }

        [Test]
        public void DetourIsZeroOnAnOptimalRoute()
        {
            var grid = new GridGraph(20, 5, Vector3.zero);
            var inference = new GoalInference(grid);
            var goals = new List<CandidateGoal> { Task(1, 19, 2), Task(2, 0, 2) };

            // Walking east along the row: on a shortest route to goal 1, away from goal 2.
            inference.Update(goals, new Vector2Int(5, 2), new Vector2Int(11, 2), false, FullAmmo);

            Assert.AreEqual(0f, inference.Detour(0), 1e-4f);
            Assert.AreEqual(6f, inference.Detour(1), 1e-4f); // 12 cells walked away and back = 6 m
            Assert.AreEqual(0, inference.MostLikelyIndex);
        }

        [Test]
        public void StandingStillGivesThePrior()
        {
            var grid = new GridGraph(20, 20, Vector3.zero);
            var inference = new GoalInference(grid);
            var goals = new List<CandidateGoal>
            {
                Task(1, 19, 10), Task(2, 0, 10),
                new CandidateGoal(3, new Vector2Int(10, 19), GoalCategory.Switch)
            };
            var priors = new float[3];
            GoalPriors.Compute(goals, false, FullAmmo, priors);

            var here = new Vector2Int(10, 10);
            inference.Update(goals, here, here, false, FullAmmo);

            for (int i = 0; i < 3; i++)
                Assert.AreEqual(priors[i], inference.Posterior(i), 1e-5f, $"Goal {i}");
        }

        [Test]
        public void WalkingStraightAtAGoalIsConfidentWithinThreeSeconds()
        {
            // Three tasks east, north and west of the player. The player walks east at 3 m/s
            // (6 cells/s) and is sampled at the 2 Hz prediction rate.
            var grid = new GridGraph(40, 40, Vector3.zero);
            var inference = new GoalInference(grid);
            var goals = new List<CandidateGoal> { Task(1, 39, 20), Task(2, 20, 39), Task(3, 0, 20) };
            var track = new PlayerTrack();

            float confidentAt = float.PositiveInfinity;
            for (int step = 0; step <= 6; step++)
            {
                float time = step * 0.5f;
                var cell = new Vector2Int(20 + step * 3, 20);
                track.Record(time, cell);
                if (!track.TryGetPast(time, out Vector2Int past))
                    continue;

                inference.Update(goals, past, cell, false, FullAmmo);
                if (inference.MostLikelyIndex == 0 && inference.Posterior(0) > 0.8f)
                {
                    confidentAt = time;
                    break;
                }
            }

            Assert.LessOrEqual(confidentAt, 3f);
        }

        [Test]
        public void PosteriorsSumToOneOnRandomGrids()
        {
            var rng = new System.Random(17);
            for (int trial = 0; trial < 50; trial++)
            {
                GridGraph grid = RandomGrid(25, 25, 0.2f, rng);
                var inference = new GoalInference(grid);
                var goals = new List<CandidateGoal>();
                for (int i = 0; i < 4; i++)
                {
                    Vector2Int cell = RandomWalkableCell(grid, rng);
                    goals.Add(new CandidateGoal(i, cell, (GoalCategory)(i % 3)));
                }

                bool predicted = inference.Update(goals, RandomWalkableCell(grid, rng), RandomWalkableCell(grid, rng), false, FullAmmo);

                if (predicted)
                    Assert.AreEqual(1f, SumOfPosteriors(inference), 1e-4f, $"Trial {trial}");
                for (int i = 0; i < inference.GoalCount; i++)
                    Assert.IsFalse(float.IsNaN(inference.Posterior(i)), $"Trial {trial}: NaN");
            }
        }

        [Test]
        public void UnreachableGoalIsLeftOut()
        {
            // Goal 2 is sealed inside a ring of walls.
            var grid = new GridGraph(10, 10, Vector3.zero);
            for (int x = 6; x <= 8; x++)
                for (int y = 6; y <= 8; y++)
                    if (x != 7 || y != 7)
                        grid.SetWalkable(new Vector2Int(x, y), false);
            var inference = new GoalInference(grid);
            var goals = new List<CandidateGoal> { Task(1, 0, 0), Task(2, 7, 7) };

            inference.Update(goals, new Vector2Int(3, 3), new Vector2Int(2, 2), false, FullAmmo);

            Assert.AreEqual(1f, inference.Posterior(0), 1e-5f);
            Assert.AreEqual(0f, inference.Posterior(1));
        }

        [Test]
        public void NoGoalsGivesNoPrediction()
        {
            var inference = new GoalInference(new GridGraph(5, 5, Vector3.zero));

            bool predicted = inference.Update(new List<CandidateGoal>(), Vector2Int.zero, Vector2Int.one, false, FullAmmo);

            Assert.IsFalse(predicted);
            Assert.AreEqual(-1, inference.MostLikelyIndex);
            Assert.AreEqual(0f, inference.Confidence);
        }

        [Test]
        public void RemovingAGoalDropsItsFieldAndRenormalises()
        {
            var grid = new GridGraph(20, 20, Vector3.zero);
            var inference = new GoalInference(grid);
            var goals = new List<CandidateGoal> { Task(1, 19, 10), Task(2, 0, 10), Task(3, 10, 19) };
            inference.Update(goals, new Vector2Int(10, 10), new Vector2Int(12, 10), false, FullAmmo);
            Assert.AreEqual(3, inference.CachedFieldCount);

            goals.RemoveAt(0);
            inference.Update(goals, new Vector2Int(10, 10), new Vector2Int(12, 10), false, FullAmmo);

            Assert.AreEqual(2, inference.CachedFieldCount);
            Assert.AreEqual(1f, SumOfPosteriors(inference), 1e-5f);
        }

        [Test]
        public void EachGoalFieldIsBuiltOnceUntilTheGridChanges()
        {
            var grid = new GridGraph(20, 20, Vector3.zero);
            var inference = new GoalInference(grid);
            var goals = new List<CandidateGoal> { Task(1, 19, 10), Task(2, 0, 10) };

            inference.Update(goals, new Vector2Int(10, 10), new Vector2Int(11, 10), false, FullAmmo);
            inference.Update(goals, new Vector2Int(10, 10), new Vector2Int(12, 10), false, FullAmmo);
            Assert.AreEqual(2, inference.FieldComputations);

            goals.Add(Task(3, 10, 19));
            inference.Update(goals, new Vector2Int(10, 10), new Vector2Int(12, 10), false, FullAmmo);
            Assert.AreEqual(3, inference.FieldComputations, "Only the new goal's field is built");

            grid.SetWalkable(new Vector2Int(5, 5), false);
            inference.Update(goals, new Vector2Int(10, 10), new Vector2Int(12, 10), false, FullAmmo);
            Assert.AreEqual(6, inference.FieldComputations, "A grid change makes every field stale");
        }

        [Test]
        public void PlayerOnABlockedCellIsSnappedToANearbyWalkableCell()
        {
            var grid = new GridGraph(20, 5, Vector3.zero);
            grid.SetWalkable(new Vector2Int(11, 2), false); // e.g. the player is standing on a box
            var inference = new GoalInference(grid);
            var goals = new List<CandidateGoal> { Task(1, 19, 2), Task(2, 0, 2) };

            bool predicted = inference.Update(goals, new Vector2Int(5, 2), new Vector2Int(11, 2), false, FullAmmo);

            Assert.IsTrue(predicted);
            Assert.AreEqual(0, inference.MostLikelyIndex);
        }

        [Test]
        public void InvalidArgumentsThrow()
        {
            var grid = new GridGraph(5, 5, Vector3.zero);
            var inference = new GoalInference(grid);
            var duplicate = new List<CandidateGoal> { Task(1, 0, 0), Task(1, 4, 4) };

            Assert.Throws<ArgumentNullException>(() => new GoalInference(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GoalInference(grid, beta: 0f));
            Assert.Throws<ArgumentNullException>(() => inference.Update(null, Vector2Int.zero, Vector2Int.zero, false, FullAmmo));
            Assert.Throws<ArgumentException>(() => inference.Update(duplicate, Vector2Int.zero, Vector2Int.one, false, FullAmmo));
        }

        [Test]
        public void RepeatedUpdatesAllocateZeroBytes()
        {
            var grid = new GridGraph(30, 30, Vector3.zero);
            var inference = new GoalInference(grid);
            var goals = new List<CandidateGoal> { Task(1, 29, 15), Task(2, 0, 15), Task(3, 15, 29) };

            // The first updates build the fields and grow the buffers.
            for (int i = 0; i < 3; i++)
                inference.Update(goals, new Vector2Int(15, 15), new Vector2Int(16 + i, 15), false, FullAmmo);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                inference.Update(goals, new Vector2Int(15, 15), new Vector2Int(16 + i % 5, 15), false, FullAmmo);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated);
        }
    }
}
