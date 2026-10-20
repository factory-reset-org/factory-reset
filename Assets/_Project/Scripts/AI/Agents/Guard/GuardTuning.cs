using UnityEngine;

namespace ToyFactory.AI.Agents.Guard
{
    /// <summary>
    /// The Guard's tactical A* exposure penalty, exposed in the Inspector so it can be tuned
    /// without touching code. A step the player can see costs
    /// <c>base * (1 + lambda)</c>, a hidden one costs <c>base</c>, so lambda decides how far
    /// out of its way the Guard walks to stay out of sight. One asset,
    /// <c>Resources/GuardTuning</c>, is read by every Guard when it is created; without it
    /// <see cref="GuardBrain.Lambda"/> is used.
    /// </summary>
    [CreateAssetMenu(fileName = "GuardTuning", menuName = "Toy Factory/Guard Tuning")]
    public sealed class GuardTuning : ScriptableObject
    {
        const string ResourceName = "GuardTuning";

        [Tooltip("Exposure penalty. 3 means a step in the player's sight costs 4x a hidden one. "
            + "0 ignores sight completely; above about 6 it takes absurd detours.")]
        [SerializeField, Range(0f, 8f)] float lambda = GuardBrain.Lambda;

        /// <summary>The exposure penalty multiplier, never negative.</summary>
        public float Lambda => Mathf.Max(0f, lambda);

        /// <summary>The lambda to use: the shared asset's if there is one, else the default.</summary>
        public static float CurrentLambda()
        {
            var asset = Resources.Load<GuardTuning>(ResourceName);
            return asset != null ? asset.Lambda : GuardBrain.Lambda;
        }
    }
}
