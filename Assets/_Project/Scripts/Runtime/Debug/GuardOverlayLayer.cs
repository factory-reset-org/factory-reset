using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// The Guard's layer on the F3 debug overlay. It shows what the Guard is deciding with:
    /// its state and the player's battery tier, the ring at its ideal distance round the
    /// player, the cover cells it costed with their scores, the one it chose, and its
    /// tactical route there.
    /// </summary>
    public sealed class GuardOverlayLayer : IAgentOverlayLayer
    {
        static readonly Color SummaryColour = new Color(0.8f, 1f, 0.8f);
        static readonly Color RouteColour = new Color(0.3f, 0.85f, 1f);
        static readonly Color ChosenColour = new Color(0.25f, 1f, 0.35f);
        static readonly Color OptionColour = new Color(1f, 0.85f, 0.25f);
        static readonly Color RangeColour = new Color(1f, 0.55f, 0.2f);

        readonly List<ScoredCover> _scored = new List<ScoredCover>();

        public string Name => "Guard";

        public bool Handles(AgentController agent) => agent.Brain is GuardBrain;

        public void Draw(AgentController agent, OverlayCanvas canvas)
        {
            var brain = (GuardBrain)agent.Brain;
            Vector3 feet = agent.transform.position;

            canvas.DrawLabel(feet + Vector3.up * 0.35f,
                $"{brain.StateName} · battery {brain.BatteryTierName} · ideal {brain.IdealRange:0} m", SummaryColour);

            if (canvas.Grid == null)
                return;

            canvas.DrawCellPath(brain.RouteCells, RouteColour, height: 0.12f);
            if (!brain.IsEngaged)
                return;

            DrawCircle(canvas, brain.PlayerPosition + Vector3.up * 0.1f, brain.IdealRange, RangeColour);
            DrawCover(brain, canvas);
        }

        void DrawCover(GuardBrain brain, OverlayCanvas canvas)
        {
            _scored.Clear();
            brain.GetScoredCover(_scored);

            bool chosenShown = false;
            for (int i = 0; i < _scored.Count; i++)
            {
                ScoredCover cover = _scored[i];
                bool chosen = brain.HasCover && cover.Cell == brain.CoverCell;
                chosenShown |= chosen;
                Color colour = chosen ? ChosenColour : OptionColour;

                canvas.DrawCell(cover.Cell, colour, inset: chosen ? 0f : 0.1f);
                string text = $"{(chosen ? "cover" : "option")} S {cover.Score:0.00}\n" +
                              $"P {cover.Protection:0.0}  cost {cover.PathCost:0}{(cover.CanPeek ? "  peek" : "")}";
                canvas.DrawLabel(canvas.Grid.CellToWorld(cover.Cell) + Vector3.up * 0.7f, text, colour);
            }

            // The cover is kept between evaluations, so it can be held without being in the latest list.
            if (brain.HasCover && !chosenShown)
            {
                canvas.DrawCell(brain.CoverCell, ChosenColour, inset: 0f);
                canvas.DrawLabel(canvas.Grid.CellToWorld(brain.CoverCell) + Vector3.up * 0.7f, "cover", ChosenColour);
            }
        }

        static void DrawCircle(OverlayCanvas canvas, Vector3 centre, float radius, Color colour)
        {
            const int Segments = 32;
            Vector3 previous = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= Segments; i++)
            {
                float angle = 2f * Mathf.PI * i / Segments;
                Vector3 next = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                canvas.DrawLine(previous, next, colour);
                previous = next;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register() => AgentDebugOverlay.Register(new GuardOverlayLayer());
    }
}
