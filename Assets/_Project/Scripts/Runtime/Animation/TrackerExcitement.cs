using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The Tracker Toy's joy at a thrown toy, a happy puppy: while its brain is Distracted it
    /// hops when it spots the toy, then bounces on the spot, wags its tail fast, perks and
    /// flaps its ears, tilts its head from side to side and winds its key twice as fast while
    /// it watches; following a toy that moves, it wags and perks its ears only.
    /// </summary>
    /// <remarks>
    /// <para><b>Reads, never writes, the brain:</b> the state name
    /// (<see cref="AgentController.DebugState"/> is "Distracted") and the ground speed
    /// (stopped is watching, moving is following). The brain is unchanged.</para>
    /// <para><b>Layered on the Animator:</b> added in LateUpdate on top of the clip pose,
    /// after <see cref="AgentBite"/> has set the model root (execution order). The clips key
    /// the head, the ears and the tail, so the Animator resets them next frame; the root, which
    /// no clip animates, is set from its rest pose plus the hop.</para>
    /// <para><b>Blending:</b> the watching and following weights ease in and out over 0.2 s,
    /// so it never pops. Nothing plays while it is knocked down, scrapped or frozen (cut at
    /// once, since the slump then owns the pose) or while it rewinds or pounces (eased out).</para>
    /// <para><b>Axes</b> (every pivot rests at 0): −X lifts the head; the ears stick up and
    /// out, so −Z lifts the left ear and +Z the right; +Y turns the tail.</para>
    /// </remarks>
    [DefaultExecutionOrder(10)]   // after AgentBite has set the root
    [RequireComponent(typeof(AgentController))]
    public sealed class TrackerExcitement : MonoBehaviour
    {
        /// <summary>The Tracker brain's state name while it is distracted by a toy.</summary>
        public const string DistractedState = "Distracted";

        [Header("Rig (Tracker Toy pivots)")]
        [Tooltip("TrackerToy_Root: hops and bounces (no clip animates it).")]
        [SerializeField] Transform root;
        [SerializeField] Transform head;
        [SerializeField] Transform earLeft;
        [SerializeField] Transform earRight;
        [SerializeField] Transform tail;

        [Header("Toy spotted (one shot)")]
        [SerializeField, Min(0.05f)] float spottedSeconds = 0.4f;
        [Tooltip("Metres the toy hops.")]
        [SerializeField] float spottedHop = 0.12f;
        [Tooltip("Degrees the ears shoot up.")]
        [SerializeField] float spottedEars = 25f;
        [Tooltip("Degrees the head snaps up.")]
        [SerializeField] float spottedHead = 10f;

        [Header("Watching (stopped)")]
        [Tooltip("Metres of each bounce.")]
        [SerializeField] float bounceHeight = 0.04f;
        [Tooltip("Bounces per second.")]
        [SerializeField] float bounceRate = 3f;
        [Tooltip("Degrees the ears flap, the two out of step.")]
        [SerializeField] float earFlap = 10f;
        [Tooltip("Flaps per second.")]
        [SerializeField] float earFlapRate = 3f;
        [Tooltip("Degrees the head tilts to each side.")]
        [SerializeField] float headTilt = 15f;
        [Tooltip("Tilts (left and right) per second.")]
        [SerializeField] float headTiltRate = 0.7f;
        [Tooltip("The key winds this many times its normal speed.")]
        [SerializeField, Min(1f)] float keySpeed = 2f;

        [Header("Watching and following")]
        [Tooltip("Degrees the tail wags to each side.")]
        [SerializeField] float tailWag = 30f;
        [Tooltip("Wags per second.")]
        [SerializeField] float tailWagRate = 6f;
        [Tooltip("Degrees the ears perk up.")]
        [SerializeField] float earPerk = 20f;

        [Header("Blending")]
        [SerializeField, Min(0.01f)] float blendSeconds = 0.2f;
        [Tooltip("Ground speed (m/s) above which it is following rather than watching.")]
        [SerializeField, Min(0f)] float movingSpeed = 0.3f;

        AgentController _agent;
        AgentKnockdown _knockdown;
        AgentBite _bite;
        WindUpKeySpinner _key;
        Vector3 _rootRest;
        bool _rootMoved;
        bool _wasDistracted;
        float _spotted = -1f;   // seconds into the one shot, -1 when not playing
        float _clock;

        /// <summary>How much of the watching loop plays, 0 to 1.</summary>
        public float Watching { get; private set; }

        /// <summary>How much of the tail wag and perked ears play, 0 to 1 (watching or following).</summary>
        public float Excited { get; private set; }

        /// <summary>True during the hop when it spots the toy.</summary>
        public bool IsSpotting => _spotted >= 0f;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            _knockdown = GetComponent<AgentKnockdown>();
            _bite = GetComponent<AgentBite>();
            _key = GetComponent<WindUpKeySpinner>();
            if (root != null)
                _rootRest = root.localPosition;
        }

        void OnDisable()
        {
            Watching = Excited = 0f;
            _spotted = -1f;
            _wasDistracted = false;
            if (_key != null)
                _key.SpeedMultiplier = 1f;
            if (root != null && _rootMoved)
                root.localPosition = _rootRest;
            _rootMoved = false;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _clock += dt;

            // Down, scrapped or frozen: the slump owns the pose, so stop at once.
            bool down = _agent.IsDisabled || _agent.IsDead || _agent.IsFrozen
                || (_knockdown != null && _knockdown.IsDown);
            bool striking = _bite != null && _bite.IsStriking;
            bool busy = down || striking || (_agent.WindUp != null && _agent.WindUp.IsRewinding);

            bool distracted = _agent.DebugState == DistractedState;
            if (distracted && !_wasDistracted && !busy)
                _spotted = 0f;
            _wasDistracted = distracted;

            bool excited = distracted && !busy;
            bool watching = excited && _agent.Speed <= movingSpeed;
            float step = dt / blendSeconds;
            Watching = down ? 0f : Mathf.MoveTowards(Watching, watching ? 1f : 0f, step);
            Excited = down ? 0f : Mathf.MoveTowards(Excited, excited ? 1f : 0f, step);

            float arc = 0f;
            if (_spotted >= 0f)
            {
                _spotted += dt;
                if (_spotted >= spottedSeconds || busy)
                    _spotted = -1f;
                else
                    arc = Mathf.Sin(_spotted / spottedSeconds * Mathf.PI);
            }

            if (_key != null)
                _key.SpeedMultiplier = Mathf.Lerp(1f, keySpeed, Watching);

            if (!striking)
                PoseRoot(spottedHop * arc + bounceHeight * Mathf.Abs(Mathf.Sin(_clock * bounceRate * Mathf.PI)) * Watching);
            if (Excited <= 0f && Watching <= 0f && arc <= 0f)
                return;

            if (tail != null)
                tail.localRotation *= Quaternion.Euler(0f, tailWag * Wave(tailWagRate) * Excited, 0f);

            // Both ears up; the flap lifts one while it drops the other.
            float flap = earFlap * Wave(earFlapRate) * Watching;
            float up = earPerk * Excited + spottedEars * arc;
            if (earLeft != null)
                earLeft.localRotation *= Quaternion.Euler(0f, 0f, -(up + flap));
            if (earRight != null)
                earRight.localRotation *= Quaternion.Euler(0f, 0f, up - flap);

            if (head != null)
                head.localRotation *= Quaternion.Euler(-spottedHead * arc, 0f, headTilt * Wave(headTiltRate) * Watching);
        }

        float Wave(float perSecond) => Mathf.Sin(_clock * perSecond * 2f * Mathf.PI);

        void PoseRoot(float height)
        {
            if (root == null || (height <= 0f && !_rootMoved))
                return;
            root.localPosition = _rootRest + Vector3.up * height;
            _rootMoved = height > 0f;
        }
    }
}
