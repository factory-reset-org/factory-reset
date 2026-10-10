using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.Runtime.World;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// A Guard's home room, taken from the level's <see cref="AreaVolume"/> boxes: the room
    /// that contains one of its patrol points.
    /// </summary>
    public sealed class AreaGuardHome : IGuardHome
    {
        readonly BoxCollider _volume;

        public AreaGuardHome(AreaVolume area)
        {
            _volume = area != null ? area.GetComponent<BoxCollider>() : null;
        }

        /// <summary>
        /// The home for a Guard that patrols <paramref name="patrolPoints"/>: the first room that
        /// holds any of them, or null if none does (it then has no room to hold).
        /// </summary>
        public static AreaGuardHome ForPatrol(IReadOnlyList<Vector3> patrolPoints)
        {
            if (patrolPoints == null)
                return null;

            for (int i = 0; i < patrolPoints.Count; i++)
            {
                AreaVolume area = AreaVolume.Find(patrolPoints[i]);
                if (area != null)
                    return new AreaGuardHome(area);
            }
            return null;
        }

        public bool Contains(Vector3 position, float margin)
        {
            if (_volume == null)
                return false;

            // How far outside the box the point is, per axis, in the box's own space, then in
            // metres. Zero on every axis means inside.
            Transform box = _volume.transform;
            Vector3 local = box.InverseTransformPoint(position) - _volume.center;
            Vector3 half = _volume.size * 0.5f;
            var outside = new Vector3(
                Mathf.Max(Mathf.Abs(local.x) - half.x, 0f),
                Mathf.Max(Mathf.Abs(local.y) - half.y, 0f),
                Mathf.Max(Mathf.Abs(local.z) - half.z, 0f));
            outside = Vector3.Scale(outside, box.lossyScale);
            return outside.sqrMagnitude <= margin * margin;
        }
    }
}
