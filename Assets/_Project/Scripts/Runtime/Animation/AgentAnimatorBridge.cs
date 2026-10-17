using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Feeds the agent's movement into its Animator every frame: <c>Speed</c> (m/s) and
    /// <c>TurnRate</c> (degrees per second, positive = right) drive the locomotion Blend Tree
    /// (idle, walk, run and the turning leans); <c>IsAttacking</c> raises the Aim layer and holds
    /// it through a fight; <c>IsDead</c> is set for the fall-apart pose. The brain never sees the
    /// Animator; it only moves the body, and this reads the result through <see cref="ToyFactory.Interfaces.IAgentState"/>.
    /// </summary>
    /// <remarks>
    /// <para>Parameters are found once by hash, so a controller may leave some out and nothing is
    /// looked up by name per frame. While the agent is frozen (cutscene, pause), knocked out
    /// or scrapped, Speed and TurnRate go to 0, so it settles into its idle pose instead of
    /// walking on the spot.</para>
    /// <para><b>The aim pose (17 October).</b> It used to follow the weapon exactly, in and out
    /// over 0.1 s: the arms came up for the 0.3 s aim and dropped right after each shot, so the
    /// Captain, firing every 1.2 s, snapped both arms up and down with every shot. Now the pose
    /// is held for <see cref="aimHoldSeconds"/> after the last shot (longer than the fire
    /// interval, so a fight keeps its guard up between shots), raised over 0.2 s and lowered over
    /// 0.6 s, both eased (smoothstep), so the arms settle rather than drop. Knocked out or frozen,
    /// it comes down at once.</para>
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

        [Tooltip("Seconds to raise the aim pose (eased).")]
        [SerializeField, Min(0.01f)] float aimRaiseTime = 0.2f;

        [Tooltip("Seconds to lower the aim pose after a fight (eased), so the arms settle down instead of dropping.")]
        [SerializeField, Min(0.01f)] float aimLowerTime = 0.6f;

        [Tooltip("Seconds the aim pose is held after the last shot. Longer than the fire interval (the Captain's 1.2 s), so the arms stay up between shots.")]
        [SerializeField, Min(0f)] float aimHoldSeconds = 1.5f;

        [Tooltip("Seconds to lower it when the agent is knocked out or frozen.")]
        [SerializeField, Min(0.01f)] float aimDropTime = 0.15f;

        AgentController _agent;
        bool _hasSpeed, _hasTurnRate, _hasAttacking, _hasDead;
        int _aimLayer = -1;
        float _aimBlend;      // linear progress, 0 to 1
        float _aimWeight;     // eased from it
        float _lastAttack = float.NegativeInfinity;

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
                if (!still && _agent.IsAttacking)
                    _lastAttack = Time.time;
                bool up = !still && Time.time - _lastAttack <= aimHoldSeconds;
                float seconds = up ? aimRaiseTime : still ? aimDropTime : aimLowerTime;
                _aimBlend = Mathf.MoveTowards(_aimBlend, up ? 1f : 0f, dt / seconds);
                _aimWeight = _aimBlend * _aimBlend * (3f - 2f * _aimBlend);   // smoothstep: no snap at either end
                animator.SetLayerWeight(_aimLayer, _aimWeight);
            }
        }
    }
}
