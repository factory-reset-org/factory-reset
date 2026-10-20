using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Player;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// Base for something the player picks up by walking into it and that acts on the
    /// blaster's battery. It spins and bobs in place with a glow round it, and once taken it
    /// either goes for good or comes back after a while, as the prototype's batteries do.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public abstract class PlayerPickup : MonoBehaviour
    {
        [SerializeField] float spinDegreesPerSecond = 115f;

        [Tooltip("How far it bobs up and down, in metres.")]
        [SerializeField, Min(0f)] float bobHeight = 0.12f;

        [SerializeField, Min(0f)] float bobSpeed = 3f;

        [Tooltip("Seconds until it is back after being taken. 0 = never. -1 = this kind's usual time.")]
        [SerializeField] float respawnSeconds = -1f;

        int _playerLayer;
        PlayerBattery _battery;
        Collider[] _colliders;
        Renderer[] _renderers;
        PropGlow _glow;
        Vector3 _home;
        float _phase;
        bool _taken;
        float _backAt;

        /// <summary>The glow's colour.</summary>
        protected abstract Color GlowColour { get; }

        protected virtual float GlowSize => 1.5f;

        protected virtual float GlowOpacity => 0.5f;

        /// <summary>Seconds until a taken pickup is back, for this kind of pickup. 0 = never.</summary>
        protected virtual float UsualRespawnSeconds => 0f;

        /// <summary>The comic word shown when the player takes it. Empty: none.</summary>
        protected virtual string TakenWord => "";

        protected virtual Sfx TakenSound => Sfx.Pickup;

        /// <summary>True while it is waiting to come back.</summary>
        public bool IsTaken => _taken;

        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        float RespawnWait => respawnSeconds >= 0f ? respawnSeconds : UsualRespawnSeconds;

        void Awake()
        {
            _playerLayer = LayerMask.NameToLayer("Player");
            _colliders = GetComponentsInChildren<Collider>();
            _renderers = GetComponentsInChildren<Renderer>();
            _phase = Random.value * 6.28f;
        }

        void Start()
        {
            _home = transform.position;
            _glow = PropGlow.Attach(gameObject, GlowColour, GlowSize, GlowOpacity);
        }

        void Update()
        {
            if (_taken)
            {
                float wait = RespawnWait;
                if (wait > 0f && Now >= _backAt)
                    Restore();
                return;
            }

            transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
            transform.position = _home + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + _phase) * bobHeight);
        }

        // Stay, not Enter: a pickup the player could not take (already carrying the most)
        // is taken as soon as there is room, without them having to step off and on again.
        void OnTriggerStay(Collider other)
        {
            if (_taken || other.gameObject.layer != _playerLayer)
                return;
            if (_battery == null)
                _battery = other.GetComponentInParent<PlayerBattery>();
            if (_battery == null || !Collect(_battery))
                return;

            PropEffects.Spark(transform.position, GlowColour);
            if (!string.IsNullOrEmpty(TakenWord))
                PropEffects.Word(TakenWord, transform.position + Vector3.up * 0.9f);
            GameSfx.Play(TakenSound);
            Take();
        }

        /// <summary>Gives the pickup to the player. False if they cannot take it right now.</summary>
        protected abstract bool Collect(PlayerBattery battery);

        /// <summary>Removes it from the level: for good, or until it comes back.</summary>
        protected void Take()
        {
            if (_taken)
                return;

            _taken = true;
            OnTaken();

            float wait = RespawnWait;
            if (wait <= 0f)
            {
                gameObject.SetActive(false);
                return;
            }

            SetPresent(false);
            _backAt = Now + wait;
        }

        void Restore()
        {
            _taken = false;
            transform.position = _home;
            SetPresent(true);
            OnRestored();
        }

        // Out of the world but still running, so it can count its time back.
        void SetPresent(bool present)
        {
            foreach (Collider each in _colliders)
                if (each != null)
                    each.enabled = present;
            foreach (Renderer each in _renderers)
                if (each != null)
                    each.forceRenderingOff = !present;
            if (_glow != null)
                _glow.Intensity = present ? 1f : 0f;
        }

        protected virtual void OnTaken()
        {
        }

        protected virtual void OnRestored()
        {
        }
    }
}
