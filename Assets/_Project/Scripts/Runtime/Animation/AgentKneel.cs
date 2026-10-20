using UnityEngine;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The Captain's knock-down: instead of tipping over it drops onto one knee, head bowed,
    /// its left fist resting on the front knee and its arm cannon planted muzzle-down on the
    /// floor; at the reboot it pushes off the knee and steps back up. The Captain's
    /// <see cref="AgentDownPose"/>, driven by <see cref="AgentKnockdown"/>; also its dormant
    /// pose before the Chapter 3 wake.
    /// </summary>
    /// <remarks>
    /// <para><b>The kneel</b> was measured on the model, not guessed: each contact was solved
    /// by bisection on the real meshes. The model root drops 0.492 m, until the back kneecap
    /// touches the floor (back hip +14°, knee +90°); the front knee then bends until the
    /// front boot, kept flat, touches it (hip −89°, knee +83°, ankle +6°); the back boot
    /// stands on its tip (ankle −86°). The cannon's muzzle stands 2 mm off the floor, the
    /// body leaning 5° onto it, and the fist rests on the outer front of the front knee, its
    /// arm clear of the torso. Every number is checked by the tests on the real body.</para>
    /// <para><b>Stepping back up</b> (1.2 s): it leans in and pushes on its knee with the fist
    /// while the cannon lifts off the floor (0 to 0.2), rises into a lunge with the back leg
    /// straightening (to 0.55), lifts the front boot and steps it back (to 0.9), and stands
    /// tall with its chest out before handing back to the Animator. Keys are eased in and out
    /// (smoothstep between keys), so each phase starts and stops cleanly, like a heavy
    /// machine.</para>
    /// <para><b>With the Animator:</b> applied in LateUpdate over the pose the Animator wrote
    /// that frame, blending from it while going down and back into it at the end of the
    /// stand-up, so there is no pop at either end. A pivot no clip writes is posed from its
    /// rest rotation instead.</para>
    /// </remarks>
    public sealed class AgentKneel : AgentDownPose
    {
        [Header("Rig (Captain pivots)")]
        [Tooltip("CaptainBot_Root: the model root that drops (no clip animates it).")]
        [SerializeField] Transform root;
        [Tooltip("Pelvis_Pivot: back to rest under the kneel (the walk bobs and turns it).")]
        [SerializeField] Transform pelvis;
        [Tooltip("Leg_L_Pivot: the front leg, boot planted.")]
        [SerializeField] Transform frontHip;
        [SerializeField] Transform frontKnee;
        [SerializeField] Transform frontAnkle;
        [Tooltip("Leg_R_Pivot: the back leg, knee on the floor.")]
        [SerializeField] Transform backHip;
        [SerializeField] Transform backKnee;
        [SerializeField] Transform backAnkle;
        [SerializeField] Transform waist;
        [SerializeField] Transform chest;
        [SerializeField] Transform neck;
        [SerializeField] Transform head;
        [Tooltip("Shoulder_L_Pivot: the fist arm, resting on the front knee.")]
        [SerializeField] Transform frontShoulder;
        [SerializeField] Transform frontElbow;
        [SerializeField] Transform frontWrist;
        [Tooltip("Clavicle_R_Pivot: the cannon arm's side, dropped a little to plant the cannon.")]
        [SerializeField] Transform backClavicle;
        [SerializeField] Transform backShoulder;
        [SerializeField] Transform backElbow;

        [Header("Timing")]
        [Tooltip("Seconds to drop onto the knee.")]
        [SerializeField, Min(0.05f)] float downSeconds = 0.55f;

        [Tooltip("Seconds to step back up at the reboot.")]
        [SerializeField, Min(0.1f)] float standSeconds = 1.2f;

        [Header("Walls (measured on the model)")]
        [Tooltip("Metres in front of the centre the model reaches standing (the cap's peak).")]
        [SerializeField] float standingReach = 0.5f;
        [Tooltip("Metres in front of the centre it reaches kneeling (the front boot).")]
        [SerializeField] float kneelingReach = 0.9f;

        // Joints, in the order of the tables below.
        const int FrontHip = 0, FrontKnee = 1, FrontAnkle = 2, BackHip = 3, BackKnee = 4, BackAnkle = 5,
            Waist = 6, Chest = 7, Neck = 8, Head = 9, FrontShoulder = 10, FrontElbow = 11, FrontWrist = 12,
            BackClavicle = 13, BackShoulder = 14, BackElbow = 15, Pelvis = 16, Joints = 17;

        /// <summary>Metres the model root drops onto the knee.</summary>
        public const float KneelDrop = 0.492f;

        static Vector3 X(float degrees) => new Vector3(degrees, 0f, 0f);

        // Kneeling: degrees (x, y, z) per joint, from rest.
        static readonly Vector3[] Kneeling =
        {
            X(-89f), X(83f), X(6f),                                     // front leg: boot flat
            X(14f), X(90f), X(-86f),                                    // back leg: kneecap and boot tip on the floor
            new Vector3(5f, 0f, -2f), new Vector3(3f, 0f, -3f),         // leaning onto the cannon side
            new Vector3(0f, 0f, 2.5f), X(22f),                          // head bowed, kept upright across
            new Vector3(-10f, 30f, -10f), X(-62f), X(10f),             // fist on the front knee
            new Vector3(0f, 0f, -1.8f), new Vector3(-4f, 0f, 10f), X(0f),   // cannon planted
            X(0f),                                                      // pelvis at rest
        };

        // Stepping back up, over normalised time: push, lunge, lift the front boot, step it back, stand tall.
        static readonly float[] StandTimes = { 0f, 0.2f, 0.55f, 0.7f, 0.9f, 1f };
        static readonly float[] StandingDrop = { KneelDrop, 0.47f, 0.08f, 0.01f, 0f, 0f };
        static readonly Vector3[][] Standing =
        {
            new[] { X(-89f), X(-89f), X(-35f), X(-30f), X(0f), X(0f) },             // front hip
            new[] { X(83f), X(83f), X(35f), X(50f), X(0f), X(0f) },                 // front knee: lifts the boot
            new[] { X(6f), X(6f), X(0f), X(-20f), X(0f), X(0f) },                   // front ankle
            new[] { X(14f), X(14f), X(15f), X(8f), X(0f), X(0f) },                  // back hip
            new[] { X(90f), X(90f), X(20f), X(10f), X(0f), X(0f) },                 // back knee: off the floor
            new[] { X(-86f), X(-86f), X(-35f), X(-15f), X(0f), X(0f) },             // back ankle: on its tip, then flat
            new[] { new Vector3(5f, 0f, -2f), X(12f), X(10f), X(4f), X(-2f), X(0f) },   // waist: leans in to push
            new[] { new Vector3(3f, 0f, -3f), X(8f), X(6f), X(2f), X(-3f), X(0f) },     // chest: out at the end
            new[] { new Vector3(0f, 0f, 2.5f), new Vector3(0f, 0f, 2f), X(0f), X(0f), X(0f), X(0f) },
            new[] { X(22f), X(20f), X(12f), X(6f), X(-4f), X(0f) },                 // head: comes up last
            new[] { new Vector3(-10f, 30f, -10f), new Vector3(-12f, 30f, -10f), new Vector3(-5f, 15f, -5f), X(0f), X(0f), X(0f) },
            new[] { X(-62f), X(-50f), X(-20f), X(-10f), X(0f), X(0f) },             // front elbow: pushes on the knee
            new[] { X(10f), X(15f), X(5f), X(0f), X(0f), X(0f) },
            new[] { new Vector3(0f, 0f, -1.8f), X(0f), X(0f), X(0f), X(0f), X(0f) },
            new[] { new Vector3(-4f, 0f, 10f), new Vector3(-10f, 0f, 8f), new Vector3(-15f, 0f, 6f), new Vector3(-8f, 0f, 4f), X(0f), X(0f) },
            new[] { X(0f), X(-10f), X(-30f), X(-15f), X(0f), X(0f) },               // the cannon swings up
            new[] { X(0f), X(0f), X(0f), X(0f), X(0f), X(0f) },                     // pelvis: at rest
        };

        // From this point of the stand-up the Animator's pose is blended back in.
        const float HandBackFrom = 0.85f;

        Joint[] _joints;
        Vector3 _rootRest;
        readonly Vector3[] _pose = new Vector3[Joints];
        float _drop;
        float _fit = 1f;   // how much of the forward lean fits in front
        bool _down;

        /// <inheritdoc/>
        public override float DownSeconds => downSeconds;

        /// <inheritdoc/>
        public override float StandSeconds => standSeconds;

        void Awake()
        {
            _joints = new[]
            {
                new Joint(frontHip), new Joint(frontKnee), new Joint(frontAnkle),
                new Joint(backHip), new Joint(backKnee), new Joint(backAnkle),
                new Joint(waist), new Joint(chest), new Joint(neck), new Joint(head),
                new Joint(frontShoulder), new Joint(frontElbow), new Joint(frontWrist),
                new Joint(backClavicle), new Joint(backShoulder), new Joint(backElbow),
                new Joint(pelvis),
            };
            if (root != null)
                _rootRest = root.localPosition;
        }

        /// <summary>Poses the drop onto the knee, <paramref name="seconds"/> after going down.</summary>
        public override void PoseDown(float seconds)
        {
            if (!_down)
            {
                _down = true;
                _fit = FitForward(standingReach, kneelingReach);
            }
            float t = Mathf.Clamp01(seconds / downSeconds);
            float joints = EaseOut(t);   // drops fast, settles
            _drop = KneelDrop * Landing(t);   // and lands a little heavy
            Kneeling.CopyTo(_pose, 0);
            Apply(joints);
        }

        /// <summary>Poses the stand-up, <paramref name="seconds"/> after the reboot.</summary>
        public override void PoseStandUp(float seconds)
        {
            _down = false;
            float t = Mathf.Clamp01(seconds / standSeconds);
            _drop = Evaluate(StandingDrop, t);
            for (int i = 0; i < Joints; i++)
                _pose[i] = Evaluate(Standing[i], t);
            float blend = t < HandBackFrom ? 1f : 1f - Smooth((t - HandBackFrom) / (1f - HandBackFrom));
            Apply(blend);
        }

        /// <summary>Standing: puts the root back; the pivots are the Animator's again.</summary>
        public override void Release()
        {
            Amount = 0f;
            _down = false;
            if (root != null)
                root.localPosition = _rootRest;
        }

        // Writes the pose over what the Animator wrote this frame, by weight.
        void Apply(float weight)
        {
            Amount = _drop / KneelDrop;
            if (root != null)
                root.localPosition = _rootRest + Vector3.down * _drop;

            for (int i = 0; i < Joints; i++)
            {
                Vector3 angles = _pose[i];
                // Against a wall the forward lean shrinks to fit (the bow only, not the legs).
                if (i == Waist || i == Chest || i == Head)
                    angles.x *= angles.x > 0f ? _fit : 1f;
                _joints[i].Pose(angles, Vector3.zero, weight);
            }
        }

        // Ease-out with a small overshoot: the knee lands, sinks a few centimetres and settles.
        static float Landing(float t)
        {
            const float s = 0.6f;
            float u = t - 1f;
            return 1f + (s + 1f) * u * u * u + s * u * u;
        }

        // Eased between keys: each key is a stop, like a machine moving from pose to pose.
        static int Key(float t, out float u)
        {
            for (int k = 1; k < StandTimes.Length; k++)
            {
                if (t > StandTimes[k])
                    continue;
                float span = StandTimes[k] - StandTimes[k - 1];
                u = Smooth(span > 0f ? (t - StandTimes[k - 1]) / span : 1f);
                return k;
            }
            u = 1f;
            return StandTimes.Length - 1;
        }

        static float Evaluate(float[] values, float t)
        {
            int k = Key(t, out float u);
            return Mathf.Lerp(values[k - 1], values[k], u);
        }

        static Vector3 Evaluate(Vector3[] values, float t)
        {
            int k = Key(t, out float u);
            return Vector3.Lerp(values[k - 1], values[k], u);
        }
    }
}
