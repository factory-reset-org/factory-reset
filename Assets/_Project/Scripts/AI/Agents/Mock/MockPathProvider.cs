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

        const string StateName = "MockPatrol";

        readonly List<Vector3> _waypoints;
        readonly float _speed;
        bool _pathSent;

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
        /// Returns the full waypoint list on the first tick, then a null path
        /// ("keep following the current path") on every tick after that.
        /// </summary>
        public AgentIntent Tick(in AgentContext ctx)
        {
            List<Vector3> path = null;
            if (!_pathSent)
            {
                // Hand out a copy so the body can never modify the brain's own list.
                path = new List<Vector3>(_waypoints);
                _pathSent = true;
            }

            return new AgentIntent
            {
                Path = path,
                DesiredSpeed = _speed,
                Action = AgentAction.None,
                DebugState = StateName
            };
        }

        /// <summary>Ignored: the mock always follows its fixed waypoints.</summary>
        public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }

        /// <summary>Ignored: the mock has no behaviour to interrupt.</summary>
        public void OnStunned(float duration) { }
    }
}
