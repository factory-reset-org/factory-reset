using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Points the cannons at what they shoot: while the weapon aims and fires, each cannon arm
    /// turns at its shoulder, up or down and side to side, until its barrel points at the
    /// target's chest, so the shot leaves straight along the barrel. The Aim clip only brings
    /// the arm up level; the Captain's cannon is on its right forearm, 0.7 m off its centre
    /// line, so a level arm alone would shoot past a near player and over a short one.
    /// </summary>
    /// <remarks>
    /// <para>Added in LateUpdate on top of the pose the Animator has just written, like the
    /// recoil, and before it (execution order), so a kick starts from a barrel on target. The
    /// Animator writes the arms again next frame, so nothing builds up.</para>
    /// <para>The correction is measured, not assumed: it reads where the barrel points now
    /// (whatever the clip, the torso and the model do) and turns the arm by the smallest turn
    /// from that to the line to the target, three times, since turning the arm also moves the
    /// barrel's tip. It eases in and out with the aim and never turns an arm more than 35°.</para>
    /// </remarks>
    [DefaultExecutionOrder(-50)]   // before AgentShotRecoil adds its kick
    [RequireComponent(typeof(AgentWeapon))]
    public sealed class AgentCannonAim : MonoBehaviour
    {
        [Tooltip("The arm pivot holding each barrel (the shoulder), in the weapon's barrel order.")]
        [SerializeField] Transform[] arms = new Transform[0];

        [Tooltip("Largest correction in degrees: a target far above or below is not chased.")]
        [SerializeField, Range(0f, 60f)] float maxCorrection = 35f;

        [Tooltip("Seconds to ease the correction in and out.")]
        [SerializeField, Min(0.01f)] float blendSeconds = 0.1f;

        AgentWeapon _weapon;
        AgentController _agent;
        AgentAnimatorBridge _bridge;
        float _weight;

        /// <summary>How much of the correction is applied now, 0 to 1.</summary>
        public float Weight => _weight;

        void Awake()
        {
            _weapon = GetComponent<AgentWeapon>();
            _agent = GetComponent<AgentController>();
            _bridge = GetComponent<AgentAnimatorBridge>();
        }

        void OnDisable() => _weight = 0f;

        void LateUpdate()
        {
            bool down = _agent != null && (_agent.IsFrozen || _agent.IsDisabled || _agent.IsDead);
            bool hasTarget = _weapon.TryGetTarget(out Vector3 target);
            if (_bridge != null && _bridge.HasAimLayer)
            {
                // Follow the Aim layer exactly: the correction cancels as much of the clip's lift
                // as the Animator has put in this frame. A weight of its own faded out as the
                // tracer ended while the layer (a frame behind) still lifted the arms: 14° up.
                _weight = _bridge.AimWeight;
            }
            else
            {
                bool aiming = _weapon.IsBusy && hasTarget && !down;
                _weight = Mathf.MoveTowards(_weight, aiming ? 1f : 0f, Time.deltaTime / blendSeconds);
            }
            if (_weight <= 0f || !hasTarget || down)
                return;

            for (int i = 0; i < arms.Length && i < _weapon.BarrelCount; i++)
                if (arms[i] != null)
                    Correct(arms[i], i, target);
        }

        void Correct(Transform arm, int barrel, Vector3 target)
        {
            Quaternion pose = arm.localRotation;
            for (int pass = 0; pass < 3; pass++)
            {
                if (!_weapon.TryGetBarrel(barrel, out Vector3 tip, out Vector3 direction))
                {
                    arm.localRotation = pose;
                    return;
                }
                Vector3 to = target - tip;
                if (to.sqrMagnitude < 1e-6f)
                    break;
                arm.rotation = Quaternion.FromToRotation(direction, to) * arm.rotation;
            }

            // The whole turn from the clip's pose, capped, then eased in by the aim's weight.
            Quaternion turn = Quaternion.Inverse(pose) * arm.localRotation;
            float angle = Quaternion.Angle(Quaternion.identity, turn);
            if (angle > maxCorrection)
                turn = Quaternion.Slerp(Quaternion.identity, turn, maxCorrection / angle);
            arm.localRotation = pose * Quaternion.Slerp(Quaternion.identity, turn, _weight);
        }
    }
}
