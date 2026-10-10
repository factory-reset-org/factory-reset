using UnityEngine;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A conveyor belt: a kinematic body that carries what stands on it. Rigidbodies are carried
    /// by the "position reset" trick (step the body back by one frame of belt travel, then move
    /// it forward again, so physics sees it moving at belt speed without it going anywhere).
    /// The player reads <see cref="Velocity"/> and is carried by the character controller.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ConveyorBelt : MonoBehaviour
    {
        [Tooltip("Direction the belt carries things, in the belt's own space.")]
        [SerializeField] Vector3 localDirection = Vector3.forward;

        [Tooltip("Metres per second.")]
        [SerializeField, Min(0f)] float speed = 1.5f;

        [Tooltip("Chapter 1's belts start off and the lever starts them.")]
        [SerializeField] bool startRunning;

        Rigidbody _rb;

        public bool IsRunning { get; private set; }

        /// <summary>The way the belt carries, in the belt's own space.</summary>
        public Vector3 LocalDirection => localDirection;

        /// <summary>The way the belt carries, in the world, running or not.</summary>
        public Vector3 Direction => transform.TransformDirection(localDirection).normalized;

        /// <summary>Metres per second when running.</summary>
        public float Speed => speed;

        /// <summary>The velocity of the belt's surface in the world: zero while it is off.</summary>
        public Vector3 Velocity => IsRunning ? Direction * speed : Vector3.zero;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            IsRunning = startRunning;
        }

        // Its chevrons, when the effects asset is there.
        void Start() => BeltSurface.Attach(this);

        public void SetRunning(bool running) => IsRunning = running;

        void FixedUpdate()
        {
            if (!IsRunning)
                return;

            Vector3 position = _rb.position;
            _rb.position = position - Velocity * Time.fixedDeltaTime;
            _rb.MovePosition(position);
        }
    }
}
