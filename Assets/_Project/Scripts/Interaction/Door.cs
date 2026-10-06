using System;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.World;

namespace ToyFactory.Interaction
{
    /// <summary>How the door's panel moves between closed and open.</summary>
    public enum DoorMode
    {
        Swing,
        Slide
    }

    /// <summary>
    /// A door that swings or slides open, stops and retries if something blocks its path
    /// instead of reversing, and raises <see cref="StateChanged"/> when it settles fully
    /// open or closed. <see cref="ISabotageable.Execute"/> closes it, for the Saboteur.
    /// The player toggles it with Interact. It tells the level grid when it stops being
    /// passable, so agents never plan a route through a closed door.
    /// </summary>
    public sealed class Door : MonoBehaviour, IDoor, ISabotageable, IInteractable
    {
        enum State
        {
            Closed,
            Opening,
            Open,
            Closing
        }

        [Tooltip("The panel that actually moves. Defaults to this object if left empty.")]
        [SerializeField] Transform movingPart;

        [SerializeField] DoorMode mode = DoorMode.Swing;

        [Tooltip("Degrees per second (Swing) or metres per second (Slide).")]
        [SerializeField, Min(0.01f)] float speed = 90f;

        [Tooltip("How far the panel swings open, in degrees around its local up axis.")]
        [SerializeField] float openAngle = 90f;

        [Tooltip("Local offset the panel slides to when fully open.")]
        [SerializeField] Vector3 openOffset = new Vector3(0f, 2f, 0f);

        [Header("Blocking")]
        [Tooltip("Something in this box, at the panel's current position, stops the door and makes it retry.")]
        [SerializeField] Vector3 blockCheckHalfExtents = new Vector3(0.5f, 1f, 0.1f);
        [SerializeField] LayerMask blockingMask;
        [SerializeField, Min(0.05f)] float retryInterval = 0.5f;

        [Header("Grid")]
        [Tooltip("The DoorwayMarker.DoorId of the doorway this door stands in. -1 = not linked to the grid.")]
        [SerializeField] int doorId = -1;

        State _state = State.Closed;
        Quaternion _closedRotation;
        Quaternion _openRotation;
        Vector3 _closedPosition;
        Vector3 _openPosition;
        float _retryTimer;

        /// <summary>Raised once the door settles fully open or fully closed.</summary>
        public event Action<Door> StateChanged;

        public bool IsOpen => _state == State.Open;

        void Awake()
        {
            if (movingPart == null)
                movingPart = transform;

            _closedRotation = movingPart.localRotation;
            _openRotation = _closedRotation * Quaternion.Euler(0f, openAngle, 0f);
            _closedPosition = movingPart.localPosition;
            _openPosition = _closedPosition + openOffset;
        }

        [ContextMenu("Open")]
        public void Open()
        {
            if (_state == State.Closed || _state == State.Closing)
                _state = State.Opening;
        }

        [ContextMenu("Close")]
        public void Close()
        {
            if (_state != State.Open && _state != State.Opening)
                return;

            // Blocked for agents from the moment it starts closing, not once it has shut.
            _state = State.Closing;
            SetGridClosed(true);
        }

        /// <summary>Player use: opens a closed door and closes an open one.</summary>
        public void Interact()
        {
            if (_state == State.Closed || _state == State.Closing)
                Open();
            else
                Close();
        }

        /// <summary>Saboteur action: close the door.</summary>
        void ISabotageable.Execute() => Close();

        void Update()
        {
            switch (_state)
            {
                case State.Opening:
                    Move(target: true, State.Open);
                    break;
                case State.Closing:
                    Move(target: false, State.Closed);
                    break;
            }
        }

        void Move(bool target, State settledState)
        {
            if (_retryTimer > 0f)
            {
                _retryTimer -= Time.deltaTime;
                return;
            }

            if (IsBlocked())
            {
                _retryTimer = retryInterval;
                return;
            }

            bool reached = mode == DoorMode.Swing
                ? MoveSwing(target)
                : MoveSlide(target);

            if (reached)
            {
                _state = settledState;
                if (settledState == State.Open)
                    SetGridClosed(false);   // passable only once fully open
                else
                    EmitSlam();
                StateChanged?.Invoke(this);
            }
        }

        bool MoveSwing(bool target)
        {
            Quaternion goal = target ? _openRotation : _closedRotation;
            movingPart.localRotation = Quaternion.RotateTowards(movingPart.localRotation, goal, speed * Time.deltaTime);
            return movingPart.localRotation == goal;
        }

        bool MoveSlide(bool target)
        {
            Vector3 goal = target ? _openPosition : _closedPosition;
            movingPart.localPosition = Vector3.MoveTowards(movingPart.localPosition, goal, speed * Time.deltaTime);
            return movingPart.localPosition == goal;
        }

        void SetGridClosed(bool closed)
        {
            if (doorId >= 0 && GridManager.Current != null)
                GridManager.SetDoorClosed(doorId, closed);
        }

        void EmitSlam()
        {
            float time = GameClock.Current != null ? GameClock.Current.GameTime : Time.time;
            // The object's hash keeps each door a distinct emitter: small ids belong to agents
            // and -1 means the player.
            NoiseEvents.Emit(new NoiseEvent(movingPart.position, NoiseLoudness.DoorSlam, GetHashCode(), time));
        }

        bool IsBlocked() =>
            Physics.CheckBox(movingPart.position, blockCheckHalfExtents, movingPart.rotation, blockingMask);
    }
}
