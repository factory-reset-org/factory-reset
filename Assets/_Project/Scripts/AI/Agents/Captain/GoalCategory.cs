namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>
    /// The kinds of place the player might be heading for. Each kind gets its own share of
    /// the prior probability (<see cref="GoalPriors"/>).
    /// </summary>
    public enum GoalCategory
    {
        /// <summary>A task target in the current chapter (from the blackboard's objective targets).</summary>
        Task,

        /// <summary>A control switch that has not been restored yet.</summary>
        Switch,

        /// <summary>The Control Room console.</summary>
        Console,

        /// <summary>A battery pickup; only a goal while the player is low on ammo.</summary>
        Battery
    }
}
