using System;
using UnityEngine;

namespace ToyFactory.Runtime.Effects
{
    /// <summary>
    /// A blaster shot as the player sees it: a bright elongated core with a big soft glow round
    /// its head, flying from the muzzle to the point the shot already hit. It is only a picture:
    /// the hit was decided, and damage dealt, when the shot was fired.
    /// </summary>
    /// <remarks>
    /// The core is the object's own line renderer. The glow is a quad that always faces the
    /// camera, so it still shows as a round blob when the bolt flies straight away from the
    /// player, where a line would shrink to nothing. Raises <see cref="Arrived"/> when the head
    /// reaches the end (where the impact sparks belong, and where the glow goes out) and
    /// <see cref="Finished"/> when the tail has too, which is when its owner puts it back in
    /// the pool. The data the owner needs travels on the bolt itself, so a shot makes no
    /// garbage. Moves on game-scaled time, so it freezes with the pause.
    /// </remarks>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class ShotBolt : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [Tooltip("Metres per second. Fast enough to feel like a hit, slow enough to be seen.")]
        [SerializeField, Min(1f)] float speed = 90f;

        [Header("Core")]
        [Tooltip("Length of the bright core, in metres.")]
        [SerializeField, Min(0.1f)] float length = 0.8f;
        [SerializeField, Min(0.005f)] float width = 0.16f;
        [Tooltip("How far the core is pushed from the shot's colour towards white.")]
        [SerializeField, Range(0f, 1f)] float coreWhiteness = 0.75f;

        [Header("Glow")]
        [Tooltip("A quad with an additive glow material, turned to face the camera and kept on the bolt's head.")]
        [SerializeField] Transform glow;
        [SerializeField] Renderer glowRenderer;
        [Tooltip("Width of the glow, in metres.")]
        [SerializeField, Min(0.05f)] float glowSize = 0.9f;
        [Tooltip("Brightness of the glow, above 1 so bloom picks it up.")]
        [SerializeField, Min(0.1f)] float glowIntensity = 2f;

        LineRenderer _line;
        MaterialPropertyBlock _block;
        Camera _camera;
        Vector3 _from;
        Vector3 _direction;
        float _distance;
        float _travelled;
        bool _arrived;

        /// <summary>Raised once, when the head reaches the end of the shot.</summary>
        public event Action<ShotBolt> Arrived;

        /// <summary>Raised once, when nothing of the bolt is left to see.</summary>
        public event Action<ShotBolt> Finished;

        public bool IsFlying { get; private set; }

        /// <summary>True if the shot ended on something, so sparks belong at <see cref="ImpactPoint"/>.</summary>
        public bool HasImpact { get; private set; }

        public Vector3 ImpactPoint { get; private set; }

        public Vector3 ImpactNormal { get; private set; }

        public Color Colour { get; private set; }

        void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _block = new MaterialPropertyBlock();
        }

        /// <summary>Sends the bolt from <paramref name="from"/> to <paramref name="to"/>.</summary>
        /// <param name="hasImpact">The shot ended on something, with this surface normal.</param>
        public void Launch(Vector3 from, Vector3 to, Color colour, bool hasImpact, Vector3 impactNormal)
        {
            Vector3 delta = to - from;
            _distance = delta.magnitude;
            _direction = _distance > 1e-4f ? delta / _distance : Vector3.forward;
            _from = from;
            _travelled = 0f;
            _arrived = false;

            Colour = colour;
            HasImpact = hasImpact;
            ImpactPoint = to;
            ImpactNormal = impactNormal;

            // The core is nearly white, fading out towards its tail.
            Color core = Color.Lerp(colour, Color.white, coreWhiteness);
            core.a = 1f;
            Color tail = core;
            tail.a = 0f;
            _line.startColor = tail;
            _line.endColor = core;
            _line.widthMultiplier = width;

            if (glowRenderer != null)
            {
                Color tint = colour * glowIntensity;
                tint.a = 1f;
                glowRenderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColor, tint);
                glowRenderer.SetPropertyBlock(_block);
                glowRenderer.enabled = true;
            }
            if (glow != null)
                glow.localScale = Vector3.one * glowSize;
            _camera = Camera.main;

            IsFlying = true;
            Place();
        }

        /// <summary>
        /// Where the two ends of the segment are, as distances along the shot, after the head has
        /// travelled <paramref name="travelled"/> metres of a shot <paramref name="distance"/> long.
        /// Neither end goes past the target.
        /// </summary>
        public static void SegmentAt(float travelled, float distance, float length, out float tail, out float head)
        {
            head = Mathf.Min(travelled, distance);
            tail = Mathf.Clamp(travelled - length, 0f, distance);
        }

        void Update()
        {
            if (!IsFlying)
                return;

            _travelled += speed * Time.deltaTime;
            if (!_arrived && _travelled >= _distance)
            {
                _arrived = true;
                if (glowRenderer != null)
                    glowRenderer.enabled = false;   // the glow goes out on impact; the core runs on into it
                Arrived?.Invoke(this);
            }

            Place();

            if (_travelled >= _distance + length)
            {
                IsFlying = false;
                Finished?.Invoke(this);
            }
        }

        void Place()
        {
            SegmentAt(_travelled, _distance, length, out float tail, out float head);
            Vector3 headPosition = _from + _direction * head;
            _line.SetPosition(0, _from + _direction * tail);
            _line.SetPosition(1, headPosition);

            if (glow == null)
                return;
            glow.position = headPosition;
            if (_camera != null)
                glow.rotation = _camera.transform.rotation;
        }
    }
}
