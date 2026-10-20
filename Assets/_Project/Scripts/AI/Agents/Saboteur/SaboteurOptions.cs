namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>How a Saboteur brain patrols.</summary>
    public sealed class SaboteurOptions
    {
        /// <summary>The options the game uses: the patrol covers the Saboteur's room.</summary>
        public static readonly SaboteurOptions Default = new SaboteurOptions(roomPatrol: true);

        /// <summary>The given patrol points only, in order (for fixtures that test the walk itself).</summary>
        public static readonly SaboteurOptions WaypointsOnly = new SaboteurOptions(roomPatrol: false);

        /// <summary>
        /// True to plan a loop that covers the Saboteur's room from its patrol points
        /// (<see cref="RoomPatrolPlanner"/>); false to walk the given points in order.
        /// </summary>
        public bool RoomPatrol { get; }

        /// <summary>Creates the options.</summary>
        public SaboteurOptions(bool roomPatrol)
        {
            RoomPatrol = roomPatrol;
        }
    }
}
