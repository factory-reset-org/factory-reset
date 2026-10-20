using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// Test-scene noise maker: emits a <see cref="NoiseEvent"/> of a set loudness on a timer, in
    /// bursts, so the Tracker's hearing can be shown without the real props. For example the
    /// colour terminal (60 every 0.8 s while held) is a burst of 8 at 0.8 s, then a pause.
    /// Not for the real level: there the props make their own noises.
    /// </summary>
    public sealed class ScriptedNoiseSource : MonoBehaviour
    {
        [Tooltip("Source level L0 (see NoiseLoudness): 60 terminal beep, 90 relay alarm.")]
        [SerializeField, Min(0f)] float loudness = NoiseLoudness.TerminalBeep;

        [Tooltip("Seconds between noises inside a burst.")]
        [SerializeField, Min(0.05f)] float interval = 0.8f;

        [Tooltip("Noises per burst; 0 = never stop.")]
        [SerializeField, Min(0)] int noisesPerBurst = 8;

        [Tooltip("Silence between bursts (seconds).")]
        [SerializeField, Min(0f)] float pauseBetweenBursts = 10f;

        [Tooltip("Silence before the first noise (seconds), so the agents have spawned and settled.")]
        [SerializeField, Min(0f)] float startDelay = 3f;

        [Tooltip("Optional mesh that pulses on every noise.")]
        [SerializeField] Transform pulse;

        float _nextAt;
        int _inBurst;
        float _pulseUntil;
        Vector3 _pulseScale;

        /// <summary>Noises emitted so far.</summary>
        public int Emitted { get; private set; }

        /// <summary>The source id its noises carry (stable for the object's lifetime).</summary>
        public int SourceId => GetInstanceHash();

        void Start()
        {
            _nextAt = Time.time + startDelay;
            if (pulse != null)
                _pulseScale = pulse.localScale;
        }

        void Update()
        {
            if (pulse != null)
                pulse.localScale = Time.time < _pulseUntil ? _pulseScale * 1.35f : _pulseScale;

            if (Time.time < _nextAt)
                return;

            Emit();
            _inBurst++;
            bool burstOver = noisesPerBurst > 0 && _inBurst >= noisesPerBurst;
            if (burstOver)
                _inBurst = 0;
            _nextAt = Time.time + (burstOver ? pauseBetweenBursts : interval);
        }

        /// <summary>Emits one noise now (also used by tests).</summary>
        public void Emit()
        {
            float time = GameClock.Current != null ? GameClock.Current.GameTime : Time.time;
            NoiseEvents.Emit(new NoiseEvent(transform.position, loudness, SourceId, time));
            Emitted++;
            _pulseUntil = Time.time + 0.15f;
        }

        // Like the props, the object's hash keeps each source distinct: small ids belong to
        // agents and -1 to the player.
        int GetInstanceHash() => GetHashCode();
    }
}
