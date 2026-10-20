using UnityEngine;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// How a body looks while it is knocked out, and how it gets back up: one per robot, used
    /// by <see cref="AgentKnockdown"/> instead of its tip-over. The Captain kneels
    /// (<see cref="AgentKneel"/>); the toys slump like a powered-down toy (the Tracker
    /// <see cref="TrackerSlump"/>, the Saboteur <see cref="SaboteurSlump"/>, the Guard
    /// <see cref="GuardSlump"/>).
    /// </summary>
    /// <remarks>
    /// <para>Applied in LateUpdate over the pose the Animator wrote that frame (the
    /// knock-down pauses the Animator, so it holds and rewrites the last pose), blending from
    /// it while going down and back into it at the end of the stand-up, so there is no pop at
    /// either end. A pivot no clip writes is posed from its rest pose instead: see
    /// <see cref="Joint"/>.</para>
    /// <para><b>Walls:</b> a slump that leans forward would push the head or the body through
    /// a wall the agent was knocked out against. <see cref="FitForward"/> measures the room in
    /// front once, as it goes down, and the lean shrinks to fit.</para>
    /// </remarks>
    public abstract class AgentDownPose : MonoBehaviour
    {
        /// <summary>Seconds until the body has settled into the down pose.</summary>
        public abstract float DownSeconds { get; }

        /// <summary>Seconds to get back up; the knock-down waits this long before it counts as up.</summary>
        public abstract float StandSeconds { get; }

        /// <summary>How far down it is: 0 standing, 1 fully down.</summary>
        public float Amount { get; protected set; }

        /// <summary>Poses the body <paramref name="seconds"/> after it went down.</summary>
        public abstract void PoseDown(float seconds);

        /// <summary>Poses the stand-up, <paramref name="seconds"/> after the reboot.</summary>
        public abstract void PoseStandUp(float seconds);

        /// <summary>Standing: puts back what no clip writes; the rest is the Animator's again.</summary>
        public abstract void Release();

        /// <summary>
        /// One pivot posed by offsets from its rest pose, blended over what the Animator wrote.
        /// </summary>
        protected sealed class Joint
        {
            readonly Transform _pivot;
            readonly Quaternion _rest;
            readonly Vector3 _restPosition;
            Quaternion _written;
            Vector3 _writtenPosition;

            public Joint(Transform pivot)
            {
                _pivot = pivot;
                if (pivot == null)
                    return;
                _rest = _written = pivot.localRotation;
                _restPosition = _writtenPosition = pivot.localPosition;
            }

            public Transform Pivot => _pivot;

            /// <summary>
            /// Writes rest · <paramref name="euler"/> (degrees) and rest + <paramref name="offset"/>
            /// (metres, the pivot's parent space), blended by <paramref name="weight"/> from the pose
            /// the Animator wrote this frame.
            /// </summary>
            public void Pose(Vector3 euler, Vector3 offset, float weight)
            {
                if (_pivot == null)
                    return;
                // Unchanged since our last write: no clip drives it, so pose it from rest.
                Quaternion from = _pivot.localRotation == _written ? _rest : _pivot.localRotation;
                Vector3 fromPosition = _pivot.localPosition == _writtenPosition ? _restPosition : _pivot.localPosition;
                _pivot.localRotation = _written = Quaternion.Slerp(from, _rest * Quaternion.Euler(euler), weight);
                _pivot.localPosition = _writtenPosition = Vector3.Lerp(fromPosition, _restPosition + offset, weight);
            }

            /// <summary>Back to rest, for a pivot no clip writes.</summary>
            public void Rest()
            {
                if (_pivot == null)
                    return;
                _pivot.localRotation = _written = _rest;
                _pivot.localPosition = _writtenPosition = _restPosition;
            }
        }

        /// <summary>
        /// How much of a forward lean fits in front of the body, 0 to 1: 1 in the open, less
        /// against a wall. <paramref name="standingReach"/> and <paramref name="leaningReach"/>
        /// are how far in front of the agent's centre the model reaches standing and fully
        /// leaned (m, measured on the model); the lean only adds the difference.
        /// </summary>
        protected float FitForward(float standingReach, float leaningReach)
        {
            var capsule = GetComponent<CharacterController>();
            float radius = capsule != null ? capsule.radius : 0.5f;
            float height = capsule != null ? capsule.height : 1.5f;
            float reach = Mathf.Max(leaningReach, radius);
            float start = Mathf.Max(standingReach, radius);
            if (reach <= start)
                return 1f;

            int mask = ~LayerMask.GetMask("Agents", "Player", "Ignore Raycast");
            float room = reach;
            foreach (float at in new[] { 0.3f, 0.75f })
            {
                Vector3 origin = transform.position + Vector3.up * (height * at);
                if (Physics.SphereCast(origin, 0.1f, transform.forward, out RaycastHit hit, reach, mask,
                        QueryTriggerInteraction.Ignore))
                    room = Mathf.Min(room, hit.distance + 0.1f);
            }
            return Mathf.Clamp01((room - start) / (reach - start));
        }

        /// <summary>Smoothstep: eases in and out.</summary>
        protected static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Ease-out: fast at first, settles.</summary>
        protected static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        /// <summary>A sine that dies away: <paramref name="cycles"/> per second, gone by <paramref name="seconds"/>.</summary>
        protected static float Decaying(float time, float seconds, float cycles)
        {
            if (time <= 0f || time >= seconds)
                return 0f;
            float fade = 1f - time / seconds;
            return Mathf.Sin(time * cycles * 2f * Mathf.PI) * fade * fade;
        }
    }
}
