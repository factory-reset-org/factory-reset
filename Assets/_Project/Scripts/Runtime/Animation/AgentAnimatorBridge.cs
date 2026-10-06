using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Feeds the agent's movement into its Animator every frame, so the Blend Trees pick the
    /// clip: <c>Speed</c> (m/s) blends idle, walk and run; <c>TurnRate</c> (degrees per
    /// second, positive = right) drives the lean layer; <c>IsAttacking</c> and <c>IsDead</c>
    /// switch the attack and dead poses. The brain never sees the Animator; it only moves
    /// the body, and this reads the result through <see cref="ToyFactory.Interfaces.IAgentState"/>.
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

        AgentController _agent;
        bool _hasSpeed, _hasTurnRate, _hasAttacking, _hasDead;

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
        }

        // LateUpdate: the path follower has moved and turned the body this frame already.
        void LateUpdate()
        {
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
        }
    }
}
