using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>Game-wide notification channel for changes to active objectives.</summary>
    public static class ObjectiveEvents
    {
        /// <summary>Raised synchronously with the complete current objective target list.</summary>
        public static event Action<IReadOnlyList<ObjectiveTargetInfo>> OnTargetsChanged;

        /// <summary>Notifies current listeners that the active objective targets changed.</summary>
        public static void RaiseTargetsChanged(IReadOnlyList<ObjectiveTargetInfo> targets) =>
            OnTargetsChanged?.Invoke(targets);

        // Domain reload is off in this project, so static event subscribers survive
        // between play sessions unless they are explicitly cleared.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearListeners()
        {
            OnTargetsChanged = null;
        }
    }
}
