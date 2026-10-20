using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Pumps a stamping press piston up and down between two local heights, as in the
    /// prototype's Assembly Floor. At <see cref="CutsceneSignals.FactoryShutdown"/> it
    /// settles at the top and stops. Dressing only: the press's collider does not move.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DressingPiston : MonoBehaviour
    {
        [Tooltip("Local height at the bottom of the stroke (the stamp).")]
        [SerializeField] float lowY = 1.35f;
        [Tooltip("Local height at the top of the stroke.")]
        [SerializeField] float highY = 2.3f;
        [Tooltip("Full strokes per second.")]
        [SerializeField, Min(0f)] float strokesPerSecond = 0.5f;
        [Tooltip("Offset into the stroke (0 to 1), so neighbouring presses do not pump in step.")]
        [SerializeField, Range(0f, 1f)] float phase;
        [Tooltip("Seconds to rise and stop after the shutdown.")]
        [SerializeField, Min(0.01f)] float settleSeconds = 1.5f;

        bool _shuttingDown;
        float _time;

        /// <summary>True once the piston has settled after the shutdown.</summary>
        public bool Stopped { get; private set; }

        /// <summary>Sets up the stroke (used when the press is built and by tests).</summary>
        public void Configure(float low, float high, float strokes, float startPhase)
        {
            lowY = low;
            highY = high;
            strokesPerSecond = Mathf.Max(0f, strokes);
            phase = Mathf.Repeat(startPhase, 1f);
        }

        /// <summary>Piston height at <paramref name="t"/> seconds: a fast stamp down, a slower rise.</summary>
        public static float HeightAt(float t, float low, float high, float strokes, float startPhase)
        {
            float u = Mathf.Repeat(t * strokes + startPhase, 1f);
            // |sin| gives the prototype's bounce: down quickly, a beat at the bottom, back up.
            float lift = Mathf.Abs(Mathf.Sin(u * Mathf.PI));
            return Mathf.Lerp(low, high, lift);
        }

        void OnEnable() => CutsceneEvents.OnCriticalSignal += HandleSignal;

        void OnDisable() => CutsceneEvents.OnCriticalSignal -= HandleSignal;

        void HandleSignal(string signalId)
        {
            if (signalId == CutsceneSignals.FactoryShutdown)
                _shuttingDown = true;
        }

        void Update()
        {
            if (Stopped)
                return;

            Vector3 local = transform.localPosition;
            if (_shuttingDown)
            {
                local.y = Mathf.MoveTowards(local.y, highY, Mathf.Abs(highY - lowY) / settleSeconds * Time.deltaTime);
                Stopped = Mathf.Approximately(local.y, highY);
            }
            else
            {
                _time += Time.deltaTime;
                local.y = HeightAt(_time, lowY, highY, strokesPerSecond, phase);
            }
            transform.localPosition = local;
        }
    }
}
