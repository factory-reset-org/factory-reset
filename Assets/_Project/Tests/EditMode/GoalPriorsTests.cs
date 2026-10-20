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
        public void WhileTasksRemainTheSwitchAndConsoleAreSealed()
        {
            // Chapter 3 in the level: one task, its switch (sealed until the task is done) and
            // the console (sealed before the final chapter): 0.60, 0.05 and 0.05 over 0.70.
            var goals = new List<CandidateGoal> { Goal(GoalCategory.Task), Goal(GoalCategory.Switch), Goal(GoalCategory.Console) };

            float[] priors = Priors(goals);

            Assert.AreEqual(0.60f / 0.70f, priors[0], Tolerance);
            Assert.AreEqual(GoalPriors.SealedShare / 0.70f, priors[1], Tolerance);
            Assert.AreEqual(GoalPriors.SealedShare / 0.70f, priors[2], Tolerance);
        }

        [Test]
        public void OnceTheTasksAreDoneTheSwitchOpens()
        {
            var goals = new List<CandidateGoal> { Goal(GoalCategory.Switch), Goal(GoalCategory.Console) };

            float[] priors = Priors(goals);

            Assert.AreEqual(0.25f / 0.30f, priors[0], Tolerance, "The switch takes its full share.");
            Assert.AreEqual(GoalPriors.SealedShare / 0.30f, priors[1], Tolerance, "The console stays sealed before the final chapter.");
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
                Assert.AreEqual(0.60f / 0.70f / 3f, priors[i], Tolerance, $"Task {i}");
            Assert.AreEqual(GoalPriors.SealedShare / 0.70f, priors[3], Tolerance);
            Assert.AreEqual(GoalPriors.SealedShare / 0.70f, priors[4], Tolerance);
        }

        [Test]
        public void FinalChapterConsoleIsSealedUntilTheCoresAreDown()
        {
            // Chapter 4: three cores and the console. Each core 0.60 / 0.65 / 3 = 0.31, so no
            // goal is sure (>= 0.5) before the player moves; the console 0.08.
            var goals = new List<CandidateGoal> { Goal(GoalCategory.Task), Goal(GoalCategory.Task), Goal(GoalCategory.Task), Goal(GoalCategory.Console) };

            float[] priors = Priors(goals, finalChapter: true);

            for (int i = 0; i < 3; i++)
                Assert.AreEqual(0.60f / 0.65f / 3f, priors[i], Tolerance, $"Core {i}");
            Assert.AreEqual(GoalPriors.SealedShare / 0.65f, priors[3], Tolerance);

            // Cores down: the console is open, and with low ammo it still leads a battery.
            var last = new List<CandidateGoal> { Goal(GoalCategory.Console), Goal(GoalCategory.Battery) };
            float[] open = Priors(last, finalChapter: true, ammo: LowAmmo);
            Assert.AreEqual(0.50f / 0.70f, open[0], Tolerance);
            Assert.AreEqual(0.20f / 0.70f, open[1], Tolerance);
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
