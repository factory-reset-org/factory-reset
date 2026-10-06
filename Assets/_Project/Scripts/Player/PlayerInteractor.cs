using UnityEngine;
using UnityEngine.InputSystem;
using ToyFactory.Interaction;
using ToyFactory.Interfaces;

namespace ToyFactory.Player
{
    /// <summary>
    /// Lets the player use what they are looking at: on Interact it casts a short ray from
    /// the camera and calls <see cref="IInteractable.Interact"/> on what it hits.
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [Tooltip("Shared Input Actions asset. Must contain a 'Player' map with an Interact action.")]
        [SerializeField] InputActionAsset inputActions;

        [Tooltip("Where the ray starts and points: the camera pivot.")]
        [SerializeField] Transform aim;

        [SerializeField, Min(0.1f)] float reach = 2.5f;

        [Tooltip("Layers the ray can hit. Anything solid in front of an interactable blocks it.")]
        [SerializeField] LayerMask interactMask = Physics.DefaultRaycastLayers;

        InputAction _interactAction;

        void Awake()
        {
            InputActionMap map = inputActions.FindActionMap("Player");
            _interactAction = map.FindAction("Interact");
            map.Enable();
        }

        void Update()
        {
            // The lookup below only runs on the frame the key goes down, never every frame.
            if (!_interactAction.WasPressedThisFrame())
                return;
            if (GameClock.Current != null && GameClock.Current.State != GameState.Playing)
                return;
            if (!Physics.Raycast(aim.position, aim.forward, out RaycastHit hit, reach, interactMask,
                    QueryTriggerInteraction.Ignore))
                return;

            IInteractable target = hit.collider.GetComponentInParent<IInteractable>();
            if (target != null)
                target.Interact();
        }
    }
}
