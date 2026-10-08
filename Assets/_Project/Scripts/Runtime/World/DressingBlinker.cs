using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Blinks the status lights on a piece of level dressing (a Control Room server rack): every
    /// tick it flips one light on or off. When Factory OS shuts down
    /// (<see cref="CutsceneSignals.FactoryShutdown"/>) every light goes dark.
    /// </summary>
    /// <remarks>
    /// The lights are switched with <see cref="Renderer.enabled"/>, not by changing their
    /// material, so they keep one shared emissive material and stay SRP Batcher and static
    /// batching friendly. The order comes from a seeded generator, so each rack blinks its own
    /// pattern and a test can reproduce it.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DressingBlinker : MonoBehaviour
    {
        [Tooltip("The small emissive light meshes to blink.")]
        [SerializeField] Renderer[] lights = new Renderer[0];

        [Tooltip("Seconds between flips.")]
        [SerializeField, Min(0.02f)] float interval = 0.25f;

        [Tooltip("Share of the lights lit at the start.")]
        [SerializeField, Range(0f, 1f)] float startLit = 0.6f;

        [SerializeField] int seed = 47;

        System.Random _random;
        float _nextFlip;
        bool _shutDown;

        /// <summary>Lights currently lit.</summary>
        public int LitCount
        {
            get
            {
                int lit = 0;
                foreach (Renderer light in lights)
                    if (light != null && light.enabled)
                        lit++;
                return lit;
            }
        }

        /// <summary>Sets the lights and pattern, for the scene builder and tests.</summary>
        public void Configure(Renderer[] statusLights, float flipInterval, int patternSeed)
        {
            lights = statusLights ?? new Renderer[0];
            interval = Mathf.Max(0.02f, flipInterval);
            seed = patternSeed;
        }

        void OnEnable() => CutsceneEvents.OnCriticalSignal += OnCriticalSignal;

        void OnDisable() => CutsceneEvents.OnCriticalSignal -= OnCriticalSignal;

        void Start()
        {
            _random = new System.Random(seed);
            foreach (Renderer light in lights)
                if (light != null)
                    light.enabled = _random.NextDouble() < startLit;
            _nextFlip = Time.time + interval;
        }

        void OnCriticalSignal(string signalId)
        {
            if (signalId != CutsceneSignals.FactoryShutdown)
                return;
            _shutDown = true;
            foreach (Renderer light in lights)
                if (light != null)
                    light.enabled = false;
        }

        void Update()
        {
            if (_shutDown || lights.Length == 0 || Time.time < _nextFlip)
                return;
            _nextFlip = Time.time + interval;
            Renderer light = lights[_random.Next(lights.Length)];
            if (light != null)
                light.enabled = !light.enabled;
        }
    }
}
