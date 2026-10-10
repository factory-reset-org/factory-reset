using System;
using System.Collections.Generic;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Lets agents hear. For every <see cref="NoiseEvents.Emit"/> it spreads the noise once
    /// with S1's <see cref="NoisePropagation"/> (through doors and round corners, not through
    /// walls), then tells each listener the level at its own cell. A listener only hears a
    /// level above <see cref="NoisePropagation.HearingThreshold"/>.
    /// </summary>
    /// <remarks>
    /// One propagation per noise, not one per agent: the flood gives every cell's level at
    /// once, so seven agents cost the same as one. The propagation is bounded by how loud
    /// the noise is and allocates nothing after the first noise on a grid. The grid is read
    /// at the moment of each noise, so a grid built after the agents spawn is picked up.
    /// Without a grid, nothing is heard.
    /// </remarks>
    public sealed class AgentHearing : IDisposable
    {
        readonly IReadOnlyList<INoiseListener> _listeners;
        readonly Func<GridGraph> _gridSource;
        NoisePropagation _propagation;
        GridGraph _propagationGrid;
        bool _disposed;

        /// <param name="listeners">The agents; a live list, so agents spawned later are heard too.</param>
        /// <param name="gridSource">Returns the current level grid (or null) when a noise happens.</param>
        public AgentHearing(IReadOnlyList<INoiseListener> listeners, Func<GridGraph> gridSource)
        {
            _listeners = listeners ?? throw new ArgumentNullException(nameof(listeners));
            _gridSource = gridSource ?? throw new ArgumentNullException(nameof(gridSource));
            NoiseEvents.OnNoise += Hear;
        }

        /// <summary>Cells the last noise reached, for the performance log; 0 before any noise.</summary>
        public int LastCellsReached => _propagation?.CellsReached ?? 0;

        /// <summary>Stops listening. Nothing is heard after this.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            NoiseEvents.OnNoise -= Hear;
        }

        /// <summary>Spreads one noise and passes it to every listener that hears it.</summary>
        public void Hear(NoiseEvent noise)
        {
            GridGraph grid = _gridSource();
            if (grid == null || _listeners.Count == 0)
                return;

            // One propagation object per grid; a rebuilt grid gets a new one.
            if (_propagation == null || _propagationGrid != grid)
            {
                _propagation = new NoisePropagation(grid);
                _propagationGrid = grid;
            }

            _propagation.Propagate(grid.WorldToCell(noise.Position), noise.Loudness);

            for (int i = 0; i < _listeners.Count; i++)
            {
                INoiseListener listener = _listeners[i];
                if (listener == null || !listener.CanHear)
                    continue;

                // LevelNear: an agent beside a wall or box stands in a cell the noise skips.
                float level = _propagation.LevelNear(grid.WorldToCell(listener.HearingPosition));
                if (level > NoisePropagation.HearingThreshold)
                    listener.Hear(new SensorSnapshot(noise.Position, level, noise.SourceId, noise.Time, noise.IsLure));
            }
        }
    }
}
