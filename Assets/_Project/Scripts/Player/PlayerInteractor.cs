using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ToyFactory.Interaction;
using ToyFactory.Interfaces;
using ToyFactory.UI;

namespace ToyFactory.Player
{
    /// <summary>
    /// Lets the player use what they are looking at: on Interact it sweeps a small sphere
    /// from the camera and calls <see cref="IInteractable.Interact"/> on the nearest usable
    /// thing it touches. If that thing is an <see cref="IHoldable"/>, it goes on being held for
    /// as long as the key stays down and the player stays in reach.
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

        [Tooltip("How close the player must be before the prompt line shows, in metres. Using a prop still works out to Reach.")]
        [SerializeField, Min(0.1f)] float promptReach = 1.8f;

        [Tooltip("How far off the exact centre of the view a usable thing may be, in metres.")]
        [SerializeField, Min(0.01f)] float aimRadius = 0.35f;

        [Tooltip("Layers the sweep can hit.")]
        [SerializeField] LayerMask interactMask = Physics.DefaultRaycastLayers;

        readonly RaycastHit[] _hits = new RaycastHit[16];
        InputAction _interactAction;

        // What is being held, and the collider the sweep found it by.
        string _promptShown = string.Empty;
        IHoldable _held;
        Collider _heldCollider;
        PlayerBattery _battery;

        void Awake()
        {
            _battery = GetComponent<PlayerBattery>();
            InputActionMap map = inputActions.FindActionMap("Player");
            _interactAction = map.FindAction("Interact");
            map.Enable();
        }

        void Update()
        {
            if (GameClock.Current != null && GameClock.Current.State != GameState.Playing)
            {
                _held = null;
                ClearPrompt();
                return;
            }

            // The lookups in Press only run on the frame the key goes down, never every frame.
            if (_interactAction.WasPressedThisFrame())
                Press();

            ShowPrompt();

            if (_held == null)
                return;
            if (_interactAction.IsPressed() && InReach(_heldCollider))
                _held.Hold(Time.deltaTime);
            else
                _held = null;
        }

        void Press()
        {
            _held = null;

            IInteractable target = FindTarget(out Collider found, out _);
            if (target == null)
                return;

            target.Interact();
            _held = target as IHoldable;
            _heldCollider = found;
        }

        // The nearest usable thing the sweep touches that is also in plain sight, or null.
        IInteractable FindTarget(out Collider found, out float distance)
        {
            found = null;
            distance = 0f;
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

                found = _hits[i].collider;
                distance = _hits[i].distance;
                return target;
            }

            return null;
        }

        void ClearPrompt()
        {
            HudPresenter hud = HudPresenter.Current;
            if (hud == null || _promptShown.Length == 0)
                return;
            _promptShown = string.Empty;
            hud.SetPrompt(string.Empty);
        }

        // The prompt line: what is in reach and usable right now, with the key it needs. Looked up
        // each frame the player is in play and not already holding something; it clears on a
        // pause, a cutscene and the title screen.
        void ShowPrompt()
        {
            HudPresenter hud = HudPresenter.Current;
            if (hud == null)
                return;

            string text = string.Empty;
            if (_held == null)
            {
                IInteractable target = FindTarget(out Collider _, out float distance);
                // A prop whose prompt is null has nothing to offer right now (a trap that is not armed).
                bool offers = !(target is IInteractionPrompt silent) || silent.PromptText != null;
                if (target != null && distance <= promptReach && offers)
                {
                    string what = target is IInteractionPrompt p && !string.IsNullOrEmpty(p.PromptText) ? p.PromptText : "use it";
                    text = target is IHoldable ? $"Hold E to {what}" : $"Press E to {what}";
                }
            }

            // With nothing to use in front of it, an empty blaster says how to get going again: it does not reload
            // by itself.
            if (text.Length == 0 && _battery != null && _battery.IsEmpty && !_battery.IsReloading
                && _battery.OverchargeTimeLeft <= 0f)
            {
                text = _battery.SpareCells > 0
                    ? $"Press R to reload ({_battery.SpareCells} spare {(_battery.SpareCells == 1 ? "cell" : "cells")})"
                    : "Out of cells: find a battery or the charger";
            }

            // Only what this script showed is ever cleared, so a prompt fed by something else stays.
            if (text == _promptShown)
                return;
            _promptShown = text;
            hud.SetPrompt(text);
        }

        // A hold only needs the player to stay next to the prop, not to keep aiming at it.
        bool InReach(Collider held)
        {
            if (held == null || !held.enabled || !held.gameObject.activeInHierarchy)
                return false;

            Vector3 origin = aim.position;
            float limit = reach + aimRadius;
            return (held.ClosestPoint(origin) - origin).sqrMagnitude <= limit * limit;
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
