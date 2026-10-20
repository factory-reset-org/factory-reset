using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Publishes the active <see cref="IGameClock"/> (GameManager) so assemblies that
    /// cannot reference GameManager's type directly, such as Runtime, can still reach
    /// it. GameManager publishes itself on Awake.
    /// </summary>
    public static class GameClock
    {
        /// <summary>The active game clock, or null before GameManager has woken up.</summary>
        public static IGameClock Current { get; private set; }

        public static void Publish(IGameClock clock) => Current = clock;

        // Domain reload is off in this project, so static fields survive between play
        // sessions. Clear the published clock at the start of each session so a stale
        // reference from the last one is never used.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOnLoad()
        {
            Current = null;
        }
    }
}
