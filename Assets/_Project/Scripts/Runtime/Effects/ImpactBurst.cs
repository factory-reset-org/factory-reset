using System;
using UnityEngine;

namespace ToyFactory.Runtime.Effects
{
    /// <summary>
    /// A small burst of glowing sparks where a shot lands. The particle system on the same
    /// object decides what a burst looks like; this places it, tints it and reports when it is
    /// over so its owner can put it back in the pool.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class ImpactBurst : MonoBehaviour
    {
        [Tooltip("Seconds until the burst is over and the owner is told. Longer than the longest particle.")]
        [SerializeField, Min(0.1f)] float lifetime = 0.8f;

        [Tooltip("How far off the surface the burst starts, so it is not buried in it.")]
        [SerializeField, Min(0f)] float lift = 0.06f;

        ParticleSystem _system;
        float _endAt;

        /// <summary>Raised once, when the burst is over.</summary>
        public event Action<ImpactBurst> Finished;

        public bool IsPlaying { get; private set; }

        void Awake() => _system = GetComponent<ParticleSystem>();

        /// <summary>Starts a burst at <paramref name="point"/>, thrown out along the surface <paramref name="normal"/>.</summary>
        public void Play(Vector3 point, Vector3 normal, Color colour)
        {
            // A zero normal has no direction to throw sparks along.
            Quaternion facing = normal.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(normal) : Quaternion.identity;
            transform.SetPositionAndRotation(point + normal.normalized * lift, facing);

            ParticleSystem.MainModule main = _system.main;
            main.startColor = colour;
            _system.Clear(true);
            _system.Play(true);

            _endAt = Time.time + lifetime;
            IsPlaying = true;
        }

        void Update()
        {
            if (!IsPlaying || Time.time < _endAt)
                return;

            IsPlaying = false;
            Finished?.Invoke(this);
        }
    }
}
