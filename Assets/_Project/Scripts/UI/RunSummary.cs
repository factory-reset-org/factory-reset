using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToyFactory.UI
{
    /// <summary>One line of the Results breakdown: every award of one kind added together.</summary>
    public readonly struct BreakdownLine
    {
        /// <summary>What the points were for.</summary>
        public ScoreReason Reason { get; }

        /// <summary>The line's text, such as "Takedowns".</summary>
        public string Label { get; }

        /// <summary>How many awards were added into the line.</summary>
        public int Count { get; }

        /// <summary>The points of the line.</summary>
        public int Points { get; }

        /// <summary>Creates a line.</summary>
        public BreakdownLine(ScoreReason reason, string label, int count, int points)
        {
            Reason = reason;
            Label = label;
            Count = count;
            Points = points;
        }
    }

    /// <summary>
    /// Everything the Results screen shows about one finished run, as plain data. It is built once
    /// from <see cref="ScoreManager"/>'s numbers and <see cref="ScoreRules.History"/>; the view only
    /// draws it and never computes points.
    /// </summary>
    public sealed class RunSummary
    {
        /// <summary>Chapters in the journey; a won run has cleared all of them.</summary>
        public const int ChapterCount = 4;

        /// <summary>How the run ended.</summary>
        public RunOutcome Outcome { get; }

        /// <summary>Game seconds the run took.</summary>
        public float Seconds { get; }

        /// <summary>Chapters finished, 0 to <see cref="ChapterCount"/>.</summary>
        public int ChaptersCleared { get; }

        /// <summary>Agents knocked out or scrapped (<see cref="ScoreRules.Takedowns"/>).</summary>
        public int Takedowns { get; }

        /// <summary>Blaster shots fired.</summary>
        public int ShotsFired { get; }

        /// <summary>Of those, the shots that hit.</summary>
        public int ShotsHit { get; }

        /// <summary>The final score.</summary>
        public int Score { get; }

        /// <summary>The grade of the final score.</summary>
        public ScoreGrade Grade { get; }

        /// <summary>The awards grouped by reason, in <see cref="ScoreReason"/> order.</summary>
        public IReadOnlyList<BreakdownLine> Breakdown { get; }

        /// <summary>True for a run that shut the factory down.</summary>
        public bool Won => Outcome == RunOutcome.Won;

        /// <summary>The screen's headline.</summary>
        public string Headline => Won ? "FACTORY SHUT DOWN" : "RECALLED";

        /// <summary>The run time as minutes and seconds.</summary>
        public string TimeText => FormatTime(Seconds);

        /// <summary>The chapters cleared as "2 of 4".</summary>
        public string ChaptersText => $"{ChaptersCleared} of {ChapterCount}";

        /// <summary>The accuracy as a percentage with the shots behind it.</summary>
        public string AccuracyText => FormatAccuracy(ShotsFired, ShotsHit);

        /// <summary>Creates a summary from a run's numbers. <see cref="RunOutcome.None"/> reads as a recall.</summary>
        public RunSummary(RunOutcome outcome, float seconds, int chaptersCleared, int takedowns,
            int shotsFired, int shotsHit, int score, ScoreGrade grade, IReadOnlyList<ScoreAward> history)
        {
            Outcome = outcome == RunOutcome.None ? RunOutcome.Recalled : outcome;
            Seconds = float.IsNaN(seconds) ? 0f : Math.Max(0f, seconds);
            ChaptersCleared = Math.Max(0, Math.Min(ChapterCount, chaptersCleared));
            Takedowns = Math.Max(0, takedowns);
            ShotsFired = Math.Max(0, shotsFired);
            ShotsHit = Math.Max(0, Math.Min(ShotsFired, shotsHit));
            Score = score;
            Grade = grade;
            Breakdown = BuildBreakdown(history);
        }

        /// <summary>Whole seconds as "m:ss"; a negative or NaN time reads as 0:00.</summary>
        public static string FormatTime(float seconds)
        {
            int total = float.IsNaN(seconds) ? 0 : (int)Math.Max(0f, Math.Min(seconds, 359999f));
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
        }

        /// <summary>
        /// "73% (22 of 30)", or "No shots recorded" when none were reported. The blaster's shots reach
        /// <see cref="ScoreManager.ReportShots"/> from the player's side, so a run without that report
        /// must not read as 0%.
        /// </summary>
        public static string FormatAccuracy(int shotsFired, int shotsHit)
        {
            if (shotsFired <= 0)
                return "No shots recorded";

            int hit = Math.Max(0, Math.Min(shotsHit, shotsFired));
            int percent = (int)Math.Round(hit * 100.0 / shotsFired, MidpointRounding.AwayFromZero);
            return string.Format(CultureInfo.InvariantCulture, "{0}% ({1} of {2})", percent, hit, shotsFired);
        }

        /// <summary>The Results text for one kind of award.</summary>
        public static string LabelFor(ScoreReason reason)
        {
            switch (reason)
            {
                case ScoreReason.Switch: return "Switches restored";
                case ScoreReason.Task: return "Tasks completed";
                case ScoreReason.Takedown: return "Toys disabled";
                case ScoreReason.Battery: return "Batteries";
                case ScoreReason.AllSaboteurs: return "All Saboteurs destroyed";
                case ScoreReason.WinBase: return "Win bonus";
                case ScoreReason.WinTime: return "Time bonus";
                case ScoreReason.WinIntegrity: return "Integrity bonus";
                case ScoreReason.WinAccuracy: return "Accuracy bonus";
                default: throw new ArgumentOutOfRangeException(nameof(reason));
            }
        }

        // One line per kind of award that happened, summed over the history. A bonus that came to 0
        // still gets its line, so the player sees it was counted.
        static IReadOnlyList<BreakdownLine> BuildBreakdown(IReadOnlyList<ScoreAward> history)
        {
            var lines = new List<BreakdownLine>();
            if (history == null)
                return lines;

            int kinds = Enum.GetValues(typeof(ScoreReason)).Length;
            var counts = new int[kinds];
            var points = new int[kinds];
            for (int i = 0; i < history.Count; i++)
            {
                int index = (int)history[i].Reason;
                counts[index]++;
                points[index] += history[i].Points;
            }

            for (int i = 0; i < kinds; i++)
            {
                if (counts[i] > 0)
                    lines.Add(new BreakdownLine((ScoreReason)i, LabelFor((ScoreReason)i), counts[i], points[i]));
            }

            return lines;
        }
    }
}
