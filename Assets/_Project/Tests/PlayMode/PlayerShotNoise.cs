using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Tests
{
    /// <summary>
    /// For the Saboteur scene tests: the noise the player's blaster makes (the same loudness and source id as
    /// <c>PlayerBlaster</c>), fired where the player stands. A Saboteur only acts on a player it has seen or heard,
    /// so a test that wants the squad to know the player is about has the player shoot, as they would in play.
    /// </summary>
    internal static class PlayerShotNoise
    {
        const int PlayerSourceId = -1;
        static float _lastAt = float.NegativeInfinity;

        /// <summary>A shot now.</summary>
        public static void Fire()
        {
            IPlayerState player = PlayerState.Current;
            if (player == null || (player is Object unityObject && unityObject == null))
                return;

            float now = GameClock.Current != null ? GameClock.Current.GameTime : Time.time;
            NoiseEvents.Emit(new NoiseEvent(player.Position, NoiseLoudness.BlasterShot, PlayerSourceId, now));
            _lastAt = Time.realtimeSinceStartup;
        }

        /// <summary>A shot unless one was fired less than <paramref name="seconds"/> ago (real time).</summary>
        public static void Every(float seconds)
        {
            if (Time.realtimeSinceStartup - _lastAt >= seconds || Time.realtimeSinceStartup < _lastAt)
                Fire();
        }
    }
}
