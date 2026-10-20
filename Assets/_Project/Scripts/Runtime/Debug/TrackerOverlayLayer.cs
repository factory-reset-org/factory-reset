using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Agents.Tracker;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// What the Tracker is thinking, drawn in the level (F3). The base layer already labels its
    /// state and alert level and draws the route the body is walking; this layer adds:
    /// <list type="bullet">
    /// <item>its top state (Calm, Hunting, Rewind, Stunned) and wind-up spring, under its feet;</item>
    /// <item>the GBFS route cells it planned, in orange;</item>
    /// <item>every noise it remembers with its decayed score: the one it would follow linked to
    /// it, repeating sources in pink, handled ones in grey;</item>
    /// <item>the player's last known position while hunting, the shut door it is waiting at,
    /// and the three Search rings;</item>
    /// <item>a heatmap of how the latest noise in the level spread through the grid (cold to
    /// hot by propagated level, for 4 s), from S1's <see cref="NoisePropagation"/>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The heatmap runs its own propagation once per new noise (not per frame), and draws every
    /// other cell (1 m apart) to keep the line count down. Reading the brain never changes it:
    /// <see cref="TrackerBrain.GetRememberedNoises"/> does not forget decayed noises.
    /// </remarks>
    public sealed class TrackerOverlayLayer : IAgentOverlayLayer
    {
        /// <summary>How long the heatmap of a noise stays up.</summary>
        public const float HeatmapSeconds = 4f;
        const int HeatmapStride = 2;
        static readonly float[] SearchRingRadii = { 1.5f, 3f, 4.5f };

        static readonly Color SummaryColour = new Color(0.75f, 0.95f, 1f);
        static readonly Color RouteColour = new Color(1f, 0.62f, 0.11f);
        static readonly Color NoiseColour = new Color(1f, 0.85f, 0.25f);
        static readonly Color BestNoiseColour = new Color(1f, 0.4f, 0.2f);
        static readonly Color RepeatingColour = new Color(1f, 0.36f, 0.66f);
        static readonly Color HandledColour = new Color(0.6f, 0.6f, 0.6f);
        static readonly Color LastKnownColour = new Color(1f, 0.25f, 0.25f);
        static readonly Color DoorColour = new Color(1f, 0.15f, 0.15f);
        static readonly Color SearchColour = new Color(1f, 0.9f, 0.3f);
        static readonly Color QuietColour = new Color(0.25f, 0.55f, 1f, 0.8f);
        static readonly Color MidColour = new Color(1f, 0.85f, 0.2f, 0.8f);
        static readonly Color LoudColour = new Color(1f, 0.2f, 0.15f, 0.9f);

        readonly List<RememberedNoise> _noises = new List<RememberedNoise>();
        NoisePropagation _heat;
        GridGraph _heatGrid;
        NoiseEvent _lastNoise;
        bool _hasNoise;
        bool _heatStale;
        float _heardAt;
        int _heatDrawnFrame = -1;

        public string Name => "Tracker";

        public bool Handles(AgentController agent) => agent.Brain is TrackerBrain;

        /// <summary>The latest noise anywhere in the level; the heatmap shows how it spread.</summary>
        public void Hear(NoiseEvent noise)
        {
            _lastNoise = noise;
            _hasNoise = true;
            _heatStale = true;
            _heardAt = Time.time;
        }

        /// <summary>Cells drawn in the last heatmap, for tests and the optimisation log.</summary>
        public int HeatmapCellsDrawn { get; private set; }

        public void Draw(AgentController agent, OverlayCanvas canvas)
        {
            var brain = (TrackerBrain)agent.Brain;
            Vector3 feet = agent.transform.position;

            string spring = $"spring {brain.Energy01 * 100f:0}%" + (brain.IsRewinding ? " rewinding, hits x2" : "");
            canvas.DrawLabel(feet + Vector3.up * 0.35f, $"{brain.TopStateName} · {spring}", SummaryColour);

            DrawHeatmap(canvas);   // once per frame, whatever the number of Trackers
            if (canvas.Grid == null)
                return;

            canvas.DrawCellPath(brain.RouteCells, RouteColour, height: 0.12f);
            DrawNoises(brain, feet, canvas);

            if (brain.HasLastKnownPosition && brain.TopStateName == "Hunting")
            {
                Vector2Int lastKnown = canvas.Grid.WorldToCell(brain.LastKnownPosition);
                canvas.DrawCell(lastKnown, LastKnownColour, inset: 0f);
                canvas.DrawLabel(canvas.Grid.CellToWorld(lastKnown) + Vector3.up * 0.5f, "last seen", LastKnownColour);
            }

            if (brain.IsBlockedByDoor)
            {
                canvas.DrawCell(brain.BlockingDoorCell, DoorColour, inset: 0f);
                canvas.DrawCell(brain.BlockingDoorCell, DoorColour, inset: 0.1f);
                canvas.DrawLabel(canvas.Grid.CellToWorld(brain.BlockingDoorCell) + Vector3.up * 1.2f, "shut door", DoorColour);
            }

            if (brain.IsSearching)
                foreach (float radius in SearchRingRadii)
                    DrawCircle(canvas, brain.SearchCentre + Vector3.up * 0.1f, radius, SearchColour);
        }

        void DrawNoises(TrackerBrain brain, Vector3 feet, OverlayCanvas canvas)
        {
            _noises.Clear();
            brain.GetRememberedNoises(_noises);
            int best = -1;
            for (int i = 0; i < _noises.Count; i++)
                if (!_noises[i].IsHandled && (best < 0 || _noises[i].Score > _noises[best].Score))
                    best = i;

            for (int i = 0; i < _noises.Count; i++)
            {
                RememberedNoise noise = _noises[i];
                Color colour = noise.IsHandled ? HandledColour
                    : i == best ? BestNoiseColour
                    : noise.IsRepeating ? RepeatingColour
                    : NoiseColour;
                Vector2Int cell = canvas.Grid.WorldToCell(noise.Position);
                canvas.DrawCell(cell, colour, inset: 0f);
                string tags = (noise.IsRepeating ? " repeating" : "") + (noise.IsHandled ? " handled" : "");
                canvas.DrawLabel(canvas.Grid.CellToWorld(cell) + Vector3.up * 0.6f,
                    $"noise #{noise.SourceId} score {noise.Score:0}{tags}", colour);
                if (i == best)
                    canvas.DrawLine(feet + Vector3.up * 0.2f, canvas.Grid.CellToWorld(cell) + Vector3.up * 0.2f, colour);
            }
        }

        void DrawHeatmap(OverlayCanvas canvas)
        {
            if (_heatDrawnFrame == Time.frameCount)
                return;
            _heatDrawnFrame = Time.frameCount;
            HeatmapCellsDrawn = 0;

            GridGraph grid = canvas.Grid;
            if (grid == null || !_hasNoise || Time.time - _heardAt > HeatmapSeconds)
                return;

            if (_heatGrid != grid)
            {
                _heat = new NoisePropagation(grid);
                _heatGrid = grid;
                _heatStale = true;
            }
            if (_heatStale || _heat.GraphVersion != grid.Version)
            {
                _heat.Propagate(grid.WorldToCell(_lastNoise.Position), _lastNoise.Loudness);
                _heatStale = false;
            }
            if (_heat.CellsReached == 0)
                return;

            float loudness = _lastNoise.Loudness;
            float range = Mathf.Max(1f, loudness - NoisePropagation.HearingThreshold);
            int radius = Mathf.CeilToInt(NoisePropagation.AudibleRadius(loudness) / GridGraph.CellSize);
            Vector2Int source = _heat.Source;
            // Keep the grid's own parity, so the dots do not shimmer as the source moves.
            int startX = source.x - radius - ((source.x - radius) % HeatmapStride + HeatmapStride) % HeatmapStride;
            int startY = source.y - radius - ((source.y - radius) % HeatmapStride + HeatmapStride) % HeatmapStride;
            for (int y = startY; y <= source.y + radius; y += HeatmapStride)
            for (int x = startX; x <= source.x + radius; x += HeatmapStride)
            {
                var cell = new Vector2Int(x, y);
                float level = _heat.Level(cell);
                if (level <= 0f)
                    continue;
                canvas.DrawCell(cell, HeatColour((level - NoisePropagation.HearingThreshold) / range), inset: 0.12f, height: 0.03f);
                HeatmapCellsDrawn++;
            }
            canvas.DrawLabel(grid.CellToWorld(source) + Vector3.up * 1.5f,
                $"noise {loudness:0} heard {NoisePropagation.AudibleRadius(loudness):0.0} m out", LoudColour);
        }

        /// <summary>Cold (just audible) to hot (at the source).</summary>
        static Color HeatColour(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? Color.Lerp(QuietColour, MidColour, t * 2f) : Color.Lerp(MidColour, LoudColour, (t - 0.5f) * 2f);
        }

        static void DrawCircle(OverlayCanvas canvas, Vector3 centre, float radius, Color colour)
        {
            const int Segments = 24;
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
        static void Register()
        {
            var layer = new TrackerOverlayLayer();
            NoiseEvents.OnNoise += layer.Hear;   // NoiseEvents clears its listeners each play session
            AgentDebugOverlay.Register(layer);
        }
    }
}
