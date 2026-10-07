using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ToyFactory.Interaction;
using ToyFactory.Interfaces;

namespace ToyFactory.Player
{
    /// <summary>
    /// Lets the player use what they are looking at: on Interact it sweeps a small sphere
    /// from the camera and calls <see cref="IInteractable.Interact"/> on the nearest usable
    /// thing it touches.
    /// </summary>
    /// <remarks>
    /// A sphere, not a thin ray, so a small prop below eye level (a lever, a switch) does not
    /// need pixel-perfect aim. Whatever it finds must also be in plain sight: a second, thin
    /// ray to the touched point has to reach it without hitting something solid first, so
    /// nothing can be used through a wall or a cage.
    /// </remarks>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        static readonly IComparer<RaycastHit> ByDistance =
            Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        [Tooltip("Shared Input Actions asset. Must contain a 'Player' map with an Interact action.")]
        [SerializeField] InputActionAsset inputActions;

        [Tooltip("Where the sweep starts and points: the camera pivot.")]
        [SerializeField] Transform aim;

        [SerializeField, Min(0.1f)] float reach = 2.5f;

        [Tooltip("How far off the exact centre of the view a usable thing may be, in metres.")]
        [SerializeField, Min(0.01f)] float aimRadius = 0.35f;

        [Tooltip("Layers the sweep can hit.")]
        [SerializeField] LayerMask interactMask = Physics.DefaultRaycastLayers;

        readonly RaycastHit[] _hits = new RaycastHit[16];
        InputAction _interactAction;

        void Awake()
        {
            InputActionMap map = inputActions.FindActionMap("Player");
            _interactAction = map.FindAction("Interact");
            map.Enable();
        }

        void Update()
        {
            // The lookups below only run on the frame the key goes down, never every frame.
            if (!_interactAction.WasPressedThisFrame())
                return;
            if (GameClock.Current != null && GameClock.Current.State != GameState.Playing)
                return;

            Vector3 origin = aim.position;
            int count = Physics.SphereCastNonAlloc(origin, aimRadius, aim.forward, _hits, reach, interactMask,
                QueryTriggerInteraction.Collide);
            Array.Sort(_hits, 0, count, ByDistance);

            for (int i = 0; i < count; i++)
            {
                // Distance 0 means the sweep started inside the collider (standing in a
                // doorway's use zone): skipped, so a door is never closed on the player.
                if (_hits[i].distance <= 0f)
                    continue;

                IInteractable target = _hits[i].collider.GetComponentInParent<IInteractable>();
                if (target == null || !InPlainSight(origin, _hits[i].point, target))
                    continue;

                target.Interact();
                return;
            }
        }

        // True if a thin ray from the camera to the touched point reaches the target without
        // hitting something solid that belongs to anything else first.
        bool InPlainSight(Vector3 origin, Vector3 point, IInteractable target)
        {
            Vector3 toPoint = point - origin;
            float distance = toPoint.magnitude;
            if (distance <= Mathf.Epsilon)
                return true;

            if (!Physics.Raycast(origin, toPoint / distance, out RaycastHit blocker, distance - 0.01f,
                    interactMask, QueryTriggerInteraction.Ignore))
                return true;

            return ReferenceEquals(blocker.collider.GetComponentInParent<IInteractable>(), target);
        }
    }
}
