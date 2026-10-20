using System;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Wakes the Captain: when the Chapter 3 cutscene fires its
    /// <see cref="CutsceneSignals.CaptainWake"/> Critical signal, sets
    /// <see cref="WorldBlackboard.CaptainAwake"/>. The director fires the signal on skip too,
    /// so skipping the cutscene cannot leave the Captain asleep. Brains cannot listen to
    /// cutscene events themselves, so this is how the wake reaches the Captain's brain.
    /// </summary>
    public sealed class CaptainWakeWriter : IDisposable
    {
        readonly WorldBlackboard _blackboard;
        bool _disposed;

        public CaptainWakeWriter(WorldBlackboard blackboard)
        {
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            CutsceneEvents.OnCriticalSignal += HandleCriticalSignal;
        }

        /// <summary>Stops listening. The writer does nothing after this.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            CutsceneEvents.OnCriticalSignal -= HandleCriticalSignal;
        }

        void HandleCriticalSignal(string signalId)
        {
            if (signalId == CutsceneSignals.CaptainWake)
                _blackboard.SetCaptainAwake();
        }
    }
}
