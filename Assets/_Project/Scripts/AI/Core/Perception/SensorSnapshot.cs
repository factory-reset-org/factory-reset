namespace ToyFactory.AI.Core.Perception
{
    /// <summary>
    /// What a single agent can currently see and hear, read by that agent's brain
    /// from <see cref="AgentContext.Senses"/>.
    /// </summary>
    /// <remarks>
    /// Stub: fields are added here as perception features land (e.g. vision queries,
    /// heard noise events). Runtime builds this once per agent per tick; brains never
    /// query Physics or NavMesh directly.
    /// </remarks>
    public readonly struct SensorSnapshot
    {
    }
}
