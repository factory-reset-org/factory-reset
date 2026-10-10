using UnityEngine;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The Tracker Toy's attack: a whole-body pounce. It crouches and rocks back (wind-up),
    /// hops forward nose first with a snap of the head (the bite), then hops back to where it
    /// stood. Ears flatten and the tail flicks up with it. A head-only bite was tried first
    /// and looked wrong with the rest of the toy frozen. Timing and triggering are in
    /// <see cref="AgentMeleeMove"/>.
    /// </summary>
    /// <remarks>
    /// Curves are over normalised time (0 to 1 over the duration), editable in the Inspector.
    /// Three phases: wind-up to 0.35, strike to 0.55, recovery to 1. The hop's arc and the
    /// crouch before it are what make it read as a pounce rather than a push. The root is
    /// <c>TrackerToy_Root</c>, which carries the wheels, so the whole toy hops.
    /// </remarks>
    public sealed class AgentBite : AgentMeleeMove
    {
        [Header("Rig (Tracker Toy pivots)")]
        [Tooltip("Body_Pivot: crouches during the wind-up.")]
        [SerializeField] Transform body;
        [Tooltip("Head_Pivot: snaps down in the bite.")]
        [SerializeField] Transform head;
        [SerializeField] Transform earLeft;
        [SerializeField] Transform earRight;
        [SerializeField] Transform tail;

        [Tooltip("Damage one pounce deals to the player: 8, just under the Saboteur's swipe (the prototype's bite dealt 9 a second). 0 = cosmetic. S1's call.")]
        [SerializeField, Min(0f)] float damage = 8f;

        [Header("Curves over normalised time")]
        [Tooltip("Root forward offset (m): back in the wind-up, forward in the strike.")]
        [SerializeField] AnimationCurve forward = Keys((0f, 0f), (0.35f, -0.1f), (0.55f, 0.35f), (0.7f, 0.35f), (1f, 0f));
        [Tooltip("Root height (m): the hop's arc during the strike.")]
        [SerializeField] AnimationCurve hop = Keys((0f, 0f), (0.35f, 0f), (0.45f, 0.16f), (0.55f, 0f), (0.8f, 0.05f), (1f, 0f));
        [Tooltip("Root pitch (degrees, + = nose down): rocks back, then dives nose first.")]
        [SerializeField] AnimationCurve pitch = Keys((0f, 0f), (0.35f, -12f), (0.55f, 16f), (0.7f, 10f), (1f, 0f));
        [Tooltip("Body crouch (m, + = down) during the wind-up.")]
        [SerializeField] AnimationCurve crouch = Keys((0f, 0f), (0.3f, 0.07f), (0.4f, 0f), (1f, 0f));
        [Tooltip("Head pitch (degrees, + = down): pulls back, then snaps down in the bite.")]
        [SerializeField] AnimationCurve headPitch = Keys((0f, 0f), (0.35f, -15f), (0.55f, 30f), (0.65f, 30f), (1f, 0f));
        [Tooltip("Ears back (degrees) through the pounce.")]
        [SerializeField] AnimationCurve earsBack = Keys((0f, 0f), (0.3f, -45f), (0.7f, -45f), (1f, 0f));
        [Tooltip("Tail up (degrees) in the wind-up, flicking down on landing.")]
        [SerializeField] AnimationCurve tailUp = Keys((0f, 0f), (0.35f, -40f), (0.6f, 15f), (1f, 0f));

        /// <inheritdoc/>
        public override float Damage => damage;

        /// <inheritdoc/>
        protected override void RootOffset(float t, out Vector3 position, out Quaternion rotation)
        {
            position = new Vector3(0f, hop.Evaluate(t), forward.Evaluate(t));
            rotation = Quaternion.Euler(pitch.Evaluate(t), 0f, 0f);
        }

        /// <inheritdoc/>
        protected override void Pose(float t)
        {
            if (body != null)
                body.localPosition += Vector3.down * crouch.Evaluate(t);
            if (head != null)
                head.localRotation *= Quaternion.Euler(headPitch.Evaluate(t), 0f, 0f);
            Quaternion ears = Quaternion.Euler(earsBack.Evaluate(t), 0f, 0f);
            if (earLeft != null)
                earLeft.localRotation *= ears;
            if (earRight != null)
                earRight.localRotation *= ears;
            if (tail != null)
                tail.localRotation *= Quaternion.Euler(tailUp.Evaluate(t), 0f, 0f);
        }
    }
}
