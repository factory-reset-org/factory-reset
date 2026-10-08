using UnityEngine;
using UnityEngine.InputSystem;
using ToyFactory.Interaction;
using ToyFactory.Interfaces;

namespace ToyFactory.Player
{
    /// <summary>
    /// First-person player movement and look. Reads the Move, Look and Sprint actions
    /// from the shared Input Actions asset, moves a CharacterController with manual
    /// gravity, and exposes position, velocity and grid cell for later use on the
    /// shared world blackboard. Shooting, interacting and throwing are separate
    /// components; this one only moves and looks, and reports the player's health,
    /// battery and last shot to the agents from <see cref="PlayerHealth"/>,
    /// <see cref="PlayerBattery"/> and <see cref="PlayerBlaster"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour, IPlayerState
    {
        [Header("Input")]
        [Tooltip("Shared Input Actions asset. Must contain a 'Player' map with Move, Look and Sprint actions.")]
        [SerializeField] InputActionAsset inputActions;

        [Header("Movement")]
        [SerializeField, Min(0f)] float walkSpeed = 4f;
        [SerializeField, Min(0f)] float sprintSpeed = 7f;
        [SerializeField] float gravity = -9.81f;
        [SerializeField] float groundedStickVelocity = -2f;

        [Header("Look")]
        [Tooltip("Child transform the camera is attached to; pitches up/down while the body yaws left/right.")]
        [SerializeField] Transform cameraPivot;
        [SerializeField] float lookSensitivity = 0.1f;
        [SerializeField] float minPitch = -80f;
        [SerializeField] float maxPitch = 80f;

        [Header("Grid")]
        [Tooltip("Must match the shared grid's cell size (0.5 m).")]
        [SerializeField] float cellSize = 0.5f;

        [Header("Pushing")]
        [Tooltip("Force applied to a Pushable box per collision hit. Must clear the " +
                 "box's static friction (roughly mass * 9.81 * frictionCoefficient) " +
                 "or it will never start moving.")]
        [SerializeField] float pushForce = 400f;

        CharacterController _controller;
        PlayerHealth _health;
        PlayerBattery _battery;
        PlayerBlaster _blaster;
        InputAction _moveAction;
        InputAction _lookAction;
        InputAction _sprintAction;

        float _verticalVelocity;
        float _pitch;

        // What the player is standing on, and its belt if it is one. Looked up only when
        // the ground collider changes, never every frame.
        Collider _groundCollider;
        ConveyorBelt _groundBelt;

        /// <summary>Current world position, for the blackboard.</summary>
        public Vector3 Position => transform.position;

        /// <summary>Velocity applied this frame (horizontal plus vertical), for the blackboard.</summary>
        public Vector3 Velocity { get; private set; }

        public Vector3 Forward => Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        public float SprintSpeed => sprintSpeed;

        // Health, battery and blaster are their own components on the player. One that is
        // missing (a bare test player) reports as full, idle and unhurt.
        public bool IsAlive => _health == null || _health.IsAlive;
        public float HealthFraction => _health != null ? _health.Fraction : 1f;
        public float AmmoFraction => _battery != null ? _battery.Fraction : 1f;
        public bool IsReloading => _battery != null && _battery.IsReloading;
        public float OverchargeTimeLeft => _battery != null ? _battery.OverchargeTimeLeft : 0f;
        public float LastShotTime => _blaster != null ? _blaster.LastShotTime : -1f;

        public void TakeDamage(float amount, int sourceAgentId)
        {
            if (_health != null)
                _health.TakeDamage(amount, sourceAgentId);
        }

        /// <summary>Current grid cell, computed from position using the shared cell size.</summary>
        public Vector2Int Cell => new Vector2Int(
            Mathf.FloorToInt(transform.position.x / cellSize),
            Mathf.FloorToInt(transform.position.z / cellSize));

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<PlayerHealth>();
            _battery = GetComponent<PlayerBattery>();
            _blaster = GetComponent<PlayerBlaster>();

            InputActionMap map = inputActions.FindActionMap("Player");
            _moveAction = map.FindAction("Move");
            _lookAction = map.FindAction("Look");
            _sprintAction = map.FindAction("Sprint");
            map.Enable();

            SetCursorLocked(true);
            PlayerState.Publish(this);
        }

        void OnDestroy()
        {
            if (ReferenceEquals(PlayerState.Current, this))
                PlayerState.Publish(null);
        }

        void Update()
        {
            HandleCursorLock();

            // Title, cutscenes, pause and results all take the controls away.
            if (GameClock.Current != null && GameClock.Current.State != GameState.Playing)
            {
                Velocity = Vector3.zero;
                return;
            }

            Look();
            Move();
        }

        // Fires once per colliding hit during CharacterController.Move(). Pushes are
        // only queued here; PushableBox applies them on its own FixedUpdate so a box
        // never receives force outside the physics step.
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > 0.5f && hit.collider != _groundCollider)
            {
                _groundCollider = hit.collider;
                hit.collider.TryGetComponent(out _groundBelt);
            }

            if (!hit.collider.CompareTag("Pushable"))
                return;
            if (hit.moveDirection.y < -0.3f)
                return;

            PushableBox box = hit.collider.GetComponent<PushableBox>();
            if (box == null)
                return;

            Vector3 direction = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z).normalized;
            box.AddPush(direction * pushForce);
        }

        // Locks and hides the cursor so mouse movement only ever reports a look
        // delta, not an on-screen pointer. Escape frees it (to click other Unity
        // panels without stopping Play); clicking back into the view relocks it.
        void HandleCursorLock()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetCursorLocked(false);
            else if (Cursor.lockState != CursorLockMode.Locked
                     && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                SetCursorLocked(true);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Look()
        {
            Vector2 look = _lookAction.ReadValue<Vector2>();

            transform.Rotate(Vector3.up, look.x * lookSensitivity);

            _pitch = Mathf.Clamp(_pitch - look.y * lookSensitivity, minPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move()
        {
            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            float speed = _sprintAction.IsPressed() ? sprintSpeed : walkSpeed;

            Vector3 horizontal = (transform.right * moveInput.x + transform.forward * moveInput.y) * speed;

            // A running belt carries the player along with it.
            if (_controller.isGrounded && _groundBelt != null)
                horizontal += _groundBelt.Velocity;

            // CharacterController has no gravity of its own, so it is applied by hand.
            // isGrounded reflects the previous Move call.
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = groundedStickVelocity;
            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = horizontal;
            motion.y = _verticalVelocity;

            _controller.Move(motion * Time.deltaTime);
            Velocity = motion;
        }
    }
}
