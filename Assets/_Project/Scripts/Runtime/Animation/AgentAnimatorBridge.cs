using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Feeds the agent's movement into its Animator every frame: <c>Speed</c> (m/s) and
    /// <c>TurnRate</c> (degrees per second, positive = right) drive the locomotion Blend Tree
    /// (idle, walk, run and the turning leans); <c>IsAttacking</c> fades the Aim layer in over
    /// 0.1 s; <c>IsDead</c> is set for the fall-apart pose. The brain never sees the Animator;
    /// it only moves the body, and this reads the result through <see cref="ToyFactory.Interfaces.IAgentState"/>.
    /// </summary>
    /// <remarks>
    /// Parameters are found once by hash, so a controller may leave some out and nothing is
    /// looked up by name per frame. While the agent is frozen (cutscene, pause), knocked out
    /// or scrapped, Speed and TurnRate go to 0, so it settles into its idle pose instead of
    /// walking on the spot.
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class AgentAnimatorBridge : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int TurnRateId = Animator.StringToHash("TurnRate");
        static readonly int IsAttackingId = Animator.StringToHash("IsAttacking");
        static readonly int IsDeadId = Animator.StringToHash("IsDead");

        [Tooltip("The Animator on the model child.")]
        [SerializeField] Animator animator;

        [Tooltip("Seconds to ease Speed and TurnRate towards a new value, so blends don't pop when the path follower changes speed.")]
        [SerializeField, Min(0f)] float dampTime = 0.1f;

        [Tooltip("Seconds to fade the Aim layer fully in or out when the agent starts or stops attacking.")]
        [SerializeField, Min(0.01f)] float aimBlendTime = 0.1f;

        AgentController _agent;
        bool _hasSpeed, _hasTurnRate, _hasAttacking, _hasDead;
        int _aimLayer = -1;
        float _aimWeight;

        /// <summary>True if the controller has an Aim layer (the Guard and the Captain) and is running.</summary>
        public bool HasAimLayer => enabled && _aimLayer >= 0;

        /// <summary>
        /// The Aim layer's weight, 0 to 1. Read before this component's LateUpdate, it is the
        /// weight the Animator built this frame's pose with (it is set for the next one).
        /// </summary>
        public float AimWeight => _aimWeight;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"{nameof(AgentAnimatorBridge)} on {name} has no Animator with a controller; animation is off.", this);
                enabled = false;
                return;
            }

            // Read once: Animator.parameters allocates a new array every call.
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == SpeedId) _hasSpeed = true;
                else if (parameter.nameHash == TurnRateId) _hasTurnRate = true;
                else if (parameter.nameHash == IsAttackingId) _hasAttacking = true;
                else if (parameter.nameHash == IsDeadId) _hasDead = true;
            }

            // The aim pose is an override layer whose weight is driven from code, so it can fade
            // and never fights the locomotion layer while the agent is not attacking.
            _aimLayer = animator.GetLayerIndex("Aim");
        }

        // LateUpdate: the path follower has moved and turned the body this frame already.
        void LateUpdate()
        {
            // Nothing to drive while the Animator is switched off.
            if (!animator.isActiveAndEnabled)
                return;

            bool still = _agent.IsFrozen || _agent.IsDisabled || _agent.IsDead;
            float dt = Time.deltaTime;

            if (_hasSpeed)
                animator.SetFloat(SpeedId, still ? 0f : _agent.Speed, dampTime, dt);
            if (_hasTurnRate)
                animator.SetFloat(TurnRateId, still ? 0f : _agent.TurnRate, dampTime, dt);
            if (_hasAttacking)
                animator.SetBool(IsAttackingId, !still && _agent.IsAttacking);
            if (_hasDead)
                animator.SetBool(IsDeadId, _agent.IsDead);

            if (_aimLayer >= 0)
            {
                float target = !still && _agent.IsAttacking ? 1f : 0f;
                _aimWeight = Mathf.MoveTowards(_aimWeight, target, dt / aimBlendTime);
                animator.SetLayerWeight(_aimLayer, _aimWeight);
            }
        }
    }
}
