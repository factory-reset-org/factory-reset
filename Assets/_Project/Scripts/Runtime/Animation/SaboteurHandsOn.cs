using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The Saboteur's hands-on move: when its controller carries out a door, trap or battery
    /// action (<see cref="AgentController.ActionCarriedOut"/>), it turns its body towards the
    /// target, leans in with both arms reaching forward and down, opens its pincers and snaps
    /// them shut on the target, then straightens up. A stolen battery appears in the open crate
    /// on its back at the pinch and stays there.
    /// </summary>
    /// <remarks>
    /// <para>Before this the action happened the moment the Saboteur was in reach and nothing
    /// moved, so arming a trap or taking a battery looked like nothing at all (S3's check of
    /// the squad on screen). The action itself is still instant: the move only shows it, and
    /// the brain is told straight away, as before.</para>
    /// <para>Posed like <see cref="AgentClawSwipe"/>: in LateUpdate, added to the pivots the
    /// Animator has just written, from normalised time 0 to 1. The angles are for the remade
    /// Saboteur, whose upper arms hang at its sides with the forearms pointing forward.</para>
    /// <para>The controller acts from up to 2.5 m away, but at full reach the pincers only get
    /// 1.3 m in front (measured on the model), so the move also rolls the model root towards
    /// the target by however much it is beyond that, up to 0.6 m, and back: the pincers close
    /// on the target instead of on air (a battery 1.49 m away in play). The swipe sets the same
    /// root every frame, so this runs after it (execution order) and only while playing; the
    /// two never play together, because the Saboteur only acts away from the player.</para>
    /// </remarks>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(AgentController))]
    public sealed class SaboteurHandsOn : MonoBehaviour
    {
        [Header("Rig (Saboteur Bot pivots)")]
        [Tooltip("The model root (SaboteurBot_Root, which carries the wheel): rolls towards a target beyond the pincers' reach.")]
        [SerializeField] Transform root;
        [Tooltip("Body_Pivot: turns towards the target and leans in.")]
        [SerializeField] Transform body;
        [SerializeField] Transform shoulderLeft;
        [SerializeField] Transform elbowLeft;
        [SerializeField] Transform clawLeftTop;
        [SerializeField] Transform clawLeftBottom;
        [SerializeField] Transform shoulderRight;
        [SerializeField] Transform elbowRight;
        [SerializeField] Transform clawRightTop;
        [SerializeField] Transform clawRightBottom;
        [Tooltip("The battery in the crate on its back; hidden until it steals one.")]
        [SerializeField] GameObject carriedBattery;

        [Header("Timing")]
        [Tooltip("Seconds the move takes.")]
        [SerializeField, Min(0.1f)] float duration = 0.7f;
        [Tooltip("When in the move (normalised time) the pincers close on the target.")]
        [SerializeField, Range(0f, 1f)] float pinchAt = 0.5f;
        [Tooltip("Most the body turns towards a target off to one side (degrees).")]
        [SerializeField, Range(0f, 90f)] float maxTurn = 60f;
        [Tooltip("How far in front of the agent the pincer tips get at full reach (m, measured on the model).")]
        [SerializeField, Min(0f)] float pincerReach = 1.3f;
        [Tooltip("Most the robot rolls towards a target beyond the pincers' reach (m).")]
        [SerializeField, Min(0f)] float maxRoll = 0.6f;

        [Header("Curves over normalised time")]
        [Tooltip("How much of the reach is applied: eased in, held, eased out (flat keys, so it never overshoots 1).")]
        [SerializeField] AnimationCurve reach = Flat((0f, 0f), (0.3f, 1f), (0.65f, 1f), (1f, 0f));
        [Tooltip("Body lean at full reach (degrees, + = forward).")]
        [SerializeField] float lean = 18f;
        [Tooltip("Shoulder pitch at full reach (degrees, - = forward; -90 = straight ahead).")]
        [SerializeField] float shoulder = -70f;
        [Tooltip("Elbow pitch at full reach (degrees, + = forearm down).")]
        [SerializeField] float elbow = 35f;
        [Tooltip("Pincer opening per jaw (degrees): open on the way in, snapped shut at the pinch.")]
        [SerializeField] AnimationCurve pincer = Keys((0f, 0f), (0.2f, 30f), (0.46f, 30f), (0.52f, -5f), (0.7f, 0f), (1f, 0f));

        AgentController _agent;
        Vector3 _rootRest;
        Vector3 _target;
        float _time = -1f;
        float _turn;
        float _roll;
        bool _pinched;
        bool _stealing;

        /// <summary>True while the move is playing.</summary>
        public bool IsPlaying => _time >= 0f;

        /// <summary>Moves played since this body spawned (for tests).</summary>
        public int Moves { get; private set; }

        /// <summary>True once it has stolen a battery: the battery shows in its crate.</summary>
        public bool CarriesBattery => carriedBattery != null && carriedBattery.activeSelf;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            if (root != null)
                _rootRest = root.localPosition;
        }

        void OnEnable() => _agent.ActionCarriedOut += OnActionCarriedOut;

        void OnDisable()
        {
            _agent.ActionCarriedOut -= OnActionCarriedOut;
            Stop();
        }

        void Stop()
        {
            if (IsPlaying && root != null)
                root.localPosition = _rootRest;
            _time = -1f;
        }

        void OnActionCarriedOut(AgentAction action, Vector3 target)
        {
            if (action != AgentAction.CloseDoor && action != AgentAction.ArmTrap && action != AgentAction.StealBattery)
                return;
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            _turn = toTarget.sqrMagnitude > 1e-4f
                ? Mathf.Clamp(Vector3.SignedAngle(transform.forward, toTarget, Vector3.up), -maxTurn, maxTurn)
                : 0f;
            _target = target;
            _roll = Mathf.Clamp(toTarget.magnitude - pincerReach, 0f, maxRoll);
            _stealing = action == AgentAction.StealBattery;
            _pinched = false;
            _time = 0f;
            Moves++;
        }

        void LateUpdate()
        {
            if (!IsPlaying)
                return;
            if (_agent.IsDisabled || _agent.IsDead)
            {
                Stop();
                return;
            }
            if (!_agent.IsFrozen)
                _time += Time.deltaTime;

            float t = Mathf.Clamp01(_time / duration);
            if (!_pinched && t >= pinchAt)
            {
                _pinched = true;
                if (_stealing && carriedBattery != null)
                    carriedBattery.SetActive(true);
            }
            Pose(t);
            if (_time >= duration)
                Stop();
        }

        void Pose(float t)
        {
            float w = reach.Evaluate(t);
            if (root != null && root.parent != null)
            {
                Vector3 toTarget = _target - transform.position;
                toTarget.y = 0f;
                Vector3 along = toTarget.sqrMagnitude > 1e-4f
                    ? root.parent.InverseTransformDirection(toTarget.normalized)
                    : Vector3.forward;
                root.localPosition = _rootRest + along * (_roll * w);
            }
            if (body != null)
                body.localRotation *= Quaternion.Euler(lean * w, _turn * w, 0f);
            Pitch(shoulderLeft, shoulder * w);
            Pitch(shoulderRight, shoulder * w);
            Pitch(elbowLeft, elbow * w);
            Pitch(elbowRight, elbow * w);
            float open = pincer.Evaluate(t);
            Pitch(clawLeftTop, -open);
            Pitch(clawRightTop, -open);
            Pitch(clawLeftBottom, open);
            Pitch(clawRightBottom, open);
        }

        static void Pitch(Transform pivot, float degrees)
        {
            if (pivot != null)
                pivot.localRotation *= Quaternion.Euler(degrees, 0f, 0f);
        }

        static AnimationCurve Flat(params (float time, float value)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (time, value) in keys)
                curve.AddKey(new Keyframe(time, value, 0f, 0f));
            return curve;
        }

        static AnimationCurve Keys(params (float time, float value)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (time, value) in keys)
                curve.AddKey(time, value);
            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0f);
            return curve;
        }
    }
}
