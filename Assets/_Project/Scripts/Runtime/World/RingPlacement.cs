using UnityEngine;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Where a ring that marks a target is drawn: on the first surface of the level below the target's real
    /// position, a little above it. A power core's orb floats over its rack, so its ring lies on the rack top;
    /// a task on the floor keeps its ring on the floor.
    /// </summary>
    /// <remarks>
    /// Only the level (Default and Environment) counts as a surface, so the target's own prop, a crate or an
    /// agent under it is never taken for the ground, and triggers are ignored. The yellow objective beacon
    /// (<c>Journey/Chapters/ObjectiveBeacon</c>, which cannot see this assembly) follows the same rule with the
    /// same numbers, so both rings sit at the same height.
    /// </remarks>
    public static class RingPlacement
    {
        /// <summary>How far above the surface the ring sits, in metres.</summary>
        public const float Lift = 0.05f;

        /// <summary>How far down a surface is looked for.</summary>
        public const float MaxDrop = 20f;

        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        public static int SurfaceMask => LayerMask.GetMask("Default", "Environment");

        /// <summary>The ring's point for a target at <paramref name="target"/>; the target itself if nothing is below.</summary>
        public static Vector3 OnSurfaceBelow(Vector3 target)
        {
            int count = Physics.RaycastNonAlloc(target + Vector3.up * 0.01f, Vector3.down, s_hits, MaxDrop,
                SurfaceMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            float surface = 0f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = s_hits[i];
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    surface = hit.point.y;
                }
            }

            return count > 0 ? new Vector3(target.x, surface + Lift, target.z) : target;
        }
    }
}
