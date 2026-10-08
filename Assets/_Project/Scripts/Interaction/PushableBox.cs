using System;
using UnityEngine;
using ToyFactory.Runtime.World;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A box the player can push around. While it rests it blocks the grid cells under it,
    /// so agents route round it and the Guard can use it as cover; while it moves it blocks
    /// nothing. Settling and moving are also raised as events.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PushableBox : MonoBehaviour
    {
        // Grid blocker owner ids: positive and unique per box (static props use negative ids).
        static int s_nextBlockerId;

        [Tooltip("Below this speed (m/s) the box is considered settled.")]
        [SerializeField, Min(0f)] float settleSpeed = 0.05f;

        Rigidbody _rb;
        Collider _collider;
        Vector3 _pendingPush;
        int _blockerId;

        /// <summary>True while the box is at rest.</summary>
        public bool IsSettled { get; private set; } = true;

        /// <summary>Raised once, when the box's speed drops below the settle threshold.</summary>
        public event Action<PushableBox> OnBoxSettled;

        /// <summary>Raised once, when a settled box starts moving again.</summary>
        public event Action<PushableBox> OnBoxMoved;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _collider = GetComponent<Collider>();
            _blockerId = ++s_nextBlockerId;
        }

        // A box placed in the scene starts at rest. The grid keeps the request if it is not built yet.
        void Start() => BlockGrid();

        void OnDisable() => GridManager.ClearBlocker(_blockerId);

        /// <summary>Queues a push force, applied on the next physics step.</summary>
        public void AddPush(Vector3 force) => _pendingPush += force;

        /// <summary>Moves the box somewhere else at once and leaves it at rest there.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            GridManager.ClearBlocker(_blockerId);

            _pendingPush = Vector3.zero;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);

            // The collider's bounds only follow the move once physics has been told about it.
            Physics.SyncTransforms();
            IsSettled = true;
            BlockGrid();
        }

        void FixedUpdate()
        {
            if (_pendingPush != Vector3.zero)
            {
                _rb.AddForce(_pendingPush, ForceMode.Force);
                _pendingPush = Vector3.zero;
            }

            bool settledNow = _rb.linearVelocity.sqrMagnitude < settleSpeed * settleSpeed;
            if (settledNow && !IsSettled)
            {
                IsSettled = true;
                BlockGrid();
                OnBoxSettled?.Invoke(this);
            }
            else if (!settledNow && IsSettled)
            {
                IsSettled = false;
                GridManager.ClearBlocker(_blockerId);
                OnBoxMoved?.Invoke(this);
            }
        }

        void BlockGrid()
        {
            if (_collider != null)
                GridManager.SetBlocker(_blockerId, _collider.bounds);
        }
    }
}
