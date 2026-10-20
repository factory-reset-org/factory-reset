using System;
using System.Globalization;
using System.Text;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Turns a Saboteur's last <see cref="UtilityDecisionTrace"/> into the lines the debug overlay shows:
    /// every candidate with its base score, its raw score and, if it lost, why. It only formats what the
    /// brain already computed; it never scores or decides anything.
    /// </summary>
    public static class UtilityDecisionText
    {
        /// <summary>
        /// One line per candidate in the order it was recorded, the selected one first marked with
        /// <c>&gt;</c>: <c>&gt; Flee 0.75 (raw 0.59)</c>, or <c>- AttackPlayer 0.12 (raw 0.12) outranked</c>.
        /// An empty trace gives "no decision yet".
        /// </summary>
        public static string Format(UtilityDecisionTrace trace)
        {
            if (trace == null)
                throw new ArgumentNullException(nameof(trace));
            if (!trace.HasDecision)
                return "no decision yet";

            var text = new StringBuilder(128);
            text.Append("t ").Append(trace.Time.ToString("0.0", CultureInfo.InvariantCulture));
            if (trace.CommitmentActive)
                text.Append(" (committed)");

            for (int i = 0; i < trace.CandidateCount; i++)
            {
                CandidateTrace candidate = trace.GetCandidate(i);
                text.Append('\n')
                    .Append(candidate.Rejection == CandidateRejection.None ? "> " : "- ")
                    .Append(candidate.Key)
                    .Append(' ').Append(candidate.BaseScore.ToString("0.00", CultureInfo.InvariantCulture))
                    .Append(" (raw ").Append(candidate.RawScore.ToString("0.00", CultureInfo.InvariantCulture)).Append(')');

                string reason = ReasonText(candidate.Rejection);
                if (reason.Length > 0)
                    text.Append(' ').Append(reason);
            }

            if (trace.Truncated)
                text.Append("\n(more candidates than the trace holds)");
            return text.ToString();
        }

        /// <summary>The short word for why a candidate lost; empty for the selected one.</summary>
        public static string ReasonText(CandidateRejection rejection)
        {
            switch (rejection)
            {
                case CandidateRejection.None: return string.Empty;
                case CandidateRejection.Vetoed: return "vetoed";
                case CandidateRejection.OnCooldown: return "cooldown";
                case CandidateRejection.CommitmentHeld: return "held back";
                case CandidateRejection.Outranked: return "outranked";
                default: throw new ArgumentOutOfRangeException(nameof(rejection));
            }
        }
    }
}
