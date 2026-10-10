using System;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Player;
using ToyFactory.Runtime.Effects;

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

        [Tooltip("The id the Saboteur names this trap by. Must be different for every trap.")]
        [SerializeField, Min(0)] int sabotageId;

        [SerializeField, Min(0f)] float damage = 15f;
        [SerializeField, Min(0f)] float loudness = NoiseLoudness.BoxImpact;

        [Tooltip("Recoloured while armed. Left empty, the first renderer on this object or its children.")]
        [SerializeField] Renderer body;
        [SerializeField] Color armedColour = new Color(1f, 0.25f, 0.2f);

        static readonly Color SparkColour = new Color(0.38f, 0.85f, 1f);

        int _playerLayer;
        PropGlow _glow;
        float _nextSparkAt;

        public bool IsArmed { get; private set; }

        /// <summary>Raised when the player steps on the trap while it is armed.</summary>
        public event Action<Trap> OnSprung;

        void OnEnable() => SabotageTargets.Register(SabotageKind.Trap, sabotageId, this, transform);

        void OnDisable() => SabotageTargets.Unregister(SabotageKind.Trap, sabotageId, this);

        void Awake()
        {
            _playerLayer = LayerMask.NameToLayer("Player");
            if (body == null)
                body = GetComponentInChildren<Renderer>();
            SetArmed(startArmed);
        }

        void Start() => _glow = PropGlow.Attach(gameObject, SparkColour, 3.2f, 0.5f, 0.35f, 9f, new Vector3(0f, 0.5f, 0f));

        // An armed trap crackles: its glow flickers and it throws a spark now and then.
        void Update()
        {
            if (_glow != null)
                _glow.Intensity = IsArmed ? 0.6f + 0.4f * Random.value : 0f;

            if (!IsArmed || Time.time < _nextSparkAt)
                return;

            _nextSparkAt = Time.time + 0.18f;
            Vector3 at = transform.position + new Vector3(Random.Range(-0.4f, 0.4f), 0.1f, Random.Range(-0.4f, 0.4f));
            PropEffects.Spark(at, SparkColour);
        }

        /// <summary>Saboteur action: arm the trap.</summary>
        void ISabotageable.Execute()
        {
            if (!IsArmed)
            {
                PropEffects.Word("zap", transform.position + Vector3.up * 1.2f);
                GameSfx.Play(Sfx.Zap);
            }
            SetArmed(true);
        }

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
            PropEffects.Word("zap", transform.position + Vector3.up * 1.4f, 1.2f);
            PropEffects.Spark(transform.position + Vector3.up * 0.4f, SparkColour);
            GameSfx.Play(Sfx.Zap);

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
