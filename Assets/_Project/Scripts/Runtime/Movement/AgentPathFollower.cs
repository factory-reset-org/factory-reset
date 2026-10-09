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

        [Tooltip("Metres per second gained or lost per second when the speed changes: starting off, or a new route at a different speed. 12 reaches 4.6 m/s in about 0.4 s.")]
        [SerializeField, Min(0.1f)] float acceleration = 12f;

        // Reused for every route so setting a new path does not allocate.
        readonly List<Vector3> _path = new List<Vector3>();

        CharacterController _controller;
        int _targetIndex;
        float _speed;        // the route's speed
        float _moveSpeed;    // the speed now, easing towards _speed
        float _verticalVelocity;
        bool _holding;
        Vector3 _holdLook;
        bool _facing;
        Vector3 _facePoint;

        /// <summary>True while there are waypoints left to walk to.</summary>
        public bool HasPath => _targetIndex < _path.Count;

        /// <summary>Waypoints still to walk, for the debug overlay.</summary>
        public int RemainingWaypointCount => Mathf.Max(0, _path.Count - _targetIndex);

        /// <summary>The <paramref name="index"/>-th waypoint still to walk (0 = the next one).</summary>
        public Vector3 RemainingWaypoint(int index) => _path[_targetIndex + index];

        /// <summary>True while <see cref="Hold"/> keeps the agent standing where it is.</summary>
        public bool IsHolding => _holding;

        /// <summary>Horizontal speed this frame in metres per second, for animation.</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>
        /// The direction it last moved in, on the ground plane (unit length, or zero before it
        /// has moved). Not always where it faces: aiming on the move turns the body, not the path.
        /// </summary>
        public Vector3 MoveDirection { get; private set; }

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
            SkipPassedFirstWaypoint();
        }

        // A new route starts at the centre of the cell the agent stands in. Re-planned
        // mid-walk, that centre is often just behind it, and walking back to it first shows as
        // a hitch. If the agent is already nearer the next waypoint than the first one is, it
        // has effectively passed the first: go straight on.
        void SkipPassedFirstWaypoint()
        {
            if (_path.Count - _targetIndex < 2)
                return;
            Vector3 first = _path[_targetIndex];
            Vector3 next = _path[_targetIndex + 1];
            if (FlatSqrDistance(transform.position, next) < FlatSqrDistance(first, next))
                _targetIndex++;
        }

        static float FlatSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// Keeps the agent standing where it is, turning to face <paramref name="lookAt"/>,
        /// without dropping its route: <see cref="Release"/> carries on along it.
        /// </summary>
        public void Hold(Vector3 lookAt)
        {
            _holding = true;
            _holdLook = lookAt;
        }

        /// <summary>Ends a <see cref="Hold"/>; the agent walks its route again.</summary>
        public void Release() => _holding = false;

        /// <summary>
        /// Turns the agent to face <paramref name="point"/> while it keeps walking its route,
        /// for aiming on the move. Lasts until <see cref="StopFacing"/>.
        /// </summary>
        public void FaceTowards(Vector3 point)
        {
            _facing = true;
            _facePoint = point;
        }

        /// <summary>Ends <see cref="FaceTowards"/>; the agent faces where it walks again.</summary>
        public void StopFacing() => _facing = false;

        /// <summary>Degrees between where the agent faces and <paramref name="point"/>, on the ground plane.</summary>
        public float FacingErrorTo(Vector3 point)
        {
            Vector3 to = point - transform.position;
            to.y = 0f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return to.sqrMagnitude < 0.0001f ? 0f : Vector3.Angle(forward, to);
        }

        /// <summary>Clears the route; the agent stops where it is.</summary>
        public void Stop()
        {
            _path.Clear();
            _targetIndex = 0;
            _moveSpeed = 0f;
        }

        void Update()
        {
            Vector3 horizontalVelocity = Vector3.zero;
            Vector3 facing = Vector3.zero;

            if (_holding)
            {
                facing = _holdLook - transform.position;
                facing.y = 0f;
                _moveSpeed = 0f;   // starts off again from standing when released
            }
            else if (HasPath)
            {
                Vector3 toTarget = _path[_targetIndex] - transform.position;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;

                if (distance > 0f)
                {
                    // Ease towards the route's speed, so starting off and a new route at another
                    // speed do not jump; never step further than the remaining distance, so the
                    // agent does not overshoot the waypoint on a long frame.
                    _moveSpeed = Mathf.MoveTowards(_moveSpeed, _speed, acceleration * Time.deltaTime);
                    float frameSpeed = Mathf.Min(_moveSpeed, distance / Time.deltaTime);
                    horizontalVelocity = toTarget / distance * frameSpeed;
                    facing = horizontalVelocity;
                    MoveDirection = toTarget / distance;
                }
            }
            else
            {
                _moveSpeed = 0f;
            }

            if (_facing && !_holding)
            {
                facing = _facePoint - transform.position;
                facing.y = 0f;
            }

            TurnTowards(facing);
            ApplyGravity();

            Vector3 velocity = horizontalVelocity;
            velocity.y = _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
            CurrentSpeed = horizontalVelocity.magnitude;

            SkipReachedWaypoints();
        }

        // Turns to face the direction of travel (or the held look point), on the ground plane
        // only, at most turnSpeed degrees per second so the agent never snaps round instantly.
        void TurnTowards(Vector3 direction)
        {
            float previousYaw = transform.eulerAngles.y;

            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
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
