using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ToyFactory.Interaction;
using ToyFactory.Interfaces;

namespace ToyFactory.Player
{
    /// <summary>
    /// Lets the player use what they are looking at: on Interact it casts a short ray from
    /// the camera and calls <see cref="IInteractable.Interact"/> on the nearest usable thing.
    /// </summary>
    /// <remarks>
    /// The ray passes through triggers that are not usable (room volumes, doorway markers)
    /// and stops at the first solid object, so nothing can be used through a wall.
    /// </remarks>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        static readonly IComparer<RaycastHit> ByDistance =
            Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        [Tooltip("Shared Input Actions asset. Must contain a 'Player' map with an Interact action.")]
        [SerializeField] InputActionAsset inputActions;

        [Tooltip("Where the ray starts and points: the camera pivot.")]
        [SerializeField] Transform aim;

        [SerializeField, Min(0.1f)] float reach = 2.5f;

        [Tooltip("Layers the ray can hit.")]
        [SerializeField] LayerMask interactMask = Physics.DefaultRaycastLayers;

        readonly RaycastHit[] _hits = new RaycastHit[8];
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

            int count = Physics.RaycastNonAlloc(aim.position, aim.forward, _hits, reach, interactMask,
                QueryTriggerInteraction.Collide);
            Array.Sort(_hits, 0, count, ByDistance);

            for (int i = 0; i < count; i++)
            {
                Collider hit = _hits[i].collider;
                IInteractable target = hit.GetComponentInParent<IInteractable>();
                if (target != null)
                {
                    target.Interact();
                    return;
                }
                if (!hit.isTrigger)
                    return;   // something solid is in the way
            }
        }
    }
}
