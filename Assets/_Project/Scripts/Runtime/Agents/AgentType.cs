namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// The four enemy classes, one per team member. Used by spawn points to say which
    /// agent appears there and by the spawner to pick that agent's brain.
    /// </summary>
    public enum AgentType
    {
        /// <summary>Wind-up scout that hunts by sound (S1).</summary>
        Tracker,

        /// <summary>Cover-using shooter (S2).</summary>
        Guard,

        /// <summary>Utility-AI saboteur (S3).</summary>
        Saboteur,

        /// <summary>Goal-predicting boss (S4).</summary>
        Captain
    }
}
