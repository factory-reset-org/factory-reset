using UnityEngine;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The Saboteur's knock-out: it wobbles on its one wheel, side to side and dying away,
    /// then leans forward and stays there, arms hanging, pincers open and limp, claws just
    /// above the floor. At the reboot it straightens up and its pincers snap shut. A scrapped
    /// Saboteur slumps the same way before it sinks.
    /// </summary>
    /// <remarks>
    /// <para><b>Axes</b> (every pivot rests at 0): +X pitches forward. The arms rest pointing
    /// forward, so +X on the shoulders lowers them; the top claw opens with −X and the bottom
    /// one with +X (the clips close them about 4° each).</para>
    /// <para><b>Down</b> (0.95 s): the wobble rolls the body ±12° at 2.5 a second, dying away
    /// by 0.7 s, while the arms start to sag; the lean comes in from 0.45 s. The angles keep
    /// the claws a few centimetres off the floor (measured on the model).</para>
    /// <para><b>Stand-up</b> (0.9 s): straightens (to 0.5), the arms come up (0.1 to 0.55),
    /// the pincers snap shut in 0.06 s at 0.55, then the Animator's pose is blended back in.</para>
    /// </remarks>
    public sealed class SaboteurSlump : AgentDownPose
    {
        [Header("Rig (Saboteur pivots)")]
        [SerializeField] Transform body;
        [SerializeField] Transform shoulderLeft;
        [SerializeField] Transform shoulderRight;
        [SerializeField] Transform elbowLeft;
        [SerializeField] Transform elbowRight;
        [SerializeField] Transform clawLeftTop;
        [SerializeField] Transform clawLeftBottom;
        [SerializeField] Transform clawRightTop;
        [SerializeField] Transform clawRightBottom;

        [Header("Down")]
        [Tooltip("Degrees of the first wobble (body roll), dying away.")]
        [SerializeField] float wobbleRoll = 12f;
        [Tooltip("Wobbles per second.")]
        [SerializeField] float wobbleRate = 2.5f;
        [SerializeField, Min(0.1f)] float wobbleSeconds = 0.7f;
        [Tooltip("Degrees the body leans forward.")]
        [SerializeField] float lean = 30f;
        [Tooltip("Seconds into the knock-out when the lean starts.")]
        [SerializeField, Min(0f)] float leanFrom = 0.45f;
        [SerializeField, Min(0.05f)] float leanSeconds = 0.5f;
        [Tooltip("Degrees the shoulders drop the arms (on top of the lean).")]
        [SerializeField] float armDrop = 30f;
        [Tooltip("Degrees the elbows hang.")]
        [SerializeField] float elbowHang = 5f;
        [Tooltip("Degrees each pincer jaw opens.")]
        [SerializeField] float clawOpen = 30f;

        [Header("Reboot")]
        [SerializeField, Min(0.3f)] float standSeconds = 0.9f;
        [Tooltip("Degrees each jaw closes past rest when the pincers snap shut (the clips meet at 4).")]
        [SerializeField] float clawShut = 4f;

        [Header("Walls (measured on the model)")]
        [Tooltip("Metres in front of the centre the body reaches standing (without the arms, which hang back as it leans).")]
        [SerializeField] float standingReach = 0.5f;
        [Tooltip("Metres in front of the centre the body reaches slumped (the goggles).")]
        [SerializeField] float slumpedReach = 0.93f;

        const float StraightTo = 0.5f, ArmsFrom = 0.1f, ArmsTo = 0.55f, SnapAt = 0.55f, SnapSeconds = 0.06f, HandBackFrom = 0.7f;

        Joint _body, _shoulderLeft, _shoulderRight, _elbowLeft, _elbowRight;
        Joint _clawLeftTop, _clawLeftBottom, _clawRightTop, _clawRightBottom;
        float _fit = 1f;
        bool _down;

        /// <inheritdoc/>
        public override float DownSeconds => leanFrom + leanSeconds;

        /// <inheritdoc/>
        public override float StandSeconds => standSeconds;

        void Awake()
        {
            _body = new Joint(body);
            _shoulderLeft = new Joint(shoulderLeft);
            _shoulderRight = new Joint(shoulderRight);
            _elbowLeft = new Joint(elbowLeft);
            _elbowRight = new Joint(elbowRight);
            _clawLeftTop = new Joint(clawLeftTop);
            _clawLeftBottom = new Joint(clawLeftBottom);
            _clawRightTop = new Joint(clawRightTop);
            _clawRightBottom = new Joint(clawRightBottom);
        }

        /// <inheritdoc/>
        public override void PoseDown(float seconds)
        {
            if (!_down)
            {
                _down = true;
                _fit = FitForward(standingReach, slumpedReach);
            }
            float leaning = Smooth((seconds - leanFrom) / leanSeconds);
            float sag = Mathf.Max(0.5f * EaseOut(seconds / leanFrom), leaning);
            float roll = wobbleRoll * Decaying(seconds, wobbleSeconds, wobbleRate);
            // Blends quickly from the pose it was hit in; the wobble carries the rest.
            float weight = EaseOut(seconds / 0.15f);
            Pose(leaning, sag, Mathf.Min(1f, seconds / 0.3f), roll, weight);
            Amount = Mathf.Min(1f, seconds / DownSeconds);
        }

        /// <inheritdoc/>
        public override void PoseStandUp(float seconds)
        {
            _down = false;
            float leaning = 1f - Smooth(seconds / StraightTo);
            float sag = 1f - Smooth((seconds - ArmsFrom) / (ArmsTo - ArmsFrom));
            // Open until the snap, then shut tight (negative opens the other way: closed).
            float claws = seconds < SnapAt ? 1f : Mathf.Lerp(1f, -clawShut / clawOpen, EaseOut((seconds - SnapAt) / SnapSeconds));
            float weight = seconds < HandBackFrom ? 1f : 1f - Smooth((seconds - HandBackFrom) / (standSeconds - HandBackFrom));
            Pose(leaning, sag, claws, 0f, weight);
            Amount = leaning;
        }

        /// <inheritdoc/>
        public override void Release()
        {
            Amount = 0f;
            _down = false;
        }

        // leaning: the body's forward lean, 0 to 1; sag: the arms; claws: open 1, rest 0;
        // roll: degrees of wobble.
        void Pose(float leaning, float sag, float claws, float roll, float weight)
        {
            _body.Pose(new Vector3(lean * leaning * _fit, 0f, roll), Vector3.zero, weight);
            Vector3 arm = new Vector3(armDrop * sag, 0f, 0f);
            _shoulderLeft.Pose(arm, Vector3.zero, weight);
            _shoulderRight.Pose(arm, Vector3.zero, weight);
            Vector3 elbow = new Vector3(elbowHang * sag, 0f, 0f);
            _elbowLeft.Pose(elbow, Vector3.zero, weight);
            _elbowRight.Pose(elbow, Vector3.zero, weight);
            Vector3 top = new Vector3(-clawOpen * claws, 0f, 0f);
            _clawLeftTop.Pose(top, Vector3.zero, weight);
            _clawRightTop.Pose(top, Vector3.zero, weight);
            _clawLeftBottom.Pose(-top, Vector3.zero, weight);
            _clawRightBottom.Pose(-top, Vector3.zero, weight);
        }
    }
}
