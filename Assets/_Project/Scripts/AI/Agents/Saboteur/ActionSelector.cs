using System;
using System.Collections.Generic;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>Stability tuning for <see cref="ActionSelector"/>.</summary>
    public sealed class SelectorSettings
    {
        /// <summary>Bonus added to the ranking of the current action-target pair.</summary>
        public float Momentum { get; }

        /// <summary>Seconds a valid selection is kept before it may be replaced normally.</summary>
        public float CommitmentSeconds { get; }

        /// <summary>A different pair may interrupt commitment only with a base score above this.</summary>
        public float EmergencyThreshold { get; }

        /// <summary>Seconds a closed door cannot be selected again after a confirmed success.</summary>
        public float DoorCooldownSeconds { get; }

        /// <summary>Creates settings; the defaults are the design's starting values.</summary>
        public SelectorSettings(float momentum = 0.15f, float commitmentSeconds = 1.5f,
            float emergencyThreshold = 0.9f, float doorCooldownSeconds = 10f)
        {
            RequireRange(momentum, 0f, 1f, nameof(momentum));
            RequireRange(commitmentSeconds, 0f, float.MaxValue, nameof(commitmentSeconds));
            RequireRange(emergencyThreshold, 0f, 1f, nameof(emergencyThreshold));
            RequireRange(doorCooldownSeconds, 0f, float.MaxValue, nameof(doorCooldownSeconds));

            Momentum = momentum;
            CommitmentSeconds = commitmentSeconds;
            EmergencyThreshold = emergencyThreshold;
            DoorCooldownSeconds = doorCooldownSeconds;
        }

        /// <summary>
        /// Cooldown after a confirmed success. Only the door cooldown is agreed; the other
        /// actions have none until their target owners agree a value.
        /// </summary>
        public float CooldownSeconds(SaboteurActionKind kind) =>
            kind == SaboteurActionKind.CloseDoor ? DoorCooldownSeconds : 0f;

        static void RequireRange(float value, float min, float max, string name)
        {
            if (float.IsNaN(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    /// <summary>The outcome of one selection pass.</summary>
    public readonly struct SelectionResult
    {
        /// <summary>False when no candidate was usable.</summary>
        public bool HasSelection { get; }

        /// <summary>The selected action-target pair.</summary>
        public ActionKey Key { get; }

        /// <summary>Base score of the selected pair.</summary>
        public float BaseScore { get; }

        /// <summary>Ranking score (base plus momentum, capped at 1) of the selected pair.</summary>
        public float RankingScore { get; }

        /// <summary>True when the selected pair differs from the previous one.</summary>
        public bool Switched { get; }

        /// <summary>Creates a result.</summary>
        public SelectionResult(bool hasSelection, ActionKey key, float baseScore, float rankingScore, bool switched)
        {
            HasSelection = hasSelection;
            Key = key;
            BaseScore = baseScore;
            RankingScore = rankingScore;
            Switched = switched;
        }
    }

    /// <summary>
    /// Chooses among eligible candidates with momentum, a commitment window and
    /// cooldowns so the Saboteur does not dither. Eligibility is decided before this
    /// class: a candidate that is not passed in is invalid, which cancels the current
    /// plan immediately, even during commitment.
    /// </summary>
    public sealed class ActionSelector
    {
        readonly SelectorSettings _settings;
        readonly Dictionary<ActionKey, float> _cooldownUntil = new Dictionary<ActionKey, float>();
        bool _hasCurrent;
        ActionKey _current;
        float _committedAt;

        /// <summary>Creates a selector with the given stability settings.</summary>
        public ActionSelector(SelectorSettings settings = null)
        {
            _settings = settings ?? new SelectorSettings();
        }

        /// <summary>True when a pair is currently selected.</summary>
        public bool HasCurrent => _hasCurrent;

        /// <summary>The currently selected pair; meaningful only when <see cref="HasCurrent"/>.</summary>
        public ActionKey Current => _current;

        /// <summary>True while the pair cannot be selected because of a cooldown.</summary>
        public bool IsOnCooldown(ActionKey key, float now) =>
            _cooldownUntil.TryGetValue(key, out float until) && now < until;

        /// <summary>
        /// Starts the pair's cooldown. Call only after the runtime confirms the action
        /// succeeded, never when the intent is merely emitted.
        /// </summary>
        public void NotifySuccess(ActionKey key, float now)
        {
            float seconds = _settings.CooldownSeconds(key.Kind);
            if (seconds > 0f)
                _cooldownUntil[key] = now + seconds;
        }

        /// <summary>Drops the current selection (stun, destruction). Cooldowns are kept.</summary>
        public void CancelCurrent() => _hasCurrent = false;

        /// <summary>
        /// Picks the pair to run. <paramref name="now"/> must be game time (the context's
        /// time), so commitment and cooldowns freeze with cutscenes and pause. When
        /// <paramref name="trace"/> is given, it already holds the candidates in the same
        /// order, and receives each one's ranking score and the reason it lost.
        /// </summary>
        public SelectionResult Select(IReadOnlyList<ActionCandidate> candidates, float now, UtilityDecisionTrace trace = null)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            bool currentUsable = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (_hasCurrent && candidates[i].Key.Equals(_current) && IsUsable(candidates[i], now))
                    currentUsable = true;
            }

            if (_hasCurrent && !currentUsable)
                _hasCurrent = false;

            // Idle is a fallback, not a plan, so it is never held: otherwise a useful action
            // that appears just after Idle was chosen would wait out the whole window.
            bool committed = _hasCurrent && _current.Kind != SaboteurActionKind.Idle
                && now - _committedAt < _settings.CommitmentSeconds;

            bool found = false;
            int bestIndex = -1;
            ActionCandidate best = default;
            float bestRank = 0f;
            bool bestIsCurrent = false;

            for (int i = 0; i < candidates.Count; i++)
            {
                ActionCandidate candidate = candidates[i];
                bool isCurrent = _hasCurrent && candidate.Key.Equals(_current);
                if (!IsUsable(candidate, now))
                {
                    trace?.SetOutcome(i, candidate.BaseScore, isCurrent,
                        candidate.BaseScore > 0f ? CandidateRejection.OnCooldown : CandidateRejection.Vetoed);
                    continue;
                }

                if (committed && !isCurrent && !(candidate.BaseScore > _settings.EmergencyThreshold))
                {
                    trace?.SetOutcome(i, candidate.BaseScore, isCurrent, CandidateRejection.CommitmentHeld);
                    continue;
                }

                float rank = Math.Min(1f, candidate.BaseScore + (isCurrent ? _settings.Momentum : 0f));
                trace?.SetOutcome(i, rank, isCurrent, CandidateRejection.Outranked);
                if (!found || IsBetter(candidate.Key, rank, isCurrent, best.Key, bestRank, bestIsCurrent))
                {
                    found = true;
                    bestIndex = i;
                    best = candidate;
                    bestRank = rank;
                    bestIsCurrent = isCurrent;
                }
            }

            if (!found)
            {
                var none = new SelectionResult(false, default, 0f, 0f, false);
                trace?.Complete(none, committed);
                return none;
            }

            bool switched = !_hasCurrent || !best.Key.Equals(_current);
            if (switched)
            {
                _current = best.Key;
                _hasCurrent = true;
                _committedAt = now;
            }

            var selected = new SelectionResult(true, best.Key, best.BaseScore, bestRank, switched);
            if (trace != null)
            {
                trace.SetOutcome(bestIndex, bestRank, bestIsCurrent, CandidateRejection.None);
                trace.Complete(selected, committed);
            }

            return selected;
        }

        bool IsUsable(ActionCandidate candidate, float now)
        {
            float score = candidate.BaseScore;
            if (float.IsNaN(score) || score < 0f || score > 1f)
                throw new ArgumentException($"Candidate {candidate.Key} has an invalid base score {score}.");

            // A zero score is a veto, and a cooling-down pair is not eligible yet.
            return score > 0f && !IsOnCooldown(candidate.Key, now);
        }

        // Higher ranking wins; on equal ranking keep the current pair, then use the
        // action order, then the lower target id, so the result is deterministic.
        static bool IsBetter(ActionKey key, float rank, bool isCurrent, ActionKey bestKey, float bestRank, bool bestIsCurrent)
        {
            if (rank != bestRank)
                return rank > bestRank;
            if (isCurrent != bestIsCurrent)
                return isCurrent;
            if (key.Kind != bestKey.Kind)
                return key.Kind < bestKey.Kind;
            return key.TargetId < bestKey.TargetId;
        }
    }
}
