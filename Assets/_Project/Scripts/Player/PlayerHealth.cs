using System;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Player
{
    /// <summary>
    /// The player's health ("integrity"). Agents' shots arrive here through
    /// <see cref="IPlayerState.TakeDamage"/>. At zero the player is recalled: the run ends
    /// and the game goes to the Results state.
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour
    {
        /// <summary><see cref="LastDamageSourceId"/> before anything has hit the player.</summary>
        public const int NoSource = -1;

        [SerializeField, Min(1f)] float maxHealth = 100f;

        public float Current { get; private set; }

        /// <summary>Health left, 0 (recalled) to 1 (full).</summary>
        public float Fraction => Current / maxHealth;

        public bool IsAlive => Current > 0f;

        /// <summary>Id of the agent whose shot landed last, for the Results screen.</summary>
        public int LastDamageSourceId { get; private set; } = NoSource;

        /// <summary>Raised on every hit that did damage: the amount and the agent's id.</summary>
        public event Action<float, int> OnDamaged;

        /// <summary>Raised once, when health reaches zero: the id of the agent that did it.</summary>
        public event Action<int> OnDied;

        void Awake() => Current = maxHealth;

        public void TakeDamage(float amount, int sourceAgentId)
        {
            if (!IsAlive || amount <= 0f)
                return;

            // A shot already in the air when a cutscene or pause begins does no harm.
            IGameClock clock = GameClock.Current;
            if (clock != null && clock.State != GameState.Playing)
                return;

            Current = Mathf.Max(0f, Current - amount);
            LastDamageSourceId = sourceAgentId;
            OnDamaged?.Invoke(amount, sourceAgentId);
            if (IsAlive)
                return;

            OnDied?.Invoke(sourceAgentId);
            clock?.RequestState(GameState.Results);
        }
    }
}
