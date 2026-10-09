using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A thrown wind-up toy. Once it lands it walks for a while, turning away from walls,
    /// and ticks loudly at a steady rate under one source id. To the Tracker that is a
    /// repeating source, which pulls it into its Distracted state, circling the toy.
    /// When its spring runs out it stands still for a moment, then goes away.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WindUpToy : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float walkSeconds = 8f;
        [SerializeField, Min(0f)] float walkSpeed = 0.8f;

        [Tooltip("Seconds between ticks. Must stay under the Tracker's 1.5 s repeat window.")]
        [SerializeField, Min(0.1f)] float tickInterval = 0.6f;
        [SerializeField, Min(0f)] float tickLoudness = NoiseLoudness.ToyLanding;

        [Tooltip("Seconds it stays after its spring runs out, before it goes away.")]
        [SerializeField, Min(0f)] float lingerSeconds = 4f;

        [Header("Turning")]
        [Tooltip("Layers it turns away from.")]
        [SerializeField] LayerMask obstacleMask;
        [SerializeField, Min(0.05f)] float lookAhead = 0.35f;
        [SerializeField, Min(0.05f)] float turnCheckInterval = 0.2f;

        [Tooltip("The wind-up key, spun while walking. Optional.")]
        [SerializeField] Transform key;
        [SerializeField] float keyDegreesPerSecond = 360f;

        enum Phase
        {
            Flying,
            Walking,
            RunDown
        }

        Rigidbody _rb;
        Phase _phase;
        float _phaseEndsAt;
        float _nextTickAt;
        float _nextTurnCheckAt;

        public bool IsWalking => _phase == Phase.Walking;

        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            if (obstacleMask.value == 0)
                obstacleMask = LayerMask.GetMask("Default", "Environment", "Pushable", "TaskProp");
        }

        /// <summary>Sends the toy off from <paramref name="position"/> with a velocity, facing its flight.</summary>
        public void Launch(Vector3 position, Vector3 velocity)
        {
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            Quaternion facing = flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat) : transform.rotation;

            transform.SetPositionAndRotation(position, facing);
            gameObject.SetActive(true);
            _rb.position = position;
            _rb.rotation = facing;
            _rb.angularVelocity = Vector3.zero;
            _rb.linearVelocity = velocity;
            _phase = Phase.Flying;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (_phase != Phase.Flying)
                return;

            // Landing means touching something from above, not glancing off a wall.
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y > 0.5f)
                {
                    StartWalking();
                    return;
                }
            }
        }

        void StartWalking()
        {
            float now = Now;
            _phase = Phase.Walking;
            _phaseEndsAt = now + walkSeconds;
            _nextTickAt = now;
            _nextTurnCheckAt = now;
        }

        void Update()
        {
            if (_phase == Phase.Flying)
                return;

            float now = Now;
            if (_phase == Phase.RunDown)
            {
                if (now >= _phaseEndsAt)
                    gameObject.SetActive(false);
                return;
            }

            if (now >= _phaseEndsAt)
            {
                _phase = Phase.RunDown;
                _phaseEndsAt = now + lingerSeconds;
                return;
            }

            if (now >= _nextTickAt)
            {
                NoiseEvents.Emit(new NoiseEvent(transform.position, tickLoudness, GetHashCode(), now));
                _nextTickAt += tickInterval;
            }

            if (key != null)
                key.Rotate(0f, 0f, keyDegreesPerSecond * Time.deltaTime, Space.Self);
        }

        void FixedUpdate()
        {
            if (_phase == Phase.Flying)
                return;

            Vector3 velocity = _rb.linearVelocity;
            if (_phase == Phase.RunDown)
            {
                _rb.linearVelocity = new Vector3(0f, velocity.y, 0f);
                return;
            }

            float now = Now;
            if (now >= _nextTurnCheckAt)
            {
                _nextTurnCheckAt = now + turnCheckInterval;
                if (Physics.Raycast(_rb.position + Vector3.up * 0.1f, transform.forward, lookAhead,
                        obstacleMask, QueryTriggerInteraction.Ignore))
                    _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, Random.Range(110f, 250f), 0f));
            }

            Vector3 walk = transform.forward * walkSpeed;
            _rb.linearVelocity = new Vector3(walk.x, velocity.y, walk.z);
        }
    }
}
