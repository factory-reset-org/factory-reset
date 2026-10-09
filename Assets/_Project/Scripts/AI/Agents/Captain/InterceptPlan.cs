using UnityEngine;

namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>What kind of cell the intercept planner chose.</summary>
    public enum InterceptKind
    {
        /// <summary>No plan: the player cannot reach the goal, the Captain cannot reach it either, or the Captain is nowhere near the grid.</summary>
        None,

        /// <summary>A chokepoint on the player's route the Captain reaches with the margin to spare.</summary>
        Chokepoint,

        /// <summary>No chokepoint qualified, so the first route cell that does.</summary>
        RouteCell,

        /// <summary>No route cell qualified (the player is too close): go to the goal and defend it. Only when the Captain can reach the goal.</summary>
        DefendGoal
    }

    /// <summary>
    /// The intercept planner's answer: where the Captain should wait, and the two arrival
    /// times that justify it (shown in the debug overlay and the intercept timing log).
    /// </summary>
    public readonly struct InterceptPlan
    {
        public static readonly InterceptPlan None = default;

        /// <summary>What kind of cell this is. <see cref="InterceptKind.None"/> means no plan.</summary>
        public readonly InterceptKind Kind;

        /// <summary>The cell to walk to and wait at.</summary>
        public readonly Vector2Int Cell;

        /// <summary>Index of <see cref="Cell"/> on the predicted route (0 = the player's cell).</summary>
        public readonly int RouteIndex;

        /// <summary>Seconds until the player, sprinting, reaches <see cref="Cell"/>.</summary>
        public readonly float PlayerArrival;

        /// <summary>Seconds until the Captain reaches <see cref="Cell"/>; infinity if it cannot.</summary>
        public readonly float CaptainArrival;

        public InterceptPlan(InterceptKind kind, Vector2Int cell, int routeIndex, float playerArrival, float captainArrival)
        {
            Kind = kind;
            Cell = cell;
            RouteIndex = routeIndex;
            PlayerArrival = playerArrival;
            CaptainArrival = captainArrival;
        }

        /// <summary>True for every kind except <see cref="InterceptKind.None"/>.</summary>
        public bool HasPlan => Kind != InterceptKind.None;

        /// <summary>How many seconds the Captain is early; negative if it would be late.</summary>
        public float Lead => PlayerArrival - CaptainArrival;

        public override string ToString() =>
            $"{Kind} at {Cell} (route {RouteIndex}): player {PlayerArrival:0.00} s, Captain {CaptainArrival:0.00} s";
    }
}
