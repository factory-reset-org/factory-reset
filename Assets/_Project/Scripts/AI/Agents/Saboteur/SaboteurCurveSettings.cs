using System;
using UnityEngine;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>The response formula selected by a Saboteur curve asset.</summary>
    public enum SaboteurCurveKind
    {
        Linear,
        Power,
        Logistic,
        Inverse
    }

    /// <summary>Inspector settings for one Saboteur response curve.</summary>
    [CreateAssetMenu(fileName = "SaboteurCurve", menuName = "Factory Reset/Saboteur/Response Curve")]
    public sealed class SaboteurCurveSettings : ScriptableObject
    {
        [SerializeField] SaboteurCurveKind kind = SaboteurCurveKind.Linear;
        [SerializeField] float slope = 1f;
        [SerializeField] float intercept = 0f;
        [SerializeField] float exponent = 2f;
        [SerializeField] float steepness = 10f;
        [SerializeField, Range(0f, 1f)] float midpoint = 0.5f;

        /// <summary>The formula this asset evaluates.</summary>
        public SaboteurCurveKind Kind => kind;

        /// <summary>Evaluates the selected formula for a normalised observation.</summary>
        public float Evaluate(float input)
        {
            switch (kind)
            {
                case SaboteurCurveKind.Linear:
                    return ResponseCurve.Linear(input, slope, intercept);
                case SaboteurCurveKind.Power:
                    return ResponseCurve.Power(input, exponent);
                case SaboteurCurveKind.Logistic:
                    return ResponseCurve.Logistic(input, steepness, midpoint);
                case SaboteurCurveKind.Inverse:
                    return ResponseCurve.Inverse(input);
                default:
                    throw new InvalidOperationException("Unknown Saboteur curve kind.");
            }
        }
    }
}
