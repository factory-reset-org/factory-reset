using System;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>Why a candidate was not the selected action in a decision.</summary>
    public enum CandidateRejection
    {
        /// <summary>The candidate was selected.</summary>
        None = 0,

        /// <summary>A consideration scored 0, so the base score is 0.</summary>
        Vetoed = 1,

        /// <summary>The pair is cooling down after a confirmed success.</summary>
        OnCooldown = 2,

        /// <summary>The current action is inside its commitment window and this score is not above the emergency threshold.</summary>
        CommitmentHeld = 3,

        /// <summary>Usable, but another candidate ranked higher.</summary>
        Outranked = 4
    }

    /// <summary>One consideration's observation and score inside a candidate.</summary>
    public readonly struct ConsiderationTrace
    {
        /// <summary>Consideration name.</summary>
        public string Name { get; }

        /// <summary>The normalised observation fed to the curve.</summary>
        public float Input { get; }

        /// <summary>The curve's score in [0, 1].</summary>
        public float Score { get; }

        /// <summary>Creates a record.</summary>
        public ConsiderationTrace(string name, float input, float score)
        {
            Name = name;
            Input = input;
            Score = score;
        }
    }

    /// <summary>One candidate's scores and outcome inside a decision.</summary>
    public readonly struct CandidateTrace
    {
        /// <summary>The action-target pair.</summary>
        public ActionKey Key { get; }

        /// <summary>Product of the consideration scores, before compensation.</summary>
        public float RawScore { get; }

        /// <summary>Compensated score in [0, 1]; the value the selector received.</summary>
        public float BaseScore { get; }

        /// <summary>Base plus momentum, capped at 1. Equals <see cref="BaseScore"/> for a pair that was not usable.</summary>
        public float RankingScore { get; }

        /// <summary>True for the pair that was current when the decision started.</summary>
        public bool WasCurrent { get; }

        /// <summary>None for the selected candidate, otherwise the reason it lost.</summary>
        public CandidateRejection Rejection { get; }

        /// <summary>Number of considerations recorded for this candidate (0 for a constant action).</summary>
        public int ConsiderationCount { get; }

        internal int FirstConsideration { get; }

        internal CandidateTrace(ActionKey key, float rawScore, float baseScore, float rankingScore,
            bool wasCurrent, CandidateRejection rejection, int considerationCount, int firstConsideration)
        {
            Key = key;
            RawScore = rawScore;
            BaseScore = baseScore;
            RankingScore = rankingScore;
            WasCurrent = wasCurrent;
            Rejection = rejection;
            ConsiderationCount = considerationCount;
            FirstConsideration = firstConsideration;
        }
    }

    /// <summary>
    /// The last decision of one Saboteur as plain data: every candidate's raw and base
    /// score, each consideration's input and value, and why each losing candidate lost.
    /// The brain and <see cref="ActionSelector"/> fill it with the numbers they already
    /// computed, so a panel only displays it and never recalculates.
    /// </summary>
    /// <remarks>
    /// Storage is allocated once in the constructor and overwritten by every decision, so
    /// recording costs no allocation per tick. The instance is read on the same thread
    /// that ticks the brain; copy what you need before the next decision. A decision that
    /// holds more than the capacity records what fits and sets <see cref="Truncated"/>.
    /// </remarks>
    public sealed class UtilityDecisionTrace
    {
        /// <summary>Default number of candidates that can be recorded per decision.</summary>
        public const int DefaultMaxCandidates = 16;

        /// <summary>Default number of consideration records per decision.</summary>
        public const int DefaultMaxConsiderations = 48;

        readonly CandidateTrace[] _candidates;
        readonly ConsiderationTrace[] _considerations;
        int _candidateCount;
        int _considerationCount;

        /// <summary>Creates a trace with fixed capacity.</summary>
        public UtilityDecisionTrace(int maxCandidates = DefaultMaxCandidates, int maxConsiderations = DefaultMaxConsiderations)
        {
            if (maxCandidates < 1)
                throw new ArgumentOutOfRangeException(nameof(maxCandidates));
            if (maxConsiderations < 0)
                throw new ArgumentOutOfRangeException(nameof(maxConsiderations));

            _candidates = new CandidateTrace[maxCandidates];
            _considerations = new ConsiderationTrace[maxConsiderations];
        }

        /// <summary>Game time of the decision.</summary>
        public float Time { get; private set; }

        /// <summary>True once a decision has been recorded.</summary>
        public bool HasDecision { get; private set; }

        /// <summary>True when the decision selected a pair.</summary>
        public bool HasSelection { get; private set; }

        /// <summary>The selected pair; meaningful only when <see cref="HasSelection"/>.</summary>
        public ActionKey Selected { get; private set; }

        /// <summary>True when the selected pair differs from the previous one.</summary>
        public bool Switched { get; private set; }

        /// <summary>True when the commitment window was holding the previous pair at this decision.</summary>
        public bool CommitmentActive { get; private set; }

        /// <summary>True when candidates or considerations beyond the capacity were dropped.</summary>
        public bool Truncated { get; private set; }

        /// <summary>Number of recorded candidates, in the order they were passed to the selector.</summary>
        public int CandidateCount => _candidateCount;

        /// <summary>A recorded candidate.</summary>
        public CandidateTrace GetCandidate(int index)
        {
            if ((uint)index >= (uint)_candidateCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _candidates[index];
        }

        /// <summary>A recorded consideration of a candidate.</summary>
        public ConsiderationTrace GetConsideration(int candidateIndex, int considerationIndex)
        {
            CandidateTrace candidate = GetCandidate(candidateIndex);
            if ((uint)considerationIndex >= (uint)candidate.ConsiderationCount)
                throw new ArgumentOutOfRangeException(nameof(considerationIndex));

            return _considerations[candidate.FirstConsideration + considerationIndex];
        }

        /// <summary>Starts a new decision and clears the previous one.</summary>
        public void Begin(float time)
        {
            Time = time;
            HasDecision = false;
            HasSelection = false;
            Selected = default;
            Switched = false;
            CommitmentActive = false;
            Truncated = false;
            _candidateCount = 0;
            _considerationCount = 0;
        }

        /// <summary>Records a constant-score candidate such as Idle.</summary>
        public void AddConstant(ActionKey key, ActionScore score)
        {
            AddCandidate(key, score, 0);
        }

        /// <summary>
        /// Records a scored candidate. <paramref name="inputs"/> and
        /// <paramref name="considerationScores"/> are the arrays passed to
        /// <see cref="UtilityAction.Evaluate"/>, so the recorded values are the ones that
        /// produced <paramref name="score"/>.
        /// </summary>
        public void AddScored(ActionKey key, UtilityAction action, float[] inputs, float[] considerationScores, ActionScore score)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (inputs == null || considerationScores == null)
                throw new ArgumentNullException(inputs == null ? nameof(inputs) : nameof(considerationScores));

            int count = action.ConsiderationCount;
            if (inputs.Length < count || considerationScores.Length < count)
                throw new ArgumentException("Buffers are shorter than the consideration list.");

            int first = _considerationCount;
            int stored = 0;
            for (int i = 0; i < count; i++)
            {
                if (_considerationCount >= _considerations.Length)
                {
                    Truncated = true;
                    break;
                }

                _considerations[_considerationCount++] = new ConsiderationTrace(action.ConsiderationName(i), inputs[i], considerationScores[i]);
                stored++;
            }

            AddCandidate(key, score, stored, first);
        }

        // Called by ActionSelector for candidate i after it has decided the outcome.
        internal void SetOutcome(int index, float rankingScore, bool wasCurrent, CandidateRejection rejection)
        {
            if ((uint)index >= (uint)_candidateCount)
                return;

            CandidateTrace c = _candidates[index];
            _candidates[index] = new CandidateTrace(c.Key, c.RawScore, c.BaseScore, rankingScore, wasCurrent,
                rejection, c.ConsiderationCount, c.FirstConsideration);
        }

        internal void Complete(in SelectionResult result, bool commitmentActive)
        {
            HasDecision = true;
            HasSelection = result.HasSelection;
            Selected = result.Key;
            Switched = result.Switched;
            CommitmentActive = commitmentActive;
        }

        void AddCandidate(ActionKey key, ActionScore score, int considerationCount, int firstConsideration = 0)
        {
            if (_candidateCount >= _candidates.Length)
            {
                Truncated = true;
                return;
            }

            // The ranking and outcome are filled in by the selector.
            _candidates[_candidateCount++] = new CandidateTrace(key, score.Raw, score.BaseScore, score.BaseScore,
                false, CandidateRejection.Outranked, considerationCount, firstConsideration);
        }
    }
}
