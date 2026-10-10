using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Carries hanging toys round a closed overhead loop (the Assembly Floor's toy rail), so the
    /// factory looks like it is running. The carriers are spaced evenly round the loop, slide
    /// along it at a steady speed and turn to face where they are going. When Factory OS shuts
    /// down (<see cref="CutsceneSignals.FactoryShutdown"/>) the rail winds down to a stop, in step
    /// with the gears and the lights.
    /// </summary>
    /// <remarks>Purely visual: the carriers have no colliders and the grid never sees them.
    /// Points are in this object's local space. Uses scaled time, so it stops while paused.</remarks>
    [DisallowMultipleComponent]
    public sealed class DressingRail : MonoBehaviour
    {
        [Tooltip("Corners of the closed loop, in local space; the last joins back to the first.")]
        [SerializeField] Vector3[] points = new Vector3[0];

        [Tooltip("The hooks that ride the rail, spaced evenly round it.")]
        [SerializeField] Transform[] carriers = new Transform[0];

        [Tooltip("Speed along the rail, metres per second.")]
        [SerializeField] float metresPerSecond = 0.5f;

        [Tooltip("Seconds to wind down to a stop after the shutdown signal.")]
        [SerializeField, Min(0f)] float windDownSeconds = 2.5f;

        float _travelled;
        float _speedScale = 1f;
        bool _shuttingDown;

        /// <summary>Current speed in metres per second (0 once wound down).</summary>
        public float CurrentMetresPerSecond => metresPerSecond * _speedScale;

        /// <summary>Length of the closed loop, in metres.</summary>
        public float Length => LoopLength(points);

        /// <summary>Sets the loop, the carriers and the speed, for the scene builder and tests.</summary>
        public void Configure(Vector3[] localPoints, Transform[] movingCarriers, float speed)
        {
            points = localPoints ?? new Vector3[0];
            carriers = movingCarriers ?? new Transform[0];
            metresPerSecond = speed;
            Place();
        }

        /// <summary>Length of the closed polyline through <paramref name="loop"/>.</summary>
        public static float LoopLength(Vector3[] loop)
        {
            if (loop == null || loop.Length < 2)
                return 0f;
            float length = 0f;
            for (int i = 0; i < loop.Length; i++)
                length += Vector3.Distance(loop[i], loop[(i + 1) % loop.Length]);
            return length;
        }

        /// <summary>
        /// The point <paramref name="distance"/> metres round the closed loop from its first corner
        /// (wrapping, so any distance works), and the direction of travel there.
        /// </summary>
        public static Vector3 PointAt(Vector3[] loop, float distance, out Vector3 direction)
        {
            direction = Vector3.forward;
            float length = LoopLength(loop);
            if (length <= 0f)
                return loop != null && loop.Length > 0 ? loop[0] : Vector3.zero;

            float d = Mathf.Repeat(distance, length);
            for (int i = 0; i < loop.Length; i++)
            {
                Vector3 a = loop[i], b = loop[(i + 1) % loop.Length];
                float segment = Vector3.Distance(a, b);
                if (segment <= 0f)
                    continue;
                if (d <= segment)
                {
                    direction = (b - a) / segment;
                    return a + direction * d;
                }
                d -= segment;
            }
            return loop[0];
        }

        void OnEnable() => CutsceneEvents.OnCriticalSignal += OnCriticalSignal;

        void OnDisable() => CutsceneEvents.OnCriticalSignal -= OnCriticalSignal;

        void Start() => Place();

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
            if (_speedScale <= 0f)
                return;
            _travelled += CurrentMetresPerSecond * Time.deltaTime;
            Place();
        }

        // Puts each carrier at its share of the loop, facing along it.
        void Place()
        {
            float length = Length;
            if (length <= 0f || carriers.Length == 0)
                return;
            float spacing = length / carriers.Length;
            for (int i = 0; i < carriers.Length; i++)
            {
                if (carriers[i] == null)
                    continue;
                Vector3 position = PointAt(points, _travelled + i * spacing, out Vector3 direction);
                carriers[i].localPosition = position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 1e-6f)
                    carriers[i].localRotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }
    }
}
