using System;
using UnityEngine;
using UnityEngine.SceneManagement;
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
    /// <remarks>
    /// A door with an unlock signal (the two Control Room doors) starts locked: the player
    /// cannot use it until a cutscene raises that signal, which unlocks it and opens it.
    /// </remarks>
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

        [Tooltip("Start fully open. Must match the doorway marker's Initially Closed setting, which is what the grid starts from.")]
        [SerializeField] bool startOpen;

        [Header("Lock")]
        [Tooltip("A CutsceneSignals id, e.g. ControlRoomUnlock. The door stays locked until a cutscene " +
                 "raises it, then opens. Empty: never locked.")]
        [SerializeField] string unlockSignal;

        State _state = State.Closed;
        Quaternion _closedRotation;
        Quaternion _openRotation;
        Vector3 _closedPosition;
        Vector3 _openPosition;
        float _retryTimer;

        /// <summary>Raised once the door settles fully open or fully closed.</summary>
        public event Action<Door> StateChanged;

        public bool IsOpen => _state == State.Open;

        /// <summary>True while the door is waiting for its unlock signal.</summary>
        public bool IsLocked { get; private set; }

        /// <summary>Raised when the player tries to use the door while it is locked.</summary>
        public event Action<Door> LockedUseAttempted;

        void Awake()
        {
            if (movingPart == null)
                movingPart = transform;

            _closedRotation = movingPart.localRotation;
            _openRotation = _closedRotation * Quaternion.Euler(0f, openAngle, 0f);
            _closedPosition = movingPart.localPosition;
            _openPosition = _closedPosition + openOffset;
            IsLocked = !string.IsNullOrEmpty(unlockSignal);
            CreateUseZone();

            // The panel is placed closed in the scene; a door that starts open jumps to its
            // open pose here. The grid already has it open, from the doorway marker.
            if (startOpen)
            {
                if (mode == DoorMode.Swing)
                    MoveSwing(target: true, maxStep: float.PositiveInfinity);
                else
                    MoveSlide(target: true, maxStep: float.PositiveInfinity);
                _state = State.Open;
            }
        }

        void OnEnable() => CutsceneEvents.OnCriticalSignal += HandleSignal;

        void OnDisable() => CutsceneEvents.OnCriticalSignal -= HandleSignal;

        void HandleSignal(string signalId)
        {
            if (!IsLocked || signalId != unlockSignal)
                return;

            IsLocked = false;
            Open();
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
            if (IsLocked)
            {
                LockedUseAttempted?.Invoke(this);
                return;
            }

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

            float step = speed * Time.deltaTime;
            bool reached = mode == DoorMode.Swing
                ? MoveSwing(target, step)
                : MoveSlide(target, step);

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

        bool MoveSwing(bool target, float maxStep)
        {
            Quaternion goal = target ? _openRotation : _closedRotation;
            movingPart.localRotation = Quaternion.RotateTowards(movingPart.localRotation, goal, maxStep);
            return movingPart.localRotation == goal;
        }

        bool MoveSlide(bool target, float maxStep)
        {
            Vector3 goal = target ? _openPosition : _closedPosition;
            movingPart.localPosition = Vector3.MoveTowards(movingPart.localPosition, goal, maxStep);
            return movingPart.localPosition == goal;
        }

        // A trigger the size of the closed panel, left in the doorway. Without it an open
        // door could not be closed: its panel has moved out of the player's reach.
        void CreateUseZone()
        {
            Transform panel = movingPart;
            var zone = new GameObject(name + "_UseZone");
            zone.layer = panel.gameObject.layer;
            zone.transform.SetPositionAndRotation(panel.position, panel.rotation);
            zone.transform.localScale = panel.lossyScale;

            // Parented to something that does not move with the panel.
            Transform anchor = panel == transform ? transform.parent : transform;
            if (anchor != null)
                zone.transform.SetParent(anchor, true);
            else
                SceneManager.MoveGameObjectToScene(zone, gameObject.scene);

            BoxCollider box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (panel.TryGetComponent(out BoxCollider panelCollider))
            {
                box.center = panelCollider.center;
                box.size = panelCollider.size;
            }

            zone.AddComponent<DoorUseZone>().Bind(this);
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
