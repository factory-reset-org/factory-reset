using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// One target of a <see cref="TargetGroup"/>. It spins until it is shot, with a pink glow
    /// while its chapter is on, then stops, changes colour and gives a DING until the group
    /// either completes or resets it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SpinningTarget : MonoBehaviour, IDamageable
    {
        static readonly Color GlowColour = new Color(1f, 0.36f, 0.66f);

        [Tooltip("Local axis the target spins round.")]
        [SerializeField] Vector3 spinAxis = Vector3.forward;
        [SerializeField] float spinDegreesPerSecond = 70f;

        [Tooltip("Recoloured when hit. Left empty, the first renderer on this object or its children.")]
        [SerializeField] Renderer body;
        [SerializeField] Color hitColour = new Color(0.24f, 0.86f, 0.69f);

        TargetGroup _group;
        PropGlow _glow;
        float _speedFactor = 1f;

        public bool IsHit { get; private set; }

        void Awake()
        {
            if (body == null)
                body = GetComponentInChildren<Renderer>();
        }

        void Start() => _glow = PropGlow.Attach(gameObject, GlowColour, 2.2f, 0.4f, 0.5f, 5f);

        /// <param name="index">0-based position in the group. Later targets spin faster, as in the prototype.</param>
        internal void Bind(TargetGroup group, int index)
        {
            _group = group;
            _speedFactor = 1f + 0.29f * index;
        }

        [ContextMenu("Take Hit")]
        public void TakeHit()
        {
            if (IsHit || _group == null || !_group.RegisterHit())
                return;

            IsHit = true;
            PropTint.Set(body, hitColour);
            Vector3 at = transform.position;
            PropEffects.Spark(at, hitColour);
            PropEffects.Word("ding", at + Vector3.up * 0.7f);
            GameSfx.Play(Sfx.Ding);
        }

        internal void ResetTarget()
        {
            IsHit = false;
            PropTint.Clear(body);
        }

        void Update()
        {
            // Quiet and slow until its chapter, lively and glowing while the targets are the task.
            bool live = _group == null || _group.IsLive;
            if (_glow != null)
                _glow.Intensity = live && !IsHit ? 1f : 0f;

            if (!IsHit)
                transform.Rotate(spinAxis, spinDegreesPerSecond * _speedFactor * (live ? 1f : 0.25f) * Time.deltaTime,
                    Space.Self);
        }
    }
}
