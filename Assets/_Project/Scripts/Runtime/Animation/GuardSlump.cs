using UnityEngine;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The Guard's knock-out, a machine whose power is cut: it slumps at the waist, the head
    /// bows, both arms drop and the cannon points at the floor (the knock-down puffs smoke
    /// from its head). At the reboot it straightens in three servo stutters, then swings the
    /// cannon back up.
    /// </summary>
    /// <remarks>
    /// <para><b>Axes</b> (every pivot rests at 0): +X pitches forward, so +X slumps the torso,
    /// bows the head and lowers the arms, which hang from the shoulders with the cannon
    /// pointing forward.</para>
    /// <para><b>Stand-up</b> (1.1 s): the torso, the head and the shield arm come up a third
    /// at a time, each a quick move and a hold (to 0.75), like a servo stepping; then the
    /// cannon swings up, a little past, and settles (0.6 to 0.95); then the Animator's pose is
    /// blended back in.</para>
    /// </remarks>
    public sealed class GuardSlump : AgentDownPose
    {
        [Header("Rig (Guard pivots)")]
        [SerializeField] Transform torso;
        [SerializeField] Transform head;
        [Tooltip("CannonArm_R_Pivot.")]
        [SerializeField] Transform cannonArm;
        [Tooltip("ShieldArm_L_Pivot.")]
        [SerializeField] Transform shieldArm;

        [Header("Down")]
        [SerializeField, Min(0.05f)] float downSeconds = 0.6f;
        [Tooltip("Degrees the torso slumps forward at the waist.")]
        [SerializeField] float waistSlump = 25f;
        [Tooltip("Degrees the head bows (on top of the slump).")]
        [SerializeField] float headBow = 30f;
        [Tooltip("Degrees the cannon arm drops: the cannon points at the floor.")]
        [SerializeField] float cannonDrop = 35f;
        [Tooltip("Degrees the shield arm drops.")]
        [SerializeField] float shieldDrop = 25f;

        [Header("Reboot")]
        [SerializeField, Min(0.3f)] float standSeconds = 1.1f;
        [Tooltip("Servo steps to straighten up.")]
        [SerializeField, Min(1)] int stutters = 3;
        [Tooltip("Degrees the cannon swings past its pose before it settles.")]
        [SerializeField] float cannonOvershoot = 10f;

        [Header("Walls (measured on the model)")]
        [Tooltip("Metres in front of the centre the model reaches standing.")]
        [SerializeField] float standingReach = 0.53f;
        [Tooltip("Metres in front of the centre it reaches slumped (the antenna on the bowed head).")]
        [SerializeField] float slumpedReach = 1.13f;

        const float StepsTo = 0.75f, StepMove = 0.45f, SwingFrom = 0.6f, SwingTo = 0.95f, HandBackFrom = 0.95f;

        Joint _torso, _head, _cannon, _shield;
        float _fit = 1f;
        bool _down;

        /// <inheritdoc/>
        public override float DownSeconds => downSeconds;

        /// <inheritdoc/>
        public override float StandSeconds => standSeconds;

        void Awake()
        {
            _torso = new Joint(torso);
            _head = new Joint(head);
            _cannon = new Joint(cannonArm);
            _shield = new Joint(shieldArm);
        }

        /// <inheritdoc/>
        public override void PoseDown(float seconds)
        {
            if (!_down)
            {
                _down = true;
                _fit = FitForward(standingReach, slumpedReach);
            }
            // Blends from the pose it was in when it was hit: the power goes, it drops.
            float t = EaseOut(seconds / downSeconds);
            Pose(1f, 1f, t);
            Amount = t;
        }

        /// <inheritdoc/>
        public override void PoseStandUp(float seconds)
        {
            _down = false;
            float slump = 1f - Stepped(seconds / StepsTo);
            float s = (seconds - SwingFrom) / (SwingTo - SwingFrom);
            // The cannon: down until the swing, then up, past its pose, and back.
            float cannon = s <= 0f ? 1f : s >= 1f ? 0f : 1f - Smooth(s) - cannonOvershoot / cannonDrop * Mathf.Sin(s * Mathf.PI);
            float weight = seconds < HandBackFrom ? 1f : 1f - Smooth((seconds - HandBackFrom) / (standSeconds - HandBackFrom));
            Pose(slump, cannon, weight);
            Amount = slump;
        }

        /// <inheritdoc/>
        public override void Release()
        {
            Amount = 0f;
            _down = false;
        }

        // slump: the torso, head and shield arm, 0 to 1; cannon: the cannon arm's drop.
        void Pose(float slump, float cannon, float weight)
        {
            _torso.Pose(new Vector3(waistSlump * slump * _fit, 0f, 0f), Vector3.zero, weight);
            _head.Pose(new Vector3(headBow * slump * _fit, 0f, 0f), Vector3.zero, weight);
            _shield.Pose(new Vector3(shieldDrop * slump, 0f, 0f), Vector3.zero, weight);
            _cannon.Pose(new Vector3(cannonDrop * cannon, 0f, 0f), Vector3.zero, weight);
        }

        // 0 to 1 in equal steps, each a quick move then a hold.
        float Stepped(float t)
        {
            if (t >= 1f)
                return 1f;
            float scaled = Mathf.Max(0f, t) * stutters;
            int step = Mathf.FloorToInt(scaled);
            float move = EaseOut((scaled - step) / StepMove);
            return (step + move) / stutters;
        }
    }
}
