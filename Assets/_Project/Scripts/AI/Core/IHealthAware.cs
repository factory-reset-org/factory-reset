namespace ToyFactory.AI.Core
{
    /// <summary>
    /// Implemented by a brain that needs its own agent's health (the Saboteur, to decide when
    /// to flee). Optional: a brain that does not implement it is never called, so
    /// <see cref="AgentContext"/> and every other brain stay as they are.
    /// </summary>
    /// <remarks>
    /// The runtime calls it once when the brain is given to the body, after every hit that
    /// counts, and at the reboot (back to full). Not called once the agent is scrapped.
    /// </remarks>
    public interface IHealthAware
    {
        /// <summary>The agent now has <paramref name="hitPointsLeft"/> of <paramref name="maxHitPoints"/>.</summary>
        void OnHealthChanged(int hitPointsLeft, int maxHitPoints);
    }
}
