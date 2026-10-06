namespace ToyFactory.AI.Core
{
    /// <summary>
    /// How aware an agent is of the player, carried on <see cref="AgentIntent.Alert"/>. The body
    /// shows it as an icon above the agent's head: nothing, "?" or "!".
    /// </summary>
    public enum AlertLevel
    {
        /// <summary>Unaware: patrolling, idle, asleep or down. No icon.</summary>
        None,

        /// <summary>Something is off: following a noise, searching, or watching without committing. "?"</summary>
        Suspicious,

        /// <summary>Has the player: chasing, fighting, or committed to cutting them off. "!"</summary>
        Alert
    }
}
