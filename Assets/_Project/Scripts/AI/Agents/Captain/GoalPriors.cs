using System;
using System.Collections.Generic;

namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>
    /// The prior P(g): how likely each goal is before looking at the player's movement.
    /// The probability is split between categories first, then shared equally inside each
    /// category, so adding a fifth task does not take probability away from the switches.
    /// </summary>
    public static class GoalPriors
    {
        /// <summary>Share for the current chapter's task targets: the player spends most of a chapter on its tasks.</summary>
        public const float TaskShare = 0.60f;

        /// <summary>Share for switches that are not restored yet.</summary>
        public const float SwitchShare = 0.25f;

        /// <summary>Share for the Control Room console before the final chapter.</summary>
        public const float ConsoleShare = 0.15f;

        /// <summary>Console share in the final chapter, where it is the player's last destination.</summary>
        public const float FinalChapterConsoleShare = 0.50f;

        /// <summary>Share for battery pickups while the player is low on ammo.</summary>
        public const float BatteryShare = 0.20f;

        /// <summary>Below this ammo fraction (0..1) a battery becomes a real goal.</summary>
        public const float LowAmmoThreshold = 0.30f;

        /// <summary>
        /// Writes the prior of each goal into <paramref name="priors"/>, in the same order as
        /// <paramref name="goals"/>. Categories with no goals are dropped and the remaining
        /// shares are scaled to sum to 1, so the priors of all included goals sum to 1.
        /// Batteries get 0 unless <paramref name="ammoFraction"/> is below
        /// <see cref="LowAmmoThreshold"/>. If no goal is included, every prior is 0.
        /// Allocates nothing.
        /// </summary>
        /// <param name="goals">The current candidate goals.</param>
        /// <param name="finalChapter">True in the final chapter, where the console share rises.</param>
        /// <param name="ammoFraction">The player's ammo, 0 (empty) to 1 (full).</param>
        /// <param name="priors">Receives one prior per goal. Must hold at least <c>goals.Count</c> values.</param>
        public static void Compute(IReadOnlyList<CandidateGoal> goals, bool finalChapter, float ammoFraction, float[] priors)
        {
            if (goals == null) throw new ArgumentNullException(nameof(goals));
            if (priors == null) throw new ArgumentNullException(nameof(priors));
            if (priors.Length < goals.Count)
                throw new ArgumentException("The priors array must hold one value per goal.", nameof(priors));

            bool lowAmmo = ammoFraction < LowAmmoThreshold;

            int tasks = 0, switches = 0, consoles = 0, batteries = 0;
            for (int i = 0; i < goals.Count; i++)
            {
                switch (goals[i].Category)
                {
                    case GoalCategory.Task: tasks++; break;
                    case GoalCategory.Switch: switches++; break;
                    case GoalCategory.Console: consoles++; break;
                    case GoalCategory.Battery: if (lowAmmo) batteries++; break;
                }
            }

            float consoleShare = finalChapter ? FinalChapterConsoleShare : ConsoleShare;

            // Only categories that actually have goals take part, so their shares are
            // rescaled to sum to 1 (e.g. final chapter: 0.60 and 0.50 become 0.55 and 0.45).
            float total = (tasks > 0 ? TaskShare : 0f) + (switches > 0 ? SwitchShare : 0f) +
                          (consoles > 0 ? consoleShare : 0f) + (batteries > 0 ? BatteryShare : 0f);

            for (int i = 0; i < goals.Count; i++)
            {
                if (total <= 0f)
                {
                    priors[i] = 0f;
                    continue;
                }

                switch (goals[i].Category)
                {
                    case GoalCategory.Task: priors[i] = TaskShare / total / tasks; break;
                    case GoalCategory.Switch: priors[i] = SwitchShare / total / switches; break;
                    case GoalCategory.Console: priors[i] = consoleShare / total / consoles; break;
                    case GoalCategory.Battery: priors[i] = lowAmmo ? BatteryShare / total / batteries : 0f; break;
                    default: priors[i] = 0f; break;
                }
            }
        }
    }
}
