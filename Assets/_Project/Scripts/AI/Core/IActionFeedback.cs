namespace ToyFactory.AI.Core
{
    /// <summary>
    /// Implemented by a brain that wants to know how its actions turned out (the Saboteur,
    /// for its target claims and cooldowns). Optional: a brain that does not implement it is
    /// never called, so no other brain changes.
    /// </summary>
    /// <remarks>
    /// The runtime calls it once per request: when it carries out an action
    /// (<paramref name="success"/> true), or when it gives up on one: the target is not
    /// registered, the agent stayed out of reach too long, or the agent went down first.
    /// Called on the main thread outside <see cref="IAgentBrain.Tick"/>; keep it cheap.
    /// </remarks>
    public interface IActionFeedback
    {
        /// <summary>How the request for <paramref name="action"/> on <paramref name="targetId"/> ended.</summary>
        void OnActionResolved(AgentAction action, int targetId, bool success);
    }
}
