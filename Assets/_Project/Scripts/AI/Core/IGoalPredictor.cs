using ToyFactory.AI.Core.Blackboard;

namespace ToyFactory.AI.Core
{
    /// <summary>
    /// Implemented by a brain that predicts the player's goal (the Captain). Brains never
    /// write the blackboard, so the runtime reads <see cref="Prediction"/> after each tick
    /// and copies it to <see cref="WorldBlackboard.PredictedGoal"/>.
    /// </summary>
    public interface IGoalPredictor
    {
        /// <summary>The latest prediction; <see cref="PredictedGoal.IsKnown"/> is false when there is none.</summary>
        PredictedGoal Prediction { get; }
    }
}
