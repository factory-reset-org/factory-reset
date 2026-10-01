using System;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>The score of one action, with its uncompensated product for debug output.</summary>
    public readonly struct ActionScore
    {
        /// <summary>Product of the consideration scores, before compensation.</summary>
        public float Raw { get; }

        /// <summary>Compensated score in [0, 1]; 0 when any consideration vetoed the action.</summary>
        public float BaseScore { get; }

        /// <summary>True when a consideration scored 0.</summary>
        public bool Vetoed => BaseScore <= 0f;

        /// <summary>Creates a score.</summary>
        public ActionScore(float raw, float baseScore)
        {
            Raw = raw;
            BaseScore = baseScore;
        }
    }

    /// <summary>
    /// Scores one kind of action from its considerations: <c>raw = product(c_i)</c>, then
    /// a compensation that removes the structural penalty on actions with more inputs.
    /// </summary>
    public sealed class UtilityAction
    {
        readonly Consideration[] _considerations;
        readonly float _constantScore;

        /// <summary>The action type this definition scores.</summary>
        public SaboteurActionKind Kind { get; }

        /// <summary>Number of considerations (0 for a constant action).</summary>
        public int ConsiderationCount => _considerations.Length;

        /// <summary>Creates an action scored by one or more considerations.</summary>
        public UtilityAction(SaboteurActionKind kind, params Consideration[] considerations)
        {
            if (considerations == null || considerations.Length == 0)
                throw new ArgumentException("An action needs at least one consideration; use Constant for a fixed score.", nameof(considerations));

            for (int i = 0; i < considerations.Length; i++)
            {
                if (considerations[i] == null)
                    throw new ArgumentException("Considerations must not contain null.", nameof(considerations));
            }

            Kind = kind;
            _considerations = (Consideration[])considerations.Clone();
        }

        UtilityAction(SaboteurActionKind kind, float constantScore)
        {
            Kind = kind;
            _constantScore = constantScore;
            _considerations = Array.Empty<Consideration>();
        }

        /// <summary>Creates an action with a fixed score and no considerations, such as Idle at 0.1.</summary>
        public static UtilityAction Constant(SaboteurActionKind kind, float score)
        {
            if (float.IsNaN(score) || score <= 0f || score > 1f)
                throw new ArgumentOutOfRangeException(nameof(score), "A constant score must be in (0, 1].");

            return new UtilityAction(kind, score);
        }

        /// <summary>
        /// Scores the action. <paramref name="inputs"/> holds one observation per
        /// consideration, in order. When <paramref name="considerationScores"/> is given it
        /// receives each consideration's score for the debug view.
        /// </summary>
        public ActionScore Evaluate(float[] inputs, float[] considerationScores = null)
        {
            if (_considerations.Length == 0)
                return new ActionScore(_constantScore, _constantScore);

            if (inputs == null || inputs.Length != _considerations.Length)
                throw new ArgumentException("Provide exactly one input per consideration.", nameof(inputs));
            if (considerationScores != null && considerationScores.Length < _considerations.Length)
                throw new ArgumentException("Score buffer is shorter than the consideration list.", nameof(considerationScores));

            float raw = 1f;
            for (int i = 0; i < _considerations.Length; i++)
            {
                float score = _considerations[i].Evaluate(inputs[i]);
                if (considerationScores != null)
                    considerationScores[i] = score;

                raw *= score;
            }

            return new ActionScore(raw, raw <= 0f ? 0f : Compensate(raw, _considerations.Length));
        }

        /// <summary>
        /// Applies <c>raw + (1 - raw) * (1 - 1/n) * raw</c>. With one consideration the
        /// result equals <paramref name="raw"/>; with more, the product's shrinkage is
        /// partly given back so actions with many inputs are not unfairly low. It does
        /// not make actions with different input counts equivalent.
        /// </summary>
        public static float Compensate(float raw, int considerationCount)
        {
            if (float.IsNaN(raw) || raw < 0f || raw > 1f)
                throw new ArgumentOutOfRangeException(nameof(raw), "Raw score must be in [0, 1].");
            if (considerationCount < 1)
                throw new ArgumentOutOfRangeException(nameof(considerationCount), "At least one consideration is required.");

            float modFactor = 1f - 1f / considerationCount;
            return raw + (1f - raw) * modFactor * raw;
        }
    }
}
