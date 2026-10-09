using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The agent's lights: its eyes, visor, goggles or antenna bulb glow while it is running,
    /// sputter out when it is knocked out or scrapped, and flicker back on when it reboots.
    /// The clearest sign of "this one is down, and now it is back".
    /// </summary>
    /// <remarks>
    /// <para>The light parts get one emissive material per agent, made from
    /// <see cref="lightMaterial"/> at start-up, so the agents dim one at a time and every part
    /// of an agent shares one material (still SRP Batcher compatible, unlike a property
    /// block). S3's model prefab is not changed: the parts are listed on the body.</para>
    /// <para>Going out and coming back on follow short brightness patterns, a sputter and a
    /// flicker, rather than a plain fade, so they read as a machine powering down and up.</para>
    /// <para>The Captain's visor stays dark while it is dormant and flickers on when the wake
    /// signal sets <c>CaptainAwake</c>, which happens during the Chapter 3 cutscene itself.</para>
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class AgentLights : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // Brightness over time, 0 to 1: going out (a sputter) and coming back on (a flicker).
        static readonly float[] OutPattern = { 1f, 0.15f, 0.7f, 0.1f, 0f };
        static readonly float[] OnPattern = { 0f, 0.6f, 0f, 0.3f, 1f, 0.5f, 1f };

        [Tooltip("The glowing parts: eyes, visor, goggles, antenna bulb.")]
        [SerializeField] Renderer[] lights = new Renderer[0];

        [Tooltip("Emissive material the parts are drawn with; copied once per agent.")]
        [SerializeField] Material lightMaterial;

        [SerializeField] Color colour = new Color(0.3f, 0.85f, 0.4f);

        [Tooltip("Emission strength when fully on.")]
        [SerializeField, Min(0f)] float intensity = 2.5f;

        [Tooltip("Seconds the sputter out takes.")]
        [SerializeField, Min(0.01f)] float outSeconds = 0.35f;

        [Tooltip("Seconds the flicker back on takes.")]
        [SerializeField, Min(0.01f)] float onSeconds = 0.6f;

        [Tooltip("The Captain: dark until the blackboard says it is awake.")]
        [SerializeField] bool darkUntilCaptainWakes;

        AgentController _agent;
        Material _material;
        float[] _pattern;
        float _patternTime;
        bool _down;
        bool _started;

        /// <summary>Brightness now, 0 (off) to 1 (on).</summary>
        public float Level { get; private set; } = 1f;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            if (lightMaterial == null || lights.Length == 0)
                return;
            _material = new Material(lightMaterial) { name = lightMaterial.name + " (" + name + ")" };
            foreach (Renderer part in lights)
                if (part != null)
                    part.sharedMaterial = _material;
            Apply(1f);
        }

        void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }

        // Off while knocked out or scrapped, and (the Captain) while still dormant. With no
        // spawner (a test scene) there is no blackboard, so the Captain counts as awake.
        bool Down()
        {
            if (_agent.IsDisabled || _agent.IsDead)
                return true;
            if (!darkUntilCaptainWakes)
                return false;
            AgentSpawner spawner = AgentSpawner.Instance;
            return spawner != null && spawner.Blackboard != null && !spawner.Blackboard.CaptainAwake;
        }

        void LateUpdate()
        {
            bool down = Down();
            if (!_started)
            {
                // The first frame takes the state as it is, without a sputter or a flicker.
                _started = true;
                _down = down;
                Apply(down ? 0f : 1f);
                return;
            }
            if (down != _down)
            {
                _down = down;
                _pattern = down ? OutPattern : OnPattern;
                _patternTime = 0f;
            }
            if (_pattern == null)
                return;

            _patternTime += Time.deltaTime;
            float t = Mathf.Clamp01(_patternTime / (_down ? outSeconds : onSeconds));
            Apply(Sample(_pattern, t));
            if (t >= 1f)
                _pattern = null;
        }

        // Steps through the pattern: each value holds for an equal share of the time.
        static float Sample(float[] pattern, float t)
        {
            int i = Mathf.Min(pattern.Length - 1, Mathf.FloorToInt(t * pattern.Length));
            return pattern[i];
        }

        void Apply(float level)
        {
            Level = level;
            if (_material == null)
                return;
            // The part keeps a dark tint of its colour when off, so it reads as an unlit lamp.
            Color tint = Color.Lerp(colour * 0.25f, colour, level);
            tint.a = 1f;
            Color glow = colour * (intensity * level);
            glow.a = 1f;
            _material.SetColor(BaseColorId, tint);
            _material.SetColor(EmissionColorId, glow);
        }
    }
}
