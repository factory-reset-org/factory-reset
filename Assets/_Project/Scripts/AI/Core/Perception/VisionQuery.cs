using UnityEngine;

namespace ToyFactory.AI.Core.Perception
{
    /// <summary>
    /// Range and view-cone check for whether an agent can see a point. The physics
    /// linecast itself is done by the caller (Runtime); this stays pure geometry so it
    /// can be unit tested without a scene, matching the rest of AI.Core.
    /// </summary>
    public static class VisionQuery
    {
        /// <summary>
        /// True only if targetChest is within range, within halfAngleDegrees of forward,
        /// and lineOfSightClear is true (the caller's own Physics.Linecast result from
        /// eyePosition to targetChest).
        /// </summary>
        public static bool CanSee(
            Vector3 eyePosition,
            Vector3 forward,
            Vector3 targetChest,
            float range,
            float halfAngleDegrees,
            bool lineOfSightClear)
        {
            if (!lineOfSightClear)
                return false;

            Vector3 toTarget = targetChest - eyePosition;
            float distance = toTarget.magnitude;
            if (distance > range)
                return false;

            // Coincident eye and target: no direction to test against the cone.
            if (distance <= Mathf.Epsilon)
                return true;

            return Vector3.Angle(forward, toTarget) <= halfAngleDegrees;
        }
    }
}
