using UnityEngine;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// The Saboteurs' layer on the F3 debug overlay. Above each Saboteur it lists its latest decision:
    /// every candidate action with its score and why it lost, the action it is carrying out and, for a
    /// door, the target it holds. The four labels together are the squad view: each instance's letter,
    /// action, claimed target and whether it is still standing. The route it runs is the base layer's.
    /// </summary>
    /// <remarks>
    /// It reads <see cref="SaboteurBrain.LastDecision"/> and never scores or controls anything. The
    /// individual considerations of a candidate are not shown: the candidate sources compute them and
    /// hand the brain only the result, so the trace holds each action's raw and final score.
    /// </remarks>
    public sealed class SaboteurOverlayLayer : IAgentOverlayLayer
    {
        static readonly Color SquadColour = new Color(1f, 0.75f, 0.9f);
        static readonly Color DecisionColour = new Color(0.95f, 0.85f, 1f);
        static readonly Color DownColour = new Color(0.6f, 0.6f, 0.6f);

        public string Name => "Saboteur";

        public bool Handles(AgentController agent) => agent.Brain is SaboteurBrain;

        public void Draw(AgentController agent, OverlayCanvas canvas)
        {
            var brain = (SaboteurBrain)agent.Brain;
            Vector3 feet = agent.transform.position;

            canvas.DrawLabel(feet + Vector3.up * 0.35f, SquadLine(brain), brain.IsDestroyed ? DownColour : SquadColour);
            if (brain.IsDestroyed)
                return;

            canvas.DrawLabel(feet + Vector3.up * 0.05f, UtilityDecisionText.Format(brain.LastDecision), DecisionColour);
        }

        /// <summary>
        /// The squad row: "A  Flee  health 25%", or "B  CloseDoor(2)  health 100%" for a held target,
        /// or "C  destroyed".
        /// </summary>
        public static string SquadLine(SaboteurBrain brain)
        {
            string letter = brain.Identity.Letter.ToString();
            if (brain.IsDestroyed)
                return $"{letter}  destroyed";

            string action = brain.LastDecision.HasSelection ? brain.LastDecision.Selected.ToString() : "no action";
            return $"{letter}  {action}  health {Mathf.RoundToInt(brain.HealthFraction * 100f)}%";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register() => AgentDebugOverlay.Register(new SaboteurOverlayLayer());
    }
}
