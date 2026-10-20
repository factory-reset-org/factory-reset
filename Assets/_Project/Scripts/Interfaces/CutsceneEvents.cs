using System;
using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Game-wide cutscene events. Only the cutscene director raises them; the game-state
    /// manager, the Captain's runtime, doors and the HUD listen. Listeners subscribe in
    /// <c>OnEnable</c> and unsubscribe in <c>OnDisable</c>.
    /// </summary>
    public static class CutsceneEvents
    {
        /// <summary>A cutscene started playing. Passes the cutscene's id.</summary>
        public static event Action<string> OnCutsceneStarted;

        /// <summary>A cutscene finished or was skipped. Passes the cutscene's id.</summary>
        public static event Action<string> OnCutsceneEnded;

        /// <summary>
        /// A Critical signal fired: a change the game must not miss, such as the Captain
        /// waking. Passes one of the <see cref="CutsceneSignals"/> ids. Raised when the
        /// Timeline reaches the signal, or straight away on skip for every Critical signal
        /// not yet reached, so skipping a cutscene can never leave the game stuck.
        /// </summary>
        public static event Action<string> OnCriticalSignal;

        public static void RaiseCutsceneStarted(string cutsceneId) => OnCutsceneStarted?.Invoke(cutsceneId);

        public static void RaiseCutsceneEnded(string cutsceneId) => OnCutsceneEnded?.Invoke(cutsceneId);

        public static void RaiseCriticalSignal(string signalId) => OnCriticalSignal?.Invoke(signalId);

        // Domain reload is off in this project, so static fields survive between play
        // sessions. Clear the listeners at the start of each session so a listener left
        // over from the last one is never called.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearListeners()
        {
            OnCutsceneStarted = null;
            OnCutsceneEnded = null;
            OnCriticalSignal = null;
        }
    }
}
