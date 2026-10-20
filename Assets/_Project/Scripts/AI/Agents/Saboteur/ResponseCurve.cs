using System;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>Maps a finite, normalised observation to a Saboteur desirability in [0, 1].</summary>
    public static class ResponseCurve
    {
        /// <summary>Evaluates y = m*x + b, clamping the input and result to [0, 1].</summary>
        public static float Linear(float input, float slope, float intercept)
        {
            float x = NormaliseInput(input);
            RequireFinite(slope, nameof(slope));
            RequireFinite(intercept, nameof(intercept));
            return Clamp01(slope * x + intercept);
        }

        /// <summary>Evaluates x^exponent for a strictly positive exponent.</summary>
        public static float Power(float input, float exponent)
        {
            float x = NormaliseInput(input);
            RequirePositive(exponent, nameof(exponent));
            return Clamp01((float)Math.Pow(x, exponent));
        }

        /// <summary>Evaluates a rising logistic curve centred at midpoint.</summary>
        public static float Logistic(float input, float steepness, float midpoint)
        {
            float x = NormaliseInput(input);
            RequirePositive(steepness, nameof(steepness));
            RequireFinite(midpoint, nameof(midpoint));
            if (midpoint < 0f || midpoint > 1f)
                throw new ArgumentOutOfRangeException(nameof(midpoint), "Midpoint must be in [0, 1].");

            return (float)(1d / (1d + Math.Exp(-(double)steepness * (x - midpoint))));
        }

        /// <summary>Evaluates 1 - x after clamping the input to [0, 1].</summary>
        public static float Inverse(float input) => 1f - NormaliseInput(input);

        static float NormaliseInput(float input)
        {
            RequireFinite(input, nameof(input));
            return Clamp01(input);
        }

        static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        static void RequirePositive(float value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0f)
                throw new ArgumentOutOfRangeException(name, "Value must be positive.");
        }

        static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, "Value must be finite.");
        }
    }
}
