using System;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Copies the player onto the blackboard once per frame, so brains can read
    /// <see cref="WorldBlackboard.Player"/> without ever seeing the player object. S2's
    /// player publishes itself as <see cref="PlayerState.Current"/>; this turns its position
    /// into a cell on the level grid, the one grid every brain plans on.
    /// </summary>
    /// <remarks>
    /// With no player in the scene (or one that has been destroyed) the snapshot has
    /// <see cref="PlayerSnapshot.IsKnown"/> false, which every brain must handle. Without a
    /// level grid the cell stays (0, 0). Writing allocates nothing. Keeps the rule that only
    /// Runtime code writes the blackboard.
    /// </remarks>
    public sealed class PlayerStateWriter
    {
        readonly WorldBlackboard _blackboard;

        public PlayerStateWriter(WorldBlackboard blackboard)
        {
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        }

        /// <summary>Writes this frame's snapshot. <paramref name="grid"/> may be null.</summary>
        public void Write(GridGraph grid)
        {
            IPlayerState player = PlayerState.Current;

            // A destroyed MonoBehaviour is not C# null, so ask Unity as well.
            if (player == null || (player is UnityEngine.Object unityObject && unityObject == null))
            {
                _blackboard.SetPlayer(default);
                return;
            }

            var snapshot = new PlayerSnapshot(
                isKnown: true,
                cell: grid != null ? grid.WorldToCell(player.Position) : default,
                position: player.Position,
                velocity: player.Velocity,
                forward: player.Forward,
                sprintSpeed: player.SprintSpeed,
                isAlive: player.IsAlive,
                healthFraction: player.HealthFraction,
                ammoFraction: player.AmmoFraction,
                isReloading: player.IsReloading,
                overchargeTimeLeft: player.OverchargeTimeLeft,
                lastShotTime: player.LastShotTime);
            _blackboard.SetPlayer(snapshot);
        }
    }
}
