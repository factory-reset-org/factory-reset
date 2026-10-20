using System.Collections.Generic;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Scene marker saying "spawn this type of agent here, facing this way". Placed in
    /// Agents.unity and read by the agent spawner. Holds the agent's squad position (for
    /// the four Saboteurs) and optional patrol waypoints used by brains that need a route.
    /// </summary>
    public sealed class SpawnPoint : MonoBehaviour
    {
        [Tooltip("Which agent appears at this spawn point.")]
        [SerializeField] AgentType agentType = AgentType.Tracker;

        [Tooltip("Position in this type's squad: 0 = A, 1 = B, 2 = C, 3 = D (the four Saboteurs). -1 = not in a squad.")]
        [SerializeField, Range(AgentIdentity.NoSquad, 3)] int squadIndex = AgentIdentity.NoSquad;

        [Tooltip("Optional patrol route markers, in walking order. Empty means the agent stays at the spawn point.")]
        [SerializeField] Transform[] patrolWaypoints = new Transform[0];

        [Tooltip("Captain only: start awake instead of Dormant. For test scenes, which have no Chapter 3 cutscene to wake it.")]
        [SerializeField] bool startAwake;

        /// <summary>Which agent appears here.</summary>
        public AgentType AgentType => agentType;

        /// <summary>Squad position of the agent spawned here, or <see cref="AgentIdentity.NoSquad"/>.</summary>
        public int SquadIndex => squadIndex;

        /// <summary>Captain only: skip the Dormant state (test scenes).</summary>
        public bool StartAwake => startAwake;

        /// <summary>
        /// World positions of the patrol waypoints, skipping any empty slots. If none are
        /// set, returns the spawn point's own position so the route is never empty.
        /// </summary>
        public List<Vector3> GetPatrolPositions()
        {
            var positions = new List<Vector3>(patrolWaypoints.Length);
            foreach (Transform waypoint in patrolWaypoints)
            {
                if (waypoint != null)
                    positions.Add(waypoint.position);
            }

            if (positions.Count == 0)
                positions.Add(transform.position);

            return positions;
        }

        // One colour per agent type so spawn points are easy to tell apart in the Scene view.
        static Color GizmoColour(AgentType type)
        {
            switch (type)
            {
                case AgentType.Tracker: return Color.yellow;
                case AgentType.Guard: return Color.blue;
                case AgentType.Saboteur: return Color.green;
                case AgentType.Captain: return Color.red;
                default: return Color.white;
            }
        }

        void OnDrawGizmos()
        {
            Color colour = GizmoColour(agentType);
            Gizmos.color = colour;

            // Body-sized marker plus an arrow showing which way the agent will face.
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.5f);
            Gizmos.DrawLine(transform.position + Vector3.up, transform.position + Vector3.up + transform.forward);

            // Patrol route, drawn as a loop.
            Gizmos.color = new Color(colour.r, colour.g, colour.b, 0.5f);
            for (int i = 0; i < patrolWaypoints.Length; i++)
            {
                Transform current = patrolWaypoints[i];
                Transform next = patrolWaypoints[(i + 1) % patrolWaypoints.Length];
                if (current == null)
                    continue;

                Gizmos.DrawWireSphere(current.position, 0.3f);
                if (next != null)
                    Gizmos.DrawLine(current.position, next.position);
            }

#if UNITY_EDITOR
            string label = squadIndex >= 0 ? $"{agentType} {(char)('A' + squadIndex)}" : agentType.ToString();
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.2f, label);
#endif
        }
    }
}
