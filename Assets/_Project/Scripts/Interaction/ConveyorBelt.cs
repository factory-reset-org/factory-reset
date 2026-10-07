using UnityEngine;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A belt that carries whatever rests on it. The belt itself never travels: each physics
    /// step its kinematic body is stepped back and then moved to where it was, so friction
    /// drags the objects on top along while the belt stays put.
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

        /// <summary>World velocity the belt gives to what stands on it; zero while stopped.</summary>
        public Vector3 Velocity =>
            IsRunning ? transform.TransformDirection(localDirection).normalized * speed : Vector3.zero;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            IsRunning = startRunning;
        }

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
