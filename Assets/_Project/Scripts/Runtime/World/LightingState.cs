using System.Collections.Generic;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.World
{
    /// <summary>The level's lighting moods, in story order. They only ever move forward.</summary>
    public enum LightingMode
    {
        /// <summary>Chapters 1-3: the Control Room doors are sealed and their alarms blink red.</summary>
        Alarm,
        /// <summary>From the Chapter 3 cutscene: the doors are open and the alarms glow steady amber.</summary>
        Unlocked,
        /// <summary>The ending: real-time lights fade to a warm glow and emissives dim.</summary>
        Shutdown
    }

    /// <summary>
    /// Drives the level's story lighting from the cutscene signals: red alarm beacons on the
    /// sealed Control Room doors, amber once <see cref="CutsceneSignals.ControlRoomUnlock"/>
    /// fires, and the factory powering down on <see cref="CutsceneSignals.FactoryShutdown"/>.
    /// </summary>
    /// <remarks>
    /// It listens to <see cref="CutsceneEvents.OnCriticalSignal"/>, never to a Timeline, so a
    /// skipped cutscene still leaves the lights right (the director fires every missed signal
    /// on skip). Beacons change by swapping shared materials, which keeps them SRP Batcher
    /// compatible; only the shutdown fade makes per-renderer material copies, once, at the
    /// very end of the game.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LightingState : MonoBehaviour
    {
        [Header("Control Room alarm")]
        [Tooltip("Real-time point lights at the sealed doors.")]
        [SerializeField] Light[] alarmLights = new Light[0];
        [Tooltip("Emissive beacon meshes on the door lintels.")]
        [SerializeField] Renderer[] alarmBeacons = new Renderer[0];
        [SerializeField] Material alarmOnMaterial;
        [Tooltip("The dim half of the blink.")]
        [SerializeField] Material alarmOffMaterial;
        [SerializeField] Material unlockedMaterial;
        [SerializeField] Color alarmColour = new Color(1f, 0.19f, 0.25f);
        [SerializeField] Color unlockedColour = new Color(1f, 0.69f, 0f);
        [SerializeField, Min(0f)] float alarmIntensity = 10f;
        [SerializeField, Min(0f)] float alarmOffIntensity = 1.2f;
        [SerializeField, Min(0f)] float unlockedIntensity = 4f;
        [Tooltip("Blinks per second while sealed.")]
        [SerializeField, Min(0.01f)] float blinkRate = 0.95f;

        [Header("Shutdown")]
        [Tooltip("Real-time lights that fade to the warm shutdown glow.")]
        [SerializeField] Light[] shutdownLights = new Light[0];
        [Tooltip("Emissive renderers (screens, strips, beacons) that dim.")]
        [SerializeField] Renderer[] shutdownEmissives = new Renderer[0];
        [SerializeField] Color shutdownColour = new Color(1f, 0.7f, 0.42f);
        [Tooltip("Light intensity at the end of the fade, as a fraction of where it started.")]
        [SerializeField, Range(0f, 1f)] float shutdownIntensity = 0.35f;
        [Tooltip("Emission at the end of the fade, as a fraction of where it started.")]
        [SerializeField, Range(0f, 1f)] float shutdownEmission = 0.15f;
        [SerializeField, Min(0f)] float shutdownSeconds = 3f;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        struct LightStart
        {
            public Light Light;
            public Color Colour;
            public float Intensity;
        }

        struct EmissionStart
        {
            public Material Material;
            public Color Emission;
        }

        readonly List<LightStart> _lightStarts = new List<LightStart>();
        readonly List<EmissionStart> _emissionStarts = new List<EmissionStart>();
        readonly List<Material> _instances = new List<Material>();
        float _shutdownStartTime;
        bool _blinkOn;
        bool _applied;

        /// <summary>The current mood.</summary>
        public LightingMode Mode { get; private set; } = LightingMode.Alarm;

        /// <summary>0 to 1 through the shutdown fade; 0 before it starts.</summary>
        public float ShutdownProgress { get; private set; }

        /// <summary>Whether the alarm is in the bright half of its blink at <paramref name="time"/>.</summary>
        public static bool IsBlinkOn(float time, float blinksPerSecond) => Mathf.Repeat(time * blinksPerSecond, 1f) < 0.5f;

        void OnEnable() => CutsceneEvents.OnCriticalSignal += HandleSignal;

        void OnDisable() => CutsceneEvents.OnCriticalSignal -= HandleSignal;

        void OnDestroy()
        {
            foreach (Material instance in _instances)
                Destroy(instance);
            _instances.Clear();
        }

        void HandleSignal(string signalId)
        {
            if (signalId == CutsceneSignals.ControlRoomUnlock)
                SetMode(LightingMode.Unlocked);
            else if (signalId == CutsceneSignals.FactoryShutdown)
                SetMode(LightingMode.Shutdown);
        }

        /// <summary>
        /// Moves to <paramref name="mode"/>. Ignored if it is not later than the current mode:
        /// the story never relocks the doors or restarts the factory.
        /// </summary>
        public void SetMode(LightingMode mode)
        {
            if (mode <= Mode)
                return;
            Mode = mode;
            _applied = false;
            if (mode == LightingMode.Shutdown)
                BeginShutdown();
            Apply(Time.time);
        }

        void Update() => Apply(Time.time);

        void Apply(float time)
        {
            switch (Mode)
            {
                case LightingMode.Alarm:
                    bool on = IsBlinkOn(time, blinkRate);
                    if (_applied && on == _blinkOn)
                        return;
                    _blinkOn = on;
                    SetAlarm(alarmColour, on ? alarmIntensity : alarmOffIntensity, on ? alarmOnMaterial : alarmOffMaterial);
                    break;

                case LightingMode.Unlocked:
                    if (_applied)
                        return;
                    SetAlarm(unlockedColour, unlockedIntensity, unlockedMaterial);
                    break;

                case LightingMode.Shutdown:
                    if (_applied)
                        return;
                    float t = shutdownSeconds > 0f ? Mathf.Clamp01((time - _shutdownStartTime) / shutdownSeconds) : 1f;
                    ShutdownProgress = t;
                    float eased = t * t * (3f - 2f * t);
                    foreach (LightStart start in _lightStarts)
                    {
                        if (start.Light == null)
                            continue;
                        start.Light.color = Color.Lerp(start.Colour, shutdownColour, eased);
                        start.Light.intensity = Mathf.Lerp(start.Intensity, start.Intensity * shutdownIntensity, eased);
                    }
                    foreach (EmissionStart start in _emissionStarts)
                        start.Material.SetColor(EmissionColorId, Color.Lerp(start.Emission, start.Emission * shutdownEmission, eased));
                    if (t < 1f)
                        return;   // keep fading next frame
                    break;
            }
            _applied = true;
        }

        void SetAlarm(Color colour, float intensity, Material beaconMaterial)
        {
            foreach (Light alarm in alarmLights)
            {
                if (alarm == null)
                    continue;
                alarm.color = colour;
                alarm.intensity = intensity;
            }
            if (beaconMaterial == null)
                return;
            foreach (Renderer beacon in alarmBeacons)
                if (beacon != null)
                    beacon.sharedMaterial = beaconMaterial;
        }

        // Records where each light and emissive starts, so the fade works from whatever state
        // the level is in (amber after the unlock, or still red if the unlock was never seen).
        void BeginShutdown()
        {
            _shutdownStartTime = Time.time;
            ShutdownProgress = 0f;

            if (alarmBeacons.Length > 0 && unlockedMaterial != null)
                SetAlarm(unlockedColour, unlockedIntensity, unlockedMaterial);   // no red blink in the ending

            _lightStarts.Clear();
            foreach (Light light in shutdownLights)
                if (light != null)
                    _lightStarts.Add(new LightStart { Light = light, Colour = light.color, Intensity = light.intensity });

            _emissionStarts.Clear();
            foreach (Renderer renderer in shutdownEmissives)
            {
                if (renderer == null)
                    continue;
                foreach (Material material in renderer.materials)   // per-renderer copies, made once
                {
                    _instances.Add(material);
                    if (material.HasProperty(EmissionColorId))
                        _emissionStarts.Add(new EmissionStart { Material = material, Emission = material.GetColor(EmissionColorId) });
                }
            }
        }
    }
}
