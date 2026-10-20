using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Publishes the current player so assemblies that cannot reference the player's
    /// type directly can read it. Null means there is no player in the scene.
    /// </summary>
    public static class PlayerState
    {
        public static IPlayerState Current { get; private set; }

        public static void Publish(IPlayerState player) => Current = player;

        // Domain reload is off in this project, so clear the published player at the
        // start of each play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOnLoad() => Current = null;
    }
}
