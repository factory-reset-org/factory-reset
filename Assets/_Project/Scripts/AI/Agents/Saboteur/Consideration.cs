using System;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// One named input of a utility action. A response curve maps a finite, normalised
    /// observation to a desirability in [0, 1]; a result of 0 vetoes the whole action.
    /// </summary>
    public sealed class Consideration
    {
        readonly Func<float, float> _curve;

        /// <summary>Name shown in debug output.</summary>
        public string Name { get; }

        /// <summary>Creates a consideration that scores its input with the given curve.</summary>
        public Consideration(string name, Func<float, float> curve)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Name is required.", nameof(name));

            Name = name;
            _curve = curve ?? throw new ArgumentNullException(nameof(curve));
        }

        /// <summary>Creates a consideration driven by a Saboteur curve asset.</summary>
        public static Consideration FromSettings(string name, SaboteurCurveSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            return new Consideration(name, settings.Evaluate);
        }

        /// <summary>
        /// Creates a consideration that passes an already-normalised input through
        /// unchanged, for facts such as "not claimed" (0 or 1) or squad saturation.
        /// </summary>
        public static Consideration Direct(string name) =>
            new Consideration(name, input => ResponseCurve.Linear(input, 1f, 0f));

        /// <summary>Scores an observation. Throws if the input or the curve result is not finite or leaves [0, 1].</summary>
        public float Evaluate(float input)
        {
            float score = _curve(input);
            if (float.IsNaN(score) || float.IsInfinity(score) || score < 0f || score > 1f)
                throw new InvalidOperationException($"Consideration '{Name}' produced {score}, outside [0, 1].");

            return score;
        }
    }
}
