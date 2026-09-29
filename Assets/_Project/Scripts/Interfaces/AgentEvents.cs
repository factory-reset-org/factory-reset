using System;
using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Game-wide agent life-cycle events. Only <c>AgentController</c> raises them; scoring,
    /// the HUD and the Saboteur squad listen. Listeners subscribe in <c>OnEnable</c> and
    /// unsubscribe in <c>OnDisable</c>.
    /// </summary>
    public static class AgentEvents
    {
        /// <summary>An agent was knocked out (stunned or fallen apart) and will come back.</summary>
        public static event Action<IAgentState> OnDisabled;

        /// <summary>An agent was scrapped for good. Raised once per agent.</summary>
        public static event Action<IAgentState> OnDestroyed;

        /// <summary>A disabled agent has reassembled and is active again.</summary>
        public static event Action<IAgentState> OnRebooted;

        public static void RaiseDisabled(IAgentState agent) => OnDisabled?.Invoke(agent);

        public static void RaiseDestroyed(IAgentState agent) => OnDestroyed?.Invoke(agent);

        public static void RaiseRebooted(IAgentState agent) => OnRebooted?.Invoke(agent);

        // Domain reload is off in this project, so static fields survive between play
        // sessions. Clear the listeners at the start of each session so a listener left
        // over from the last one is never called.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearListeners()
        {
            OnDisabled = null;
            OnDestroyed = null;
            OnRebooted = null;
        }
    }
}
