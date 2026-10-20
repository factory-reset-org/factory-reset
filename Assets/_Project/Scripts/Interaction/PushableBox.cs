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
        [Tooltip("Speed, in m/s, a box leaves at when the player slides it with Interact. With the default friction and damping, about 1.2 m of glide at 3.2, enough to carry it up a belt's edge ramp.")]
        [SerializeField, Min(0f)] float slideSpeed = 3.2f;

        [Tooltip("Friction between the box and what it rests on. Low enough that a shove glides before it stops. Applied at start unless the collider already has its own material.")]
        [SerializeField, Range(0.05f, 1f)] float slideFriction = 0.45f;

        [Tooltip("Air drag on the box while it slides, per second. Low, so a shove is stopped by friction and not by drag.")]
        [SerializeField, Min(0f)] float slideDamping = 0.3f;

        [Tooltip("How far, in metres, must be clear for a way to count as free when sliding.")]
        [SerializeField, Min(0.05f)] float slideClearance = 0.3f;

        [Tooltip("A box faster than this (m/s) is still sliding and ignores another shove. Above a belt's speed, so a box carried along a belt can still be shoved.")]
        [SerializeField, Min(0f)] float reshoveBelowSpeed = 2f;

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

            // A box stays upright: tipped over, a crate lay on its side with its model sunk into the floor.
            _rb.constraints |= RigidbodyConstraints.FreezeRotation;
            _rb.linearDamping = slideDamping;

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
            // A player standing on the box cannot shove it out from under their own feet.
            if (IsStoodOnBy(player.Position))
                return;

            // Away from the player along one of the four axes; if that way is blocked (a wall, another
            // box), the free way closest to it, so a box in a corner slides out along the wall.
            Vector3 away = transform.position - player.Position;
            if (!BoxShove.TryChoose(away, CanSlide, out Vector3 direction))
                return;

            _rb.linearVelocity = direction * slideSpeed;
        }

        /// <summary>True if <paramref name="feet"/> is on top of this box: at its top face and within its footprint.</summary>
        public bool IsStoodOnBy(Vector3 feet)
        {
            if (_collider == null)
                return false;
            Bounds b = _collider.bounds;
            return feet.y >= b.max.y - 0.15f
                && feet.x >= b.min.x - 0.3f && feet.x <= b.max.x + 0.3f
                && feet.z >= b.min.z - 0.3f && feet.z <= b.max.z + 0.3f;
        }

        // True if the box can move a little way along direction: nothing solid in its way. Shrunk a
        // touch so the floor and a wall it already rests against do not count as a hit.
        bool CanSlide(Vector3 direction)
        {
            if (_collider == null)
                return true;

            Bounds bounds = _collider.bounds;
            Vector3 half = bounds.extents - new Vector3(0.02f, 0.05f, 0.02f);
            int mask = LayerMask.GetMask("Default", "Environment", "Pushable", "TaskProp", "Agents");
            RaycastHit[] hits = Physics.BoxCastAll(bounds.center, half, direction, Quaternion.identity,
                slideClearance, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider != _collider && !hits[i].collider.isTrigger)
                    return false;
            }

            return true;
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
