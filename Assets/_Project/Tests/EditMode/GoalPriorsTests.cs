using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;

namespace ToyFactory.Tests.EditMode
{
    public class GoalPriorsTests
    {
        const float Tolerance = 1e-5f;
        const float FullAmmo = 1f;
        const float LowAmmo = 0.1f;

        static int _nextId;

        static CandidateGoal Goal(GoalCategory category) =>
            new CandidateGoal(_nextId++, Vector2Int.zero, category);

        static float[] Priors(List<CandidateGoal> goals, bool finalChapter = false, float ammo = FullAmmo)
        {
            var priors = new float[goals.Count];
            GoalPriors.Compute(goals, finalChapter, ammo, priors);
            return priors;
        }

        static float Sum(float[] values)
        {
            float total = 0f;
            foreach (float value in values) total += value;
            return total;
        }

        [Test]
        public void CategorySharesMatchTheDesignTable()
        {
            var goals = new List<CandidateGoal> { Goal(GoalCategory.Task), Goal(GoalCategory.Switch), Goal(GoalCategory.Console) };

            float[] priors = Priors(goals);

            Assert.AreEqual(0.60f, priors[0], Tolerance);
            Assert.AreEqual(0.25f, priors[1], Tolerance);
            Assert.AreEqual(0.15f, priors[2], Tolerance);
        }

        [Test]
        public void GoalsShareTheirCategoryEqually()
        {
            var goals = new List<CandidateGoal>
            {
                Goal(GoalCategory.Task), Goal(GoalCategory.Task), Goal(GoalCategory.Task),
                Goal(GoalCategory.Switch), Goal(GoalCategory.Console)
            };

            float[] priors = Priors(goals);

            for (int i = 0; i < 3; i++)
                Assert.AreEqual(0.20f, priors[i], Tolerance, $"Task {i}");
            Assert.AreEqual(0.25f, priors[3], Tolerance);
            Assert.AreEqual(0.15f, priors[4], Tolerance);
        }

        [Test]
        public void FinalChapterExampleRenormalisesToFiftyFiveAndFortyFive()
        {
            // Final chapter: no unrestored switches, so the cores (0.60) and the console
            // (0.50) are rescaled by 1.10 to 0.55 and 0.45, as in CaptainBot.md.
            var goals = new List<CandidateGoal> { Goal(GoalCategory.Task), Goal(GoalCategory.Console) };

            float[] priors = Priors(goals, finalChapter: true);

            Assert.AreEqual(0.60f / 1.10f, priors[0], Tolerance);
            Assert.AreEqual(0.50f / 1.10f, priors[1], Tolerance);
        }

        [Test]
        public void BatteriesAreOnlyGoalsBelowThirtyPercentAmmo()
        {
            var goals = new List<CandidateGoal> { Goal(GoalCategory.Task), Goal(GoalCategory.Battery) };

            float[] full = Priors(goals, ammo: FullAmmo);
            float[] atThreshold = Priors(goals, ammo: GoalPriors.LowAmmoThreshold);
            float[] low = Priors(goals, ammo: LowAmmo);

            Assert.AreEqual(1f, full[0], Tolerance);
            Assert.AreEqual(0f, full[1]);
            Assert.AreEqual(0f, atThreshold[1]);
            Assert.AreEqual(0.60f / 0.80f, low[0], Tolerance);
            Assert.AreEqual(0.20f / 0.80f, low[1], Tolerance);
        }

        [Test]
        public void PriorsSumToOneForEveryCategoryMix()
        {
            var categories = (GoalCategory[])Enum.GetValues(typeof(GoalCategory));
            var rng = new System.Random(4);
            for (int trial = 0; trial < 200; trial++)
            {
                var goals = new List<CandidateGoal>();
                int count = rng.Next(1, 12);
                for (int i = 0; i < count; i++)
                    goals.Add(Goal(categories[rng.Next(categories.Length)]));
                bool finalChapter = rng.Next(2) == 0;
                float ammo = (float)rng.NextDouble();

                float[] priors = Priors(goals, finalChapter, ammo);

                bool anyIncluded = false;
                foreach (CandidateGoal goal in goals)
                    if (goal.Category != GoalCategory.Battery || ammo < GoalPriors.LowAmmoThreshold)
                        anyIncluded = true;
                Assert.AreEqual(anyIncluded ? 1f : 0f, Sum(priors), 1e-4f, $"Trial {trial}");
            }
        }

        [Test]
        public void OnlyBatteriesWithFullAmmoGivesNoPrior()
        {
            var goals = new List<CandidateGoal> { Goal(GoalCategory.Battery), Goal(GoalCategory.Battery) };

            float[] priors = Priors(goals, ammo: FullAmmo);

            Assert.AreEqual(0f, Sum(priors));
        }

        [Test]
        public void NoGoalsIsAllowed()
        {
            Assert.DoesNotThrow(() => GoalPriors.Compute(new List<CandidateGoal>(), false, FullAmmo, new float[0]));
        }

        [Test]
        public void InvalidArgumentsThrow()
        {
            var goals = new List<CandidateGoal> { Goal(GoalCategory.Task), Goal(GoalCategory.Switch) };

            Assert.Throws<ArgumentNullException>(() => GoalPriors.Compute(null, false, FullAmmo, new float[2]));
            Assert.Throws<ArgumentNullException>(() => GoalPriors.Compute(goals, false, FullAmmo, null));
            Assert.Throws<ArgumentException>(() => GoalPriors.Compute(goals, false, FullAmmo, new float[1]));
        }

        [Test]
        public void ComputingPriorsAllocatesZeroBytes()
        {
            var goals = new List<CandidateGoal>
            {
                Goal(GoalCategory.Task), Goal(GoalCategory.Task), Goal(GoalCategory.Switch),
                Goal(GoalCategory.Console), Goal(GoalCategory.Battery)
            };
            var priors = new float[goals.Count];

            for (int i = 0; i < 3; i++)
                GoalPriors.Compute(goals, false, LowAmmo, priors);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                GoalPriors.Compute(goals, false, LowAmmo, priors);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated);
        }
    }
}
