using ToyFactory.AI.Core;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Fallback for brains that do not set <see cref="AgentIntent.Alert"/> yet: works the alert
    /// level out from the state names the team's brains use. A brain that sets the level
    /// itself (the Captain) never comes here.
    /// </summary>
    public static class AlertFromState
    {
        /// <summary>The alert level for a brain state name; <see cref="AlertLevel.None"/> if unknown.</summary>
        public static AlertLevel For(string state)
        {
            switch (state)
            {
                // Has the player.
                case "Chase":                                                  // Tracker
                case "TakeCover": case "InCover": case "PeekAndShoot":         // Guard
                case "Relocate": case "Advance": case "Retreat":
                case "AttackPlayer": case "Flee":                              // Saboteur
                case "Engage": case "Intercept": case "Ambush":                // Captain
                    return AlertLevel.Alert;

                // Something is off.
                case "Investigate": case "Search": case "Distracted":          // Tracker
                case "Observe": case "Reassess":                               // Captain
                    return AlertLevel.Suspicious;

                default:
                    return AlertLevel.None;
            }
        }
    }
}
