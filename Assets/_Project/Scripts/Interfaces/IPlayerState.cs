using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// What the rest of the game may read about the player. TakeDamage is the only write,
    /// and it is applied by the player's own systems.
    /// </summary>
    public interface IPlayerState
    {
        /// <summary>Feet position in world space.</summary>
        Vector3 Position { get; }

        /// <summary>Velocity in metres per second.</summary>
        Vector3 Velocity { get; }

        /// <summary>Flat facing direction.</summary>
        Vector3 Forward { get; }

        /// <summary>Top sprint speed. Planners assume this worst case.</summary>
        float SprintSpeed { get; }

        bool IsAlive { get; }

        /// <summary>0 to 1. Returns 1 until health exists.</summary>
        float HealthFraction { get; }

        /// <summary>0 to 1. Returns 1 until the blaster exists.</summary>
        float AmmoFraction { get; }

        bool IsReloading { get; }

        /// <summary>Game-time seconds left on overcharge, 0 when off.</summary>
        float OverchargeTimeLeft { get; }

        /// <summary>Game time of the last shot, -1 if the player has never shot.</summary>
        float LastShotTime { get; }

        void TakeDamage(float amount, int sourceAgentId);
    }
}
