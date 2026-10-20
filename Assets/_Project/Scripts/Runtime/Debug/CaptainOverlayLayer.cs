using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// What the Captain is thinking, drawn in the level:
    /// <list type="bullet">
    /// <item>every candidate goal with its live probability P(g), the most likely one in green;</item>
    /// <item>the player's predicted route to that goal, in yellow;</item>
    /// <item>the cell it is heading for or holding, in magenta, with the player's and its own
    /// arrival times (the intercept inequality t_captain + 1 s ≤ t_player).</item>
    /// </list>
    /// </summary>
    public sealed class CaptainOverlayLayer : IAgentOverlayLayer
    {
        static readonly Color GoalColour = new Color(1f, 1f, 1f, 0.85f);
        static readonly Color BestGoalColour = new Color(0.35f, 1f, 0.45f);
        static readonly Color RouteColour = new Color(1f, 0.9f, 0.25f);
        static readonly Color TargetColour = new Color(1f, 0.3f, 1f);

        public string Name => "Captain";

        public bool Handles(AgentController agent) => agent.Brain is CaptainBrain;

        public void Draw(AgentController agent, OverlayCanvas canvas)
        {
            var brain = (CaptainBrain)agent.Brain;
            if (canvas.Grid == null || !brain.IsAwake)
                return;

            IReadOnlyList<CandidateGoal> goals = brain.Goals;
            int best = -1;
            float bestP = 0f;
            for (int i = 0; i < goals.Count; i++)
            {
                float p = brain.GoalProbability(i);
                if (p > bestP)
                {
                    bestP = p;
                    best = i;
                }
            }

            for (int i = 0; i < goals.Count; i++)
            {
                Color colour = i == best ? BestGoalColour : GoalColour;
                canvas.DrawCell(goals[i].Cell, colour);
                canvas.DrawLabel(canvas.Grid.CellToWorld(goals[i].Cell) + Vector3.up * 0.6f,
                    $"P={brain.GoalProbability(i):0.00}", colour);
            }

            if (brain.Confidence > 0f)
                canvas.DrawCellPath(brain.PredictedRoute, RouteColour);

            if (brain.HasTarget)
            {
                canvas.DrawCell(brain.TargetCell, TargetColour, inset: 0f);
                canvas.DrawCell(brain.TargetCell, TargetColour, inset: 0.08f);
                InterceptPlan plan = brain.Plan;
                string timing = plan.HasPlan
                    ? $"{plan.Kind}\nplayer {plan.PlayerArrival:0.00}s  captain {plan.CaptainArrival:0.00}s  lead {plan.Lead:0.00}s"
                    : "holding";
                canvas.DrawLabel(canvas.Grid.CellToWorld(brain.TargetCell) + Vector3.up * 0.9f, timing, TargetColour);
            }
        }
    }
}
