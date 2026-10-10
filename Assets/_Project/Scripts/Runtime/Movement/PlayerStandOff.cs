using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Movement
{
    /// <summary>
    /// Stops an agent's body a short way from the player instead of walking into them.
    /// A brain chasing the player plans its route onto the player's own cell, so without
    /// this the body drives into the player's capsule, is pushed back and drives in again,
    /// with the walk cycle playing on the spot. Inside <see cref="standOff"/> the body
    /// holds where it is and turns to face the player; it walks its route again once the
    /// player is beyond <see cref="release"/>.
    /// </summary>
    /// <remarks>
    /// Body only: the brain still plans and decides as before and never knows about the
    /// hold. The gap between the two distances stops the body flickering between walking
    /// and holding while the player edges back and forth at the limit.
    /// </remarks>
    [RequireComponent(typeof(AgentPathFollower), typeof(AgentController))]
    public sealed class PlayerStandOff : MonoBehaviour
    {
        [Tooltip("Ground distance (m) from the player at which the body stops and faces them.")]
        [SerializeField, Min(0.1f)] float standOff = 1.4f;

        [Tooltip("Ground distance (m) the player must back off to before the body walks on.")]
        [SerializeField, Min(0.1f)] float release = 1.7f;

        [Tooltip("The player only counts within this height difference (m), so a player on another level is ignored.")]
        [SerializeField, Min(0.1f)] float maxHeightDifference = 1.5f;

        AgentPathFollower _follower;
        AgentController _agent;

        /// <summary>True while the body is held facing the player.</summary>
        public bool IsHolding => _follower != null && _follower.IsHolding;

        /// <summary>Stand-off distance in metres.</summary>
        public float StandOffDistance => standOff;

        void Awake()
        {
            _follower = GetComponent<AgentPathFollower>();
            _agent = GetComponent<AgentController>();
            if (release < standOff)
                release = standOff;
        }

        void OnDisable()
        {
            if (_follower != null)
                _follower.Release();
        }

        void Update()
        {
            IPlayerState player = PlayerState.Current;
            // A brain busy with something else (the Tracker watching a thrown toy) walks on past
            // the player, so its body neither holds in front of them nor strikes.
            if (player == null || !player.IsAlive || _agent.IsFrozen || _agent.IsDisabled || _agent.IsDead ||
                _agent.IgnoresPlayer)
            {
                _follower.Release();
                return;
            }

            Vector3 offset = player.Position - transform.position;
            if (Mathf.Abs(offset.y) > maxHeightDifference)
            {
                _follower.Release();
                return;
            }

            offset.y = 0f;
            float limit = _follower.IsHolding ? release : standOff;
            if (offset.sqrMagnitude <= limit * limit)
                _follower.Hold(player.Position);
            else
                _follower.Release();
        }
    }
}
