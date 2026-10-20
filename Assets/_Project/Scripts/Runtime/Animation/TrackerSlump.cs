using UnityEngine;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The Tracker Toy's knock-out, a wind-up toy that has run down: the key spins backwards
    /// fast as the spring lets go, the body tips forward and sinks, the head droops, the ears
    /// flop down and the tail goes limp. At the reboot the key winds back up in ticks, the
    /// ears pop up and it gives a quick dog shake.
    /// </summary>
    /// <remarks>
    /// <para><b>Axes</b> (every pivot rests at 0): +X pitches forward, so +X droops the body
    /// and the head and −X lowers the tail, which points back. The ears stick up and out, so
    /// rolling them outwards (+Z the left ear, −Z the right) flops them down the sides.</para>
    /// <para><b>The key</b> is turned here only while the toy is down; <see cref="WindUpKeySpinner"/>
    /// stops while it is disabled and turns it again from the reboot. The unwinding spin dies
    /// away like a spring running out.</para>
    /// <para><b>Stand-up</b> (1.3 s): four ticks of the key (to 0.55), the body and head come
    /// up (0.1 to 0.55), the ears pop up with an overshoot (0.5 to 0.8), a dog shake of the
    /// body (0.7 to 1.15), then the Animator's pose is blended back in.</para>
    /// </remarks>
    public sealed class TrackerSlump : AgentDownPose
    {
        [Header("Rig (Tracker Toy pivots)")]
        [SerializeField] Transform body;
        [SerializeField] Transform head;
        [SerializeField] Transform earLeft;
        [SerializeField] Transform earRight;
        [SerializeField] Transform tail;
        [Tooltip("WindupKey_Pivot.")]
        [SerializeField] Transform key;
        [Tooltip("Axis the key turns around, in its pivot's local space.")]
        [SerializeField] Vector3 keyAxis = Vector3.forward;

        [Header("Down")]
        [SerializeField, Min(0.05f)] float downSeconds = 0.5f;
        [Tooltip("Degrees the body tips forward.")]
        [SerializeField] float bodyPitch = 15f;
        [Tooltip("Metres the body sinks.")]
        [SerializeField] float bodyDrop = 0.05f;
        [Tooltip("Degrees the head droops (on top of the body's tip).")]
        [SerializeField] float headDroop = 25f;
        [Tooltip("Degrees each ear flops down the side.")]
        [SerializeField] float earFlop = 65f;
        [Tooltip("Degrees the ears fall back as they flop.")]
        [SerializeField] float earBack = 15f;
        [Tooltip("Degrees the tail drops.")]
        [SerializeField] float tailLimp = 45f;
        [Tooltip("Degrees per second the key spins backwards as it goes down.")]
        [SerializeField] float unwindSpeed = -1080f;
        [Tooltip("Seconds until the unwinding key has stopped.")]
        [SerializeField, Min(0.1f)] float unwindSeconds = 1.6f;

        [Header("Reboot")]
        [SerializeField, Min(0.3f)] float standSeconds = 1.3f;
        [Tooltip("Ticks of the key as it winds back up.")]
        [SerializeField, Min(1)] int keyTicks = 4;
        [Tooltip("Degrees per tick.")]
        [SerializeField] float tickDegrees = 90f;
        [Tooltip("Degrees the ears overshoot as they pop up.")]
        [SerializeField] float earPop = 20f;
        [Tooltip("Degrees of the dog shake (body roll).")]
        [SerializeField] float shakeRoll = 10f;
        [Tooltip("Shakes per second.")]
        [SerializeField] float shakeRate = 6.5f;

        [Header("Walls (measured on the model)")]
        [Tooltip("Metres in front of the centre the model reaches standing (the snout).")]
        [SerializeField] float standingReach = 1.02f;
        [Tooltip("Metres in front of the centre it reaches slumped.")]
        [SerializeField] float slumpedReach = 1.04f;

        // Stand-up keys, over seconds.
        const float RiseFrom = 0.1f, RiseTo = 0.55f, TicksTo = 0.55f, PopFrom = 0.5f, PopTo = 0.8f;
        const float ShakeFrom = 0.7f, ShakeSeconds = 0.45f, HandBackFrom = 1.15f;

        Joint _body, _head, _earLeft, _earRight, _tail;
        float _fit = 1f;
        bool _down;
        float _keyTurned;   // degrees ticked so far in this stand-up

        /// <inheritdoc/>
        public override float DownSeconds => downSeconds;

        /// <inheritdoc/>
        public override float StandSeconds => standSeconds;

        void Awake()
        {
            _body = new Joint(body);
            _head = new Joint(head);
            _earLeft = new Joint(earLeft);
            _earRight = new Joint(earRight);
            _tail = new Joint(tail);
        }

        /// <inheritdoc/>
        public override void PoseDown(float seconds)
        {
            if (!_down)
            {
                _down = true;
                _fit = FitForward(standingReach, slumpedReach);
            }
            // Blends from the pose it was in when it was hit into the slump.
            float t = EaseOut(seconds / downSeconds);
            Pose(1f, 1f, 0f, 0f, t);
            Amount = t;

            // The spring lets go: fast backwards, dying away.
            if (key != null && seconds < unwindSeconds)
            {
                float fade = 1f - seconds / unwindSeconds;
                key.Rotate(keyAxis, unwindSpeed * fade * fade * Time.deltaTime, Space.Self);
            }
        }

        /// <inheritdoc/>
        public override void PoseStandUp(float seconds)
        {
            if (_down)
            {
                _down = false;
                _keyTurned = 0f;
            }
            float lean = 1f - Smooth((seconds - RiseFrom) / (RiseTo - RiseFrom));
            float flop = 1f - Smooth((seconds - PopFrom) / (PopTo - PopFrom) * 2f);
            float pop = seconds > PopFrom && seconds < PopTo ? Mathf.Sin((seconds - PopFrom) / (PopTo - PopFrom) * Mathf.PI) : 0f;
            float shake = Decaying(seconds - ShakeFrom, ShakeSeconds, shakeRate);
            float weight = seconds < HandBackFrom ? 1f : 1f - Smooth((seconds - HandBackFrom) / (standSeconds - HandBackFrom));
            Pose(lean, flop, pop, shake, weight);
            Amount = lean;
            TickKey(seconds);
        }

        /// <inheritdoc/>
        public override void Release()
        {
            Amount = 0f;
            _down = false;
        }

        // lean: the body, head and tail (0 to 1); flop: the ears; pop: the ears' overshoot up;
        // shake: the dog shake, −1 to 1.
        void Pose(float lean, float flop, float pop, float shake, float weight = 1f)
        {
            float leanFit = lean * _fit;
            _body.Pose(new Vector3(bodyPitch * leanFit, 0f, shakeRoll * shake), Vector3.down * (bodyDrop * lean), weight);
            _head.Pose(new Vector3(headDroop * leanFit, 0f, 0f), Vector3.zero, weight);
            _tail.Pose(new Vector3(-tailLimp * lean, 0f, 0f), Vector3.zero, weight);
            // Up is inwards: −Z for the left ear, +Z for the right.
            float roll = earFlop * flop - earPop * pop;
            _earLeft.Pose(new Vector3(-earBack * flop, 0f, roll), Vector3.zero, weight);
            _earRight.Pose(new Vector3(-earBack * flop, 0f, -roll), Vector3.zero, weight);
        }

        // Winds the key forward in quick ticks, each a snap and a pause, like a ratchet.
        void TickKey(float seconds)
        {
            if (key == null)
                return;
            float spacing = TicksTo / keyTicks;
            int tick = Mathf.Min(keyTicks - 1, Mathf.FloorToInt(seconds / spacing));
            float into = Mathf.Clamp01((seconds - tick * spacing) / (spacing * 0.4f));
            float target = (tick + EaseOut(into)) * tickDegrees;
            if (seconds >= TicksTo)
                target = keyTicks * tickDegrees;
            key.Rotate(keyAxis, target - _keyTurned, Space.Self);
            _keyTurned = target;
        }
    }
}
