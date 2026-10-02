using System;
using UnityEngine;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A box the player can push around. Settling and moving are reported as events
    /// rather than written to the grid directly, so this stays decoupled from
    /// GridManager; whoever owns grid blocking subscribes to react.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PushableBox : MonoBehaviour
    {
        [Tooltip("Below this speed (m/s) the box is considered settled.")]
        [SerializeField, Min(0f)] float settleSpeed = 0.05f;

        Rigidbody _rb;
        Vector3 _pendingPush;
        bool _isSettled = true;

        /// <summary>Raised once, when the box's speed drops below the settle threshold.</summary>
        public event Action<PushableBox> OnBoxSettled;

        /// <summary>Raised once, when a settled box starts moving again.</summary>
        public event Action<PushableBox> OnBoxMoved;

        void Awake() => _rb = GetComponent<Rigidbody>();

        /// <summary>Queues a push force, applied on the next physics step.</summary>
        public void AddPush(Vector3 force) => _pendingPush += force;

        void FixedUpdate()
        {
            if (_pendingPush != Vector3.zero)
            {
                _rb.AddForce(_pendingPush, ForceMode.Force);
                _pendingPush = Vector3.zero;
            }

            bool settledNow = _rb.linearVelocity.sqrMagnitude < settleSpeed * settleSpeed;
            if (settledNow && !_isSettled)
            {
                _isSettled = true;
                OnBoxSettled?.Invoke(this);
            }
            else if (!settledNow && _isSettled)
            {
                _isSettled = false;
                OnBoxMoved?.Invoke(this);
            }
        }
    }
}
