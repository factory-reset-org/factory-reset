using System;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;

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

        [Tooltip("Seconds without a hit before integrity starts to come back.")]
        [SerializeField, Min(0f)] float regenDelay = 5f;

        [Tooltip("Integrity restored a second once it does. The prototype's 3.")]
        [SerializeField, Min(0f)] float regenPerSecond = 3f;

        // A hit of less than this makes no sound, so the thin graze of a trap or a far shot is quiet.
        const float HurtSoundThreshold = 4f;

        float _lastDamageAt = float.NegativeInfinity;

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

        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        // Integrity comes back by itself once the player has been left alone for a few seconds.
        void Update()
        {
            if (!IsAlive || Current >= maxHealth || regenPerSecond <= 0f)
                return;

            IGameClock clock = GameClock.Current;
            if (clock != null && clock.State != GameState.Playing)
                return;

            if (Now - _lastDamageAt < regenDelay)
                return;

            Current = Mathf.Min(maxHealth, Current + regenPerSecond * Time.deltaTime);
        }

        public void TakeDamage(float amount, int sourceAgentId)
        {
            if (!IsAlive || amount <= 0f)
                return;

            // A shot already in the air when a cutscene or pause begins does no harm.
            IGameClock clock = GameClock.Current;
            if (clock != null && clock.State != GameState.Playing)
                return;

            Current = Mathf.Max(0f, Current - amount);
            _lastDamageAt = Now;
            LastDamageSourceId = sourceAgentId;
            if (amount >= HurtSoundThreshold)
                GameSfx.Play(Sfx.Hurt);
            OnDamaged?.Invoke(amount, sourceAgentId);
            if (IsAlive)
                return;

            OnDied?.Invoke(sourceAgentId);
            clock?.RequestState(GameState.Results);
        }
    }
}
