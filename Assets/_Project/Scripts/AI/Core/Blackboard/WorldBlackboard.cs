namespace ToyFactory.AI.Core.Blackboard
{
    /// <summary>
    /// Read-only, shared world state every agent's brain reads from
    /// <see cref="AgentContext.World"/>: player state, active noises, door states,
    /// battery positions and cell reservations.
    /// </summary>
    /// <remarks>
    /// Stub: fields are added here as the systems that produce them land (player state,
    /// noise propagation, doors, batteries). Brains only ever read this; only Runtime
    /// code writes to it.
    /// </remarks>
    public sealed class WorldBlackboard
    {
    }
}
