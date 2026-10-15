using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Core
{
    /// <summary>
    /// Implemented by a brain whose body is much wider than its capsule (the Captain), so it
    /// can route where its shoulders fit. Optional: a brain that does not implement it is never
    /// called, so <see cref="AgentContext"/> and every other brain stay as they are.
    /// </summary>
    /// <remarks>
    /// The runtime measures the level against the body once, when the brain is given to the
    /// body on a built grid, and passes the result here.
    /// </remarks>
    public interface IBodyFitAware
    {
        /// <summary>Where the body fits, and which way round, on the brain's grid.</summary>
        void OnBodyFit(BodyFit fit);
    }
}
