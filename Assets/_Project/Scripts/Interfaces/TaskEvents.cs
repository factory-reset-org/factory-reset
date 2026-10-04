using System;
using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Announces task props created during play, such as the Chapter 3 keycard pickup that
    /// Saboteur A drops. Props placed in a scene need nothing: the chapter manager finds them
    /// once when the journey begins.
    /// </summary>
    public static class TaskEvents
    {
        /// <summary>
        /// A task prop was spawned. The task should be a component, so the chapter manager can
        /// use its transform as the objective position.
        /// </summary>
        public static event Action<ITask> OnTaskSpawned;

        /// <summary>Notifies listeners that <paramref name="task"/> now exists in the level.</summary>
        public static void RaiseTaskSpawned(ITask task) => OnTaskSpawned?.Invoke(task);

        // Domain reload is off in this project, so static event subscribers survive
        // between play sessions unless they are explicitly cleared.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearListeners()
        {
            OnTaskSpawned = null;
        }
    }
}
