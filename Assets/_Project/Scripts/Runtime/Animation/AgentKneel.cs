using UnityEngine;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The Captain's knock-down: instead of tipping over it drops onto one knee, head bowed,
    /// one cannon resting on the front knee; at the reboot it steps back up. Driven by
    /// <see cref="AgentKnockdown"/>, which uses this in place of its tip-over when the body
    /// has one.
    /// </summary>
    /// <remarks>
    /// <para><b>The kneel:</b> every pivot is a hinge on local X (negative swings a leg
    /// forward, positive bends a knee back, as in the walk clips). The left leg goes forward
    /// with the shin vertical (hip −91°, knee +91°, so the boot stays flat), the right
    /// thigh hangs almost straight down so its knee rests on the floor, its shin lies back
    /// and its boot stands on its sole behind, and the model root drops 0.45 m: the front
    /// boot stays on the floor when 0.44 m · (1 − cos 91°) ≈ 0.45 m. The back leg's angles
    /// were measured on the model so the knee, shin and boot all touch the floor.</para>
    /// <para><b>Stepping back up</b> (1.2 s): it leans in and pushes (0 to 0.2), rises into
    /// a lunge with the front shin still vertical and the back leg straight (to 0.55), lifts
    /// the front foot and steps it back beside the other (to 0.9), then hands back to the
    /// Animator. Keys are eased in and out (smoothstep between keys), so each phase starts
    /// and stops cleanly, like a heavy machine.</para>
    /// <para><b>With the Animator:</b> applied in LateUpdate over the pose the Animator wrote
    /// that frame, blending from it while going down and back into it at the end of the
    /// stand-up, so there is no pop at either end. A pivot no clip writes is posed from its
    /// rest rotation instead.</para>
    /// </remarks>
    public sealed class AgentKneel : MonoBehaviour
    {
        [Header("Rig (Captain pivots)")]
        [Tooltip("The model root that drops (no clip animates it).")]
        [SerializeField] Transform root;
        [Tooltip("Leg_L_Pivot: the front leg, foot planted.")]
        [SerializeField] Transform frontHip;
        [SerializeField] Transform frontKnee;
        [SerializeField] Transform frontAnkle;
        [Tooltip("Leg_R_Pivot: the back leg, knee on the floor.")]
        [SerializeField] Transform backHip;
        [SerializeField] Transform backKnee;
        [SerializeField] Transform backAnkle;
        [SerializeField] Transform torso;
        [SerializeField] Transform head;
        [Tooltip("CannonArm_L_Pivot: rests on the front knee.")]
        [SerializeField] Transform frontArm;
        [SerializeField] Transform backArm;

        [Header("Timing")]
        [Tooltip("Seconds to drop onto the knee.")]
        [SerializeField, Min(0.05f)] float downSeconds = 0.55f;

        [Tooltip("Seconds to step back up at the reboot.")]
        [SerializeField, Min(0.1f)] float standSeconds = 1.2f;

        // Channels, in the order of the tables below.
        const int Drop = 0, FrontHip = 1, FrontKnee = 2, FrontAnkle = 3, BackHip = 4, BackKnee = 5,
            BackAnkle = 6, Torso = 7, Head = 8, FrontArm = 9, BackArm = 10, Channels = 11;

        // Kneeling: root drop (m), then degrees about local X.
        // Measured on the model: both boots and the back shin within 3 mm of the floor.
        static readonly float[] Kneeling = { 0.45f, -91f, 91f, 0f, 15f, 85f, -95f, 12f, 15f, -30f, 5f };

        // Stepping back up, over normalised time: push, lunge, lift the front foot, step it back.
        static readonly float[] StandTimes = { 0f, 0.2f, 0.55f, 0.7f, 0.9f, 1f };
        static readonly float[][] Standing =
        {
            new[] { 0.45f, 0.45f, 0.08f, 0.01f, 0f, 0f },      // drop: rises into the lunge
            new[] { -91f, -91f, -35f, -30f, 0f, 0f },          // front hip
            new[] { 91f, 91f, 35f, 50f, 0f, 0f },              // front knee: shin vertical, then lifts the foot
            new[] { 0f, 0f, 0f, -20f, 0f, 0f },                // front ankle
            new[] { 15f, 15f, 15f, 8f, 0f, 0f },               // back hip
            new[] { 85f, 85f, 20f, 10f, 0f, 0f },              // back knee: off the floor
            new[] { -95f, -95f, -35f, -15f, 0f, 0f },          // back ankle: boot on its sole behind, then flat in the lunge
            new[] { 12f, 22f, 15f, 6f, 0f, 0f },               // torso: leans in to push
            new[] { 15f, 15f, 12f, 6f, 0f, 0f },               // head: comes up last
            new[] { -30f, -35f, -10f, 0f, 0f, 0f },            // front arm: pushes on the knee
            new[] { 5f, 10f, 5f, 0f, 0f, 0f },                 // back arm
        };

        // From this point of the stand-up the Animator's pose is blended back in.
        const float HandBackFrom = 0.85f;

        Transform[] _pivots;
        Quaternion[] _rest;
        Quaternion[] _written;
        Vector3 _rootRest;
        readonly float[] _pose = new float[Channels];

        /// <summary>Seconds to step back up; the knock-down waits this long before it counts as up.</summary>
        public float StandSeconds => standSeconds;

        /// <summary>How far down it is: 0 standing, 1 kneeling (the root drop over the full drop).</summary>
        public float Amount { get; private set; }

        void Awake()
        {
            _pivots = new Transform[Channels];
            _pivots[FrontHip] = frontHip;
            _pivots[FrontKnee] = frontKnee;
            _pivots[FrontAnkle] = frontAnkle;
            _pivots[BackHip] = backHip;
            _pivots[BackKnee] = backKnee;
            _pivots[BackAnkle] = backAnkle;
            _pivots[Torso] = torso;
            _pivots[Head] = head;
            _pivots[FrontArm] = frontArm;
            _pivots[BackArm] = backArm;
            _rest = new Quaternion[Channels];
            _written = new Quaternion[Channels];
            for (int i = 1; i < Channels; i++)
                if (_pivots[i] != null)
                    _rest[i] = _written[i] = _pivots[i].localRotation;
            if (root != null)
                _rootRest = root.localPosition;
        }

        /// <summary>Poses the drop onto the knee, <paramref name="seconds"/> after going down.</summary>
        public void PoseDown(float seconds)
        {
            float t = Mathf.Clamp01(seconds / downSeconds);
            float joints = 1f - (1f - t) * (1f - t) * (1f - t);   // ease out: drops fast, settles
            float drop = Landing(t);                              // and lands a little heavy
            for (int i = 0; i < Channels; i++)
                _pose[i] = Kneeling[i];
            _pose[Drop] *= drop;
            Apply(joints);
        }

        /// <summary>Poses the stand-up, <paramref name="seconds"/> after the reboot.</summary>
        public void PoseStandUp(float seconds)
        {
            float t = Mathf.Clamp01(seconds / standSeconds);
            for (int i = 0; i < Channels; i++)
                _pose[i] = Evaluate(Standing[i], t);
            float blend = t < HandBackFrom ? 1f : 1f - Smooth((t - HandBackFrom) / (1f - HandBackFrom));
            Apply(blend);
        }

        /// <summary>Standing: puts the root back; the pivots are the Animator's again.</summary>
        public void Release()
        {
            Amount = 0f;
            if (root != null)
                root.localPosition = _rootRest;
        }

        // Writes the pose over what the Animator wrote this frame, by weight.
        void Apply(float weight)
        {
            Amount = _pose[Drop] / Kneeling[Drop];
            if (root != null)
                root.localPosition = _rootRest + Vector3.down * _pose[Drop];

            for (int i = 1; i < Channels; i++)
            {
                Transform pivot = _pivots[i];
                if (pivot == null)
                    continue;
                // Unchanged since our last write: no clip drives it, so pose it from rest.
                Quaternion from = pivot.localRotation == _written[i] ? _rest[i] : pivot.localRotation;
                Quaternion target = _rest[i] * Quaternion.Euler(_pose[i], 0f, 0f);
                pivot.localRotation = _written[i] = Quaternion.Slerp(from, target, weight);
            }
        }

        // Ease-out with a small overshoot: the knee lands, sinks a few centimetres and settles.
        static float Landing(float t)
        {
            const float s = 0.6f;
            float u = t - 1f;
            return 1f + (s + 1f) * u * u * u + s * u * u;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        // Eased between keys: each key is a stop, like a machine moving from pose to pose.
        static float Evaluate(float[] values, float t)
        {
            for (int k = 1; k < StandTimes.Length; k++)
            {
                if (t > StandTimes[k])
                    continue;
                float span = StandTimes[k] - StandTimes[k - 1];
                float u = span > 0f ? (t - StandTimes[k - 1]) / span : 1f;
                return Mathf.Lerp(values[k - 1], values[k], Smooth(u));
            }
            return values[values.Length - 1];
        }
    }
}
