using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// One target of a <see cref="TargetGroup"/>. It spins until it is shot, then stops and
    /// changes colour until the group either completes or resets it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SpinningTarget : MonoBehaviour, IDamageable
    {
        [Tooltip("Local axis the target spins round.")]
        [SerializeField] Vector3 spinAxis = Vector3.forward;
        [SerializeField] float spinDegreesPerSecond = 120f;

        [Tooltip("Recoloured when hit. Left empty, the first renderer on this object or its children.")]
        [SerializeField] Renderer body;
        [SerializeField] Color hitColour = new Color(0.2f, 0.9f, 0.4f);

        TargetGroup _group;

        public bool IsHit { get; private set; }

        void Awake()
        {
            if (body == null)
                body = GetComponentInChildren<Renderer>();
        }

        internal void Bind(TargetGroup group) => _group = group;

        [ContextMenu("Take Hit")]
        public void TakeHit()
        {
            if (IsHit || _group == null || !_group.RegisterHit())
                return;

            IsHit = true;
            PropTint.Set(body, hitColour);
        }

        internal void ResetTarget()
        {
            IsHit = false;
            PropTint.Clear(body);
        }

        void Update()
        {
            if (!IsHit)
                transform.Rotate(spinAxis, spinDegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
