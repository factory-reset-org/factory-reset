using UnityEngine;

namespace ToyFactory.AI.Core.Blackboard
{
    /// <summary>
    /// The Captain's guess of which objective the player is heading for, shared on the
    /// blackboard so other agents can use it (the Saboteur squad picks the door whose
    /// closure costs the player the longest detour to it).
    /// </summary>
    public readonly struct PredictedGoal
    {
        /// <summary>False when there is no prediction (Captain asleep, no player, no reachable goal).</summary>
        public bool IsKnown { get; }

        /// <summary>The <see cref="ObjectiveTarget.Id"/> of the predicted goal.</summary>
        public int GoalId { get; }

        /// <summary>The predicted goal's cell on the level grid.</summary>
        public Vector2Int Cell { get; }

        /// <summary>Probability of this goal, P(g*), 0 to 1. Below 0.5 the Captain itself does not commit.</summary>
        public float Confidence { get; }

        /// <summary>Game time of the prediction, so readers can tell how fresh it is.</summary>
        public float Time { get; }

        public PredictedGoal(int goalId, Vector2Int cell, float confidence, float time)
        {
            IsKnown = true;
            GoalId = goalId;
            Cell = cell;
            Confidence = confidence;
            Time = time;
        }
    }
}
