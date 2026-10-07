using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Movement;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// The overlay's base layer, for every agent: a label above its head (name, state, alert
    /// level, hit points) and the route it still has to walk.
    /// </summary>
    public sealed class AgentOverlayLayer : IAgentOverlayLayer
    {
        static readonly Color RouteColour = new Color(0.3f, 0.85f, 1f);
        static readonly Color CalmColour = Color.white;
        static readonly Color SuspiciousColour = new Color(1f, 0.85f, 0.2f);
        static readonly Color AlertColour = new Color(1f, 0.35f, 0.3f);
        static readonly Color DownColour = new Color(0.6f, 0.6f, 0.6f);

        public string Name => "Agents";

        public bool Handles(AgentController agent) => true;

        public void Draw(AgentController agent, OverlayCanvas canvas)
        {
            Vector3 feet = agent.transform.position;
            var capsule = agent.GetComponent<CharacterController>();
            float height = capsule != null ? capsule.height : 2f;

            canvas.DrawLabel(feet + Vector3.up * (height + 0.9f), Describe(agent), ColourFor(agent));

            AgentPathFollower follower = agent.Follower;
            if (follower == null || agent.IsDead || agent.IsDisabled)
                return;
            Vector3 lift = Vector3.up * 0.1f;
            Vector3 previous = feet + lift;
            for (int i = 0; i < follower.RemainingWaypointCount; i++)
            {
                Vector3 next = follower.RemainingWaypoint(i) + lift;
                canvas.DrawLine(previous, next, RouteColour);
                previous = next;
            }
        }

        /// <summary>The label text: name, state, alert and hit points, or why it is not acting.</summary>
        public static string Describe(AgentController agent)
        {
            if (agent.IsDead)
                return $"{agent.name}\nscrapped";
            if (agent.IsDisabled)
                return $"{agent.name}\nknocked out {agent.KnockOutTimeLeft:0.0}s";
            string alert = agent.Alert == AlertLevel.Alert ? " !" : agent.Alert == AlertLevel.Suspicious ? " ?" : "";
            return $"{agent.name}\n{agent.DebugState}{alert}  HP {agent.HitPointsLeft}/{agent.MaxHitPoints}";
        }

        static Color ColourFor(AgentController agent)
        {
            if (agent.IsDead || agent.IsDisabled)
                return DownColour;
            switch (agent.Alert)
            {
                case AlertLevel.Alert: return AlertColour;
                case AlertLevel.Suspicious: return SuspiciousColour;
                default: return CalmColour;
            }
        }
    }
}
