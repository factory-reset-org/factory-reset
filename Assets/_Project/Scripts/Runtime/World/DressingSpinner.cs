using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Turns a piece of level dressing (a wall gear) around a local axis, so the factory looks
    /// like it is running. When Factory OS shuts down (<see cref="CutsceneSignals.FactoryShutdown"/>)
    /// it winds down to a stop, in step with the lights fading in <see cref="LightingState"/>.
    /// </summary>
    /// <remarks>Purely visual: it has no collider and the grid never sees it. Uses scaled time,
    /// so it also stops while the game is paused.</remarks>
    [DisallowMultipleComponent]
    public sealed class DressingSpinner : MonoBehaviour
    {
        [Tooltip("Local axis to turn around (a gear's axle is its local Z).")]
        [SerializeField] Vector3 axis = Vector3.forward;

        [Tooltip("Turning speed; negative turns the other way.")]
        [SerializeField] float degreesPerSecond = 30f;

        [Tooltip("Seconds to wind down to a stop after the shutdown signal.")]
        [SerializeField, Min(0f)] float windDownSeconds = 2.5f;

        float _speedScale = 1f;
        bool _shuttingDown;

        /// <summary>Current speed in degrees per second (0 once wound down).</summary>
        public float CurrentDegreesPerSecond => degreesPerSecond * _speedScale;

        /// <summary>Sets the speed, for the scene builder and tests.</summary>
        public void Configure(Vector3 localAxis, float speed)
        {
            axis = localAxis;
            degreesPerSecond = speed;
        }

        void OnEnable() => CutsceneEvents.OnCriticalSignal += OnCriticalSignal;

        void OnDisable() => CutsceneEvents.OnCriticalSignal -= OnCriticalSignal;

        void OnCriticalSignal(string signalId)
        {
            if (signalId == CutsceneSignals.FactoryShutdown)
                _shuttingDown = true;
        }

        void Update()
        {
            if (_shuttingDown && _speedScale > 0f)
                _speedScale = windDownSeconds > 0f
                    ? Mathf.Max(0f, _speedScale - Time.deltaTime / windDownSeconds)
                    : 0f;
            if (_speedScale > 0f)
                transform.Rotate(axis, CurrentDegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
