using UnityEngine;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The Saboteur Bot's attack: a one-armed claw swipe, alternating arms. The arm rises with
    /// its pincer open while the body twists away from it (wind-up), then the body twists back
    /// and leans in, the robot rolls forward on its wheel and the arm slashes down with the
    /// pincer snapping shut (strike), then it rolls back (recovery). Timing and triggering are
    /// in <see cref="AgentMeleeMove"/>: it plays while the body is held in front of the player
    /// and whenever the body starts an attack (the Saboteur's AttackPlayer action requests one).
    /// </summary>
    /// <remarks>
    /// Curves are over normalised time and editable in the Inspector. Signs are for the
    /// right arm; the left arm mirrors the twist. The root is <c>SaboteurBot_Root</c>, which
    /// carries the wheel, so the whole robot rolls in and out. The angles are for the remade
    /// Saboteur (15 October), whose upper arms hang at its sides with the forearms pointing
    /// forward: the shoulder lifts the arm forward and up past horizontal (-90° is straight
    /// ahead). In the strike the upper arm is just above level (-105°) and the elbow points the
    /// forearm 45° up, so with the 14° lean the pincer ends about 1.2 m up: the player's chest,
    /// not their knees. The Saboteur swipes from 2 m (its brain's melee reach), where the front
    /// of the player's body (radius 0.4 m) is 1.6 m away; the 0.5 m roll-in takes the pincer
    /// tip there (measured in play: 1.43 m with the old 0.3 m roll, so it closed on air).
    /// </remarks>
    public sealed class AgentClawSwipe : AgentMeleeMove
    {
        [Header("Rig (Saboteur Bot pivots)")]
        [Tooltip("Body_Pivot: twists and leans into the swipe.")]
        [SerializeField] Transform body;
        [SerializeField] Transform shoulderLeft;
        [SerializeField] Transform elbowLeft;
        [SerializeField] Transform clawLeftTop;
        [SerializeField] Transform clawLeftBottom;
        [SerializeField] Transform shoulderRight;
        [SerializeField] Transform elbowRight;
        [SerializeField] Transform clawRightTop;
        [SerializeField] Transform clawRightBottom;

        [Tooltip("Damage one swipe deals to the player.")]
        [SerializeField, Min(0f)] float damage = 10f;

        [Header("Curves over normalised time")]
        [Tooltip("Root forward offset (m): rolls in for the strike, back after. 0.5 m puts the pincer tip on the front of the player's body when the Saboteur swipes from 2 m.")]
        [SerializeField] AnimationCurve roll = Keys((0f, 0f), (0.3f, -0.08f), (0.5f, 0.5f), (0.65f, 0.5f), (1f, 0f));
        [Tooltip("Body twist (degrees, + = towards the swinging arm): away in the wind-up, through in the strike.")]
        [SerializeField] AnimationCurve twist = Keys((0f, 0f), (0.3f, -25f), (0.5f, 20f), (0.65f, 15f), (1f, 0f));
        [Tooltip("Body lean (degrees, + = forward).")]
        [SerializeField] AnimationCurve lean = Keys((0f, 0f), (0.3f, -8f), (0.5f, 14f), (0.65f, 12f), (1f, 0f));
        [Tooltip("Swinging shoulder pitch (degrees, - = forward and up; -90 = straight ahead): raised high, then slashed down to the front.")]
        [SerializeField] AnimationCurve shoulder = Keys((0f, 0f), (0.3f, -140f), (0.5f, -105f), (0.65f, -95f), (1f, 0f));
        [Tooltip("Swinging elbow pitch (degrees, + = forearm down): cocked back in the wind-up, levelled in the strike.")]
        [SerializeField] AnimationCurve elbow = Keys((0f, 0f), (0.3f, -30f), (0.5f, 60f), (0.65f, 55f), (1f, 0f));
        [Tooltip("Pincer opening (degrees per jaw): open in the wind-up, snapped shut in the strike.")]
        [SerializeField] AnimationCurve pincer = Keys((0f, 0f), (0.3f, 35f), (0.48f, 35f), (0.52f, -5f), (0.7f, 0f), (1f, 0f));
        [Tooltip("Other shoulder pitch (degrees): pulled back for balance.")]
        [SerializeField] AnimationCurve otherShoulder = Keys((0f, 0f), (0.3f, 15f), (0.5f, -15f), (1f, 0f));

        bool _rightArm = true;

        /// <summary>True when the swipe in progress uses the right arm.</summary>
        public bool RightArm => _rightArm;

        /// <inheritdoc/>
        public override float Damage => damage;

        /// <inheritdoc/>
        protected override void OnStrikeStarted(int count) => _rightArm = count % 2 == 1;

        /// <inheritdoc/>
        protected override void RootOffset(float t, out Vector3 position, out Quaternion rotation)
        {
            position = new Vector3(0f, 0f, roll.Evaluate(t));
            rotation = Quaternion.identity;
        }

        /// <inheritdoc/>
        protected override void Pose(float t)
        {
            // Twisting towards the right arm turns the body clockwise seen from above (+Y).
            float side = _rightArm ? 1f : -1f;
            if (body != null)
                body.localRotation *= Quaternion.Euler(lean.Evaluate(t), side * twist.Evaluate(t), 0f);

            Transform swingShoulder = _rightArm ? shoulderRight : shoulderLeft;
            Transform swingElbow = _rightArm ? elbowRight : elbowLeft;
            Transform top = _rightArm ? clawRightTop : clawLeftTop;
            Transform bottom = _rightArm ? clawRightBottom : clawLeftBottom;
            Transform restShoulder = _rightArm ? shoulderLeft : shoulderRight;

            if (swingShoulder != null)
                swingShoulder.localRotation *= Quaternion.Euler(shoulder.Evaluate(t), 0f, 0f);
            if (swingElbow != null)
                swingElbow.localRotation *= Quaternion.Euler(elbow.Evaluate(t), 0f, 0f);
            float open = pincer.Evaluate(t);
            if (top != null)
                top.localRotation *= Quaternion.Euler(-open, 0f, 0f);
            if (bottom != null)
                bottom.localRotation *= Quaternion.Euler(open, 0f, 0f);
            if (restShoulder != null)
                restShoulder.localRotation *= Quaternion.Euler(otherShoulder.Evaluate(t), 0f, 0f);
        }
    }
}
