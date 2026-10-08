using System;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Player;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A floor trap. It is harmless until a Saboteur arms it through
    /// <see cref="ISabotageable.Execute"/>. An armed trap hurts the player who steps on it,
    /// makes a noise the agents hear and is then spent; the player can also clear an armed
    /// trap safely by using it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Trap : MonoBehaviour, ISabotageable, IInteractable
    {
        [Tooltip("Armed from the start, without a Saboteur.")]
        [SerializeField] bool startArmed;

        [SerializeField, Min(0f)] float damage = 15f;
        [SerializeField, Min(0f)] float loudness = NoiseLoudness.BoxImpact;

        [Tooltip("Recoloured while armed. Left empty, the first renderer on this object or its children.")]
        [SerializeField] Renderer body;
        [SerializeField] Color armedColour = new Color(1f, 0.25f, 0.2f);

        int _playerLayer;

        public bool IsArmed { get; private set; }

        /// <summary>Raised when the player steps on the trap while it is armed.</summary>
        public event Action<Trap> OnSprung;

        void Awake()
        {
            _playerLayer = LayerMask.NameToLayer("Player");
            if (body == null)
                body = GetComponentInChildren<Renderer>();
            SetArmed(startArmed);
        }

        /// <summary>Saboteur action: arm the trap.</summary>
        void ISabotageable.Execute() => SetArmed(true);

        /// <summary>Player use: clear the trap.</summary>
        public void Interact() => SetArmed(false);

        void OnTriggerEnter(Collider other)
        {
            if (!IsArmed || other.gameObject.layer != _playerLayer)
                return;

            var player = other.GetComponentInParent<IPlayerState>();
            if (player == null)
                return;

            SetArmed(false);
            player.TakeDamage(damage, PlayerHealth.NoSource);

            float time = GameClock.Current != null ? GameClock.Current.GameTime : Time.time;
            NoiseEvents.Emit(new NoiseEvent(transform.position, loudness, GetHashCode(), time));
            OnSprung?.Invoke(this);
        }

        void SetArmed(bool armed)
        {
            IsArmed = armed;
            if (armed)
                PropTint.Set(body, armedColour);
            else
                PropTint.Clear(body);
        }
    }
}
