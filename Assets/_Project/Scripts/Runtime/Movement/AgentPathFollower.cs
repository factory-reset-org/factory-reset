using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Runtime.Movement
{
    /// <summary>
    /// The body half of an agent: walks a <see cref="CharacterController"/> through a list
    /// of world-space waypoints handed over by the brain. It makes no decisions; it only
    /// carries out the route. Deliberately does not use NavMeshAgent, because the AI plans
    /// its own paths on the grid and a NavMeshAgent would replan and fight it.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class AgentPathFollower : MonoBehaviour
    {
        [Tooltip("How close (metres, on the ground plane) the agent must get to a waypoint before moving on to the next.")]
        [SerializeField, Min(0.01f)] float arrivalRadius = 0.3f;

        [Tooltip("Small downward speed (m/s) applied while grounded so the agent stays pressed onto slopes and steps instead of hovering off them.")]
        [SerializeField] float groundedStickSpeed = -2f;

        [Tooltip("Maximum turn speed in degrees per second. 360 turns the agent fully around in half a second.")]
        [SerializeField, Min(1f)] float turnSpeed = 360f;

        // Reused for every route so setting a new path does not allocate.
        readonly List<Vector3> _path = new List<Vector3>();

        CharacterController _controller;
        int _targetIndex;
        float _speed;
        float _verticalVelocity;

        /// <summary>True while there are waypoints left to walk to.</summary>
        public bool HasPath => _targetIndex < _path.Count;

        /// <summary>Horizontal speed this frame in metres per second, for animation.</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>
        /// Signed turn speed this frame in degrees per second (positive = turning right),
        /// for animation.
        /// </summary>
        public float TurnRate { get; private set; }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        /// <summary>
        /// Replaces the current route. Waypoints the agent is already standing on are
        /// skipped straight away.
        /// </summary>
        /// <param name="path">World positions to walk through, in order.</param>
        /// <param name="speed">Movement speed in metres per second.</param>
        public void SetPath(IReadOnlyList<Vector3> path, float speed)
        {
            _path.Clear();
            if (path != null)
            {
                for (int i = 0; i < path.Count; i++)
                    _path.Add(path[i]);
            }

            _targetIndex = 0;
            _speed = Mathf.Max(0f, speed);
            SkipReachedWaypoints();
        }

        /// <summary>Clears the route; the agent stops where it is.</summary>
        public void Stop()
        {
            _path.Clear();
            _targetIndex = 0;
        }

        void Update()
        {
            Vector3 horizontalVelocity = Vector3.zero;

            if (HasPath)
            {
                Vector3 toTarget = _path[_targetIndex] - transform.position;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;

                if (distance > 0f)
                {
                    // Never step further than the remaining distance, so the agent
                    // does not overshoot the waypoint on a long frame.
                    float frameSpeed = Mathf.Min(_speed, distance / Time.deltaTime);
                    horizontalVelocity = toTarget / distance * frameSpeed;
                }
            }

            TurnTowards(horizontalVelocity);
            ApplyGravity();

            Vector3 velocity = horizontalVelocity;
            velocity.y = _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
            CurrentSpeed = horizontalVelocity.magnitude;

            SkipReachedWaypoints();
        }

        // Turns to face the direction of travel, on the ground plane only, at most
        // turnSpeed degrees per second so the agent never snaps round instantly.
        void TurnTowards(Vector3 horizontalVelocity)
        {
            float previousYaw = transform.eulerAngles.y;

            if (horizontalVelocity.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(horizontalVelocity, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, target, turnSpeed * Time.deltaTime);
            }

            TurnRate = Mathf.DeltaAngle(previousYaw, transform.eulerAngles.y) / Time.deltaTime;
        }

        // CharacterController has no gravity of its own, so it is applied by hand.
        // isGrounded reflects the previous Move call.
        void ApplyGravity()
        {
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = groundedStickSpeed;

            _verticalVelocity += Physics.gravity.y * Time.deltaTime;
        }

        void SkipReachedWaypoints()
        {
            while (HasPath && IsWithinArrivalRadius(transform.position, _path[_targetIndex]))
                _targetIndex++;
        }

        // Compared on the ground plane only: the agent's pivot height and the waypoint
        // height may differ, which should not stop it counting as arrived. Squared
        // distances avoid a square root every frame.
        bool IsWithinArrivalRadius(Vector3 position, Vector3 waypoint)
        {
            float dx = position.x - waypoint.x;
            float dz = position.z - waypoint.z;
            return dx * dx + dz * dz <= arrivalRadius * arrivalRadius;
        }
    }
}
