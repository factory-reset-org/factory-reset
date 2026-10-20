using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A thrown wind-up toy. Once it lands it winds up with a comic "WIND!" and walks for a
    /// while: its heading wanders a little, it hops with each step, its key turns, and it
    /// turns away from walls. It ticks loudly at a steady rate under one source id. Each tick
    /// is marked as a lure (<see cref="NoiseEvent.IsLure"/>), so the Tracker is Distracted by
    /// the first tick on landing, goes to the toy and watches it, instead of waiting to hear it
    /// repeat. When its spring runs out it falls over, lies there a moment, then goes away.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WindUpToy : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float walkSeconds = 8f;
        [SerializeField, Min(0f)] float walkSpeed = 0.9f;

        [Tooltip("Seconds between ticks. Must stay under the Tracker's 1.5 s repeat window.")]
        [SerializeField, Min(0.1f)] float tickInterval = 0.6f;
        [SerializeField, Min(0f)] float tickLoudness = NoiseLoudness.ToyLanding;

        [Tooltip("Seconds it lies there after its spring runs out, before it goes away.")]
        [SerializeField, Min(0f)] float lingerSeconds = 2.5f;

        [Header("Walking")]
        [Tooltip("How far its heading wanders, in radians per second, either way.")]
        [SerializeField, Min(0f)] float wander = 2.5f;

        [Tooltip("Height of each hop, in metres.")]
        [SerializeField, Min(0f)] float hopHeight = 0.05f;
        [SerializeField, Min(0f)] float hopRate = 18f;

        [Header("Turning")]
        [Tooltip("Layers it turns away from.")]
        [SerializeField] LayerMask obstacleMask;
        [SerializeField, Min(0.05f)] float lookAhead = 0.35f;
        [SerializeField, Min(0.05f)] float turnCheckInterval = 0.2f;

        [Header("Look")]
        [Tooltip("The toy's visible body. It hops and falls over, so the physics body does not have to. Optional.")]
        [SerializeField] Transform model;

        [Tooltip("The wind-up key, spun while walking. Optional.")]
        [SerializeField] Transform key;
        [SerializeField] float keyDegreesPerSecond = 690f;

        enum Phase
        {
            Flying,
            Walking,
            RunDown
        }

        Rigidbody _rb;
        Phase _phase;
        float _phaseStartedAt;
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
            ShowModel(0f, 0f);
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
            _phaseStartedAt = now;
            _phaseEndsAt = now + walkSeconds;
            _nextTickAt = now;
            _nextTurnCheckAt = now;
            PropEffects.Word("wind", transform.position + Vector3.up * 0.9f, 0.7f);
        }

        void Update()
        {
            if (_phase == Phase.Flying)
                return;

            float now = Now;
            if (_phase == Phase.RunDown)
            {
                // Falls over onto its side in a fraction of a second, and lies there.
                ShowModel(0f, Mathf.Min(86f, (now - _phaseStartedAt) * 230f));
                if (now >= _phaseEndsAt)
                    gameObject.SetActive(false);
                return;
            }

            if (now >= _phaseEndsAt)
            {
                _phase = Phase.RunDown;
                _phaseStartedAt = now;
                _phaseEndsAt = now + lingerSeconds;
                return;
            }

            if (now >= _nextTickAt)
            {
                NoiseEvents.Emit(new NoiseEvent(transform.position, tickLoudness, GetHashCode(), now, isLure: true));
                GameSfx.Play(Sfx.Tick, 0.7f);
                _nextTickAt += tickInterval;
            }

            if (key != null)
                key.Rotate(0f, 0f, keyDegreesPerSecond * Time.deltaTime, Space.Self);
            ShowModel(Mathf.Abs(Mathf.Sin((now - _phaseStartedAt) * hopRate)) * hopHeight, 0f);
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

            // Its heading wanders a little all the time, so it does not walk a ruler-straight line.
            float drift = Random.Range(-wander, wander) * Mathf.Rad2Deg * Time.fixedDeltaTime;
            _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, drift, 0f));

            Vector3 walk = transform.forward * walkSpeed;
            _rb.linearVelocity = new Vector3(walk.x, velocity.y, walk.z);
        }

        // The body hops and falls over; the physics body stays put.
        void ShowModel(float height, float fallDegrees)
        {
            if (model == null)
                return;
            model.localPosition = new Vector3(0f, height, 0f);
            model.localRotation = Quaternion.Euler(0f, 0f, fallDegrees);
        }
    }
}
