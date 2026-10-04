using UnityEngine;

namespace ToyFactory.AI.Core.Blackboard
{
    /// <summary>
    /// A brain-safe copy of the player's state, taken once per frame. IsKnown is false
    /// when there is no player in the scene, and the other fields are then defaults.
    /// </summary>
    public readonly struct PlayerSnapshot
    {
        public PlayerSnapshot(
            bool isKnown,
            Vector2Int cell,
            Vector3 position,
            Vector3 velocity,
            Vector3 forward,
            float sprintSpeed,
            bool isAlive,
            float healthFraction,
            float ammoFraction,
            bool isReloading,
            float overchargeTimeLeft,
            float lastShotTime)
        {
            IsKnown = isKnown;
            Cell = cell;
            Position = position;
            Velocity = velocity;
            Forward = forward;
            SprintSpeed = sprintSpeed;
            IsAlive = isAlive;
            HealthFraction = healthFraction;
            AmmoFraction = ammoFraction;
            IsReloading = isReloading;
            OverchargeTimeLeft = overchargeTimeLeft;
            LastShotTime = lastShotTime;
        }

        public bool IsKnown { get; }

        /// <summary>Cell on the level grid (GridManager), not the player controller's own cell.</summary>
        public Vector2Int Cell { get; }

        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public Vector3 Forward { get; }
        public float SprintSpeed { get; }
        public bool IsAlive { get; }
        public float HealthFraction { get; }
        public float AmmoFraction { get; }
        public bool IsReloading { get; }
        public float OverchargeTimeLeft { get; }
        public float LastShotTime { get; }
    }
}
