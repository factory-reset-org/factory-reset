using UnityEngine;

namespace ToyFactory.Runtime.Effects
{
    /// <summary>
    /// A soft glow behind a prop: a camera-facing additive quad that pulses gently, the
    /// prototype's halo on pickups, targets, the terminal, relays, cores, the charger and traps.
    /// A prop script sets how bright it is (<see cref="Intensity"/>) and what colour, for
    /// instance the terminal's glow growing as it is hacked.
    /// </summary>
    /// <remarks>
    /// The quad is not a child of the prop, so the prop's scale and rotation cannot squash or
    /// tilt it. It follows the prop's position, hides with it and goes with it.
    /// </remarks>
    public sealed class PropGlow : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] Color colour = Color.white;

        [Tooltip("Width of the glow, in metres.")]
        [SerializeField, Min(0.05f)] float size = 1.5f;

        [Tooltip("How strong the glow is at full intensity, 0 to 1.")]
        [SerializeField, Range(0f, 1f)] float opacity = 0.6f;

        [Tooltip("How much it breathes, as a share of its strength.")]
        [SerializeField, Range(0f, 1f)] float pulseAmount = 0.25f;

        [SerializeField, Min(0f)] float pulseSpeed = 3f;

        [Tooltip("Where the glow sits, in the prop's own space.")]
        [SerializeField] Vector3 offset;

        [Tooltip("Brightness above plain white, so bloom picks the glow up.")]
        [SerializeField, Min(0.1f)] float brightness = 2f;

        [SerializeField] Material material;

        Transform _quad;
        MeshRenderer _renderer;
        MaterialPropertyBlock _block;
        float _intensity = 1f;
        float _phase;

        /// <summary>The glow's colour. Setting it takes effect on the next frame.</summary>
        public Color Colour
        {
            get => colour;
            set => colour = value;
        }

        /// <summary>0 hides the glow, 1 is its full strength.</summary>
        public float Intensity
        {
            get => _intensity;
            set => _intensity = Mathf.Clamp01(value);
        }

        /// <summary>The glow's quad, for tests. Null until the glow has been built.</summary>
        public Transform Quad => _quad;

        /// <summary>
        /// Puts a glow on <paramref name="target"/>, using the shared glow material. Returns the
        /// existing glow if there already is one, and null if no glow material is available.
        /// </summary>
        public static PropGlow Attach(GameObject target, Color colour, float size, float opacity,
            float pulseAmount = 0.25f, float pulseSpeed = 3f, Vector3 offset = default)
            => Attach(target, PropEffects.GlowMaterial, colour, size, opacity, pulseAmount, pulseSpeed, offset);

        /// <summary>As <see cref="Attach(GameObject, Color, float, float, float, float, Vector3)"/> with a material given.</summary>
        public static PropGlow Attach(GameObject target, Material material, Color colour, float size, float opacity,
            float pulseAmount = 0.25f, float pulseSpeed = 3f, Vector3 offset = default)
        {
            if (target == null || material == null)
                return null;

            if (!target.TryGetComponent(out PropGlow glow))
                glow = target.AddComponent<PropGlow>();
            glow.material = material;
            glow.colour = colour;
            glow.size = size;
            glow.opacity = opacity;
            glow.pulseAmount = pulseAmount;
            glow.pulseSpeed = pulseSpeed;
            glow.offset = offset;
            glow.Build();
            return glow;
        }

        void Awake()
        {
            _phase = Random.value * 6.28f;   // so a row of glows does not breathe in step
            Build();
        }

        void OnEnable()
        {
            if (_quad != null)
                _quad.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            if (_quad != null)
                _quad.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_quad != null)
                Destroy(_quad.gameObject);
        }

        void Build()
        {
            if (_quad != null || material == null)
                return;

            var quad = new GameObject(name + "_Glow");
            quad.layer = gameObject.layer;
            _quad = quad.transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(quad, gameObject.scene);
            quad.AddComponent<MeshFilter>().sharedMesh = GlowQuad.Mesh;
            _renderer = quad.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _block = new MaterialPropertyBlock();
            quad.SetActive(isActiveAndEnabled);
        }

        void LateUpdate()
        {
            if (_quad == null)
            {
                Build();
                if (_quad == null)
                    return;
            }

            float strength = opacity * _intensity;
            _renderer.enabled = strength > 0.001f;
            if (!_renderer.enabled)
                return;

            float pulse = 1f + pulseAmount * Mathf.Sin(Time.time * pulseSpeed + _phase);
            Color shown = colour * (brightness * strength * pulse);
            shown.a = 1f;
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, shown);
            _renderer.SetPropertyBlock(_block);

            _quad.position = transform.TransformPoint(offset);
            _quad.localScale = new Vector3(size, size, 1f);
            Camera camera = Camera.main;
            if (camera != null)
                _quad.rotation = camera.transform.rotation;
        }
    }
}
