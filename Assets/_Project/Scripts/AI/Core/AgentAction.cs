namespace ToyFactory.AI.Core
{
    /// <summary>
    /// An action an agent's brain can ask its body to perform this tick, carried on
    /// <see cref="AgentIntent.Action"/>. New actions are added here as new agent
    /// behaviours need them; adding one never breaks an existing agent, but whoever
    /// adds it must also make sure something actually handles it.
    /// </summary>
    public enum AgentAction
    {
        /// <summary>No action this tick; only movement/look intent applies.</summary>
        None,

        /// <summary>Fire at the current target.</summary>
        Shoot,

        /// <summary>Close the door identified by <see cref="AgentIntent.ActionTargetId"/>.</summary>
        CloseDoor,

        /// <summary>Arm the trap identified by <see cref="AgentIntent.ActionTargetId"/>.</summary>
        ArmTrap,

        /// <summary>Pick up the battery identified by <see cref="AgentIntent.ActionTargetId"/>.</summary>
        StealBattery,

        /// <summary>Stop moving and rewind wind-up energy.</summary>
        Rewind
    }
}
