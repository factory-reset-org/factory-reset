using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core;

namespace ToyFactory.AI.Agents.Mock
{
    /// <summary>
    /// A stand-in brain that does no reasoning: it hands the body a fixed list of
    /// world-space waypoints. Used to build and test the movement pipeline
    /// (path follower, rotation, animation) before any real brain exists, and swapped
    /// for a real <see cref="IAgentBrain"/> later without changing the body code.
    /// </summary>
    public sealed class MockPathProvider : IAgentBrain
    {
        /// <summary>Default movement speed in metres per second.</summary>
        public const float DefaultSpeed = 3f;

        /// <summary>
        /// How close (in metres, measured on the ground plane) the agent must be to the
        /// last waypoint to count as having finished the route.
        /// </summary>
        public const float ArrivalRadius = 0.3f;

        const string StateName = "MockPatrol";

        readonly List<Vector3> _waypoints;
        readonly float _speed;
        bool _pathSent;

        // True once the agent has moved away from the last waypoint since the route was
        // last sent, so standing on the end point does not resend the route every tick.
        bool _leftEndPoint;

        /// <param name="waypoints">World positions to walk through, in order. Must contain at least one point.</param>
        /// <param name="speed">Desired movement speed in metres per second.</param>
        public MockPathProvider(IReadOnlyList<Vector3> waypoints, float speed = DefaultSpeed)
        {
            if (waypoints == null)
                throw new ArgumentNullException(nameof(waypoints));
            if (waypoints.Count == 0)
                throw new ArgumentException("At least one waypoint is required.", nameof(waypoints));

            _waypoints = new List<Vector3>(waypoints);
            _speed = speed;
        }

        /// <summary>
        /// Returns the full waypoint list on the first tick and again each time the agent
        /// reaches the last waypoint, so it patrols the route in a loop. On every other
        /// tick returns a null path ("keep following the current path").
        /// </summary>
        public AgentIntent Tick(in AgentContext ctx)
        {
            bool atEndPoint = IsAtEndPoint(ctx.Position);
            if (!atEndPoint)
                _leftEndPoint = true;

            List<Vector3> path = null;
            if (!_pathSent || (atEndPoint && _leftEndPoint))
            {
                // Hand out a copy so the body can never modify the brain's own list.
                path = new List<Vector3>(_waypoints);
                _pathSent = true;
                _leftEndPoint = false;
            }

            return new AgentIntent
            {
                Path = path,
                DesiredSpeed = _speed,
                Action = AgentAction.None,
                DebugState = StateName
            };
        }

        bool IsAtEndPoint(Vector3 position)
        {
            // Compare on the ground plane only: the agent's pivot height and the
            // waypoint height may differ, which should not stop it counting as arrived.
            Vector3 end = _waypoints[_waypoints.Count - 1];
            float dx = position.x - end.x;
            float dz = position.z - end.z;
            return dx * dx + dz * dz <= ArrivalRadius * ArrivalRadius;
        }

        /// <summary>Ignored: the mock always follows its fixed waypoints.</summary>
        public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }

        /// <summary>Ignored: the mock has no behaviour to interrupt.</summary>
        public void OnStunned(float duration) { }
    }
}
