using System;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;
using ToyFactory.Runtime.World;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A box the player can push around. While it rests it blocks the grid cells under it,
    /// so agents route round it and the Guard can use it as cover; while it moves it blocks
    /// nothing. Settling and moving are also raised as events. The player can also slide it:
    /// pressing Interact on it gives it a shove away from the player, and it glides to a stop.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PushableBox : MonoBehaviour, IInteractable, IInteractionPrompt
    {
        // Grid blocker owner ids: positive and unique per box (static props use negative ids).
        static int s_nextBlockerId;

        [Tooltip("Below this speed (m/s) the box is considered settled.")]
        [SerializeField, Min(0f)] float settleSpeed = 0.05f;

        [Header("Sliding")]
        [Tooltip("Speed, in m/s, a box leaves at when the player slides it with Interact. About 1.3 m of glide at 5 with the default friction.")]
        [SerializeField, Min(0f)] float slideSpeed = 5f;

        [Tooltip("Friction between the box and what it rests on. Low enough that a shove glides before it stops. Applied at start unless the collider already has its own material.")]
        [SerializeField, Range(0.05f, 1f)] float slideFriction = 0.35f;

        [Tooltip("A box faster than this (m/s) is still sliding and ignores another shove.")]
        [SerializeField, Min(0f)] float reshoveBelowSpeed = 1f;

        Rigidbody _rb;
        Collider _collider;
        Vector3 _pendingPush;
        int _blockerId;
        float _lastSoundAt = -10f;

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

            if (_collider != null && _collider.sharedMaterial == null)
            {
                // Minimum, so the box glides on a floor whose own friction is higher.
                _collider.sharedMaterial = new PhysicsMaterial("Box slide")
                {
                    dynamicFriction = slideFriction,
                    staticFriction = slideFriction,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }
        }

        /// <summary>The prompt line while the player looks at the box.</summary>
        public string PromptText => "slide the box";

        /// <summary>
        /// Player use: a shove away from the player along the floor. A box that is still sliding
        /// ignores it, so it cannot be kicked faster by pressing again.
        /// </summary>
        public void Interact()
        {
            IPlayerState player = PlayerState.Current;
            if (player == null || _rb == null)
                return;
            if (_rb.linearVelocity.sqrMagnitude > reshoveBelowSpeed * reshoveBelowSpeed)
                return;

            Vector3 away = transform.position - player.Position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f)
                return;

            _rb.linearVelocity = away.normalized * slideSpeed;
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

                // A scrape as it starts to slide, not one for every nudge.
                if (Time.time - _lastSoundAt > 0.4f)
                {
                    _lastSoundAt = Time.time;
                    GameSfx.Play(Sfx.Box, 0.8f);
                }
            }
        }

        void BlockGrid()
        {
            if (_collider != null)
                GridManager.SetBlocker(_blockerId, _collider.bounds);
        }
    }
}
