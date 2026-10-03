using System;
using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>Game-wide notification channel for sounds emitted into the world.</summary>
    public static class NoiseEvents
    {
        /// <summary>Raised synchronously when a sound occurs.</summary>
        public static event Action<NoiseEvent> OnNoise;

        /// <summary>Notifies current listeners that a sound occurred.</summary>
        public static void Emit(NoiseEvent e) => OnNoise?.Invoke(e);

        // Domain reload is off in this project, so static event subscribers survive
        // between play sessions unless they are explicitly cleared.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearListeners()
        {
            OnNoise = null;
        }
    }
}
