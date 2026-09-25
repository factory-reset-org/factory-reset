using UnityEngine;
using UnityEngine.InputSystem;

namespace ToyFactory.Player
{
    /// <summary>
    /// First-person player movement and look. Reads the Move, Look and Sprint actions
    /// from the shared Input Actions asset, moves a CharacterController with manual
    /// gravity, and exposes position, velocity and grid cell for later use on the
    /// shared world blackboard. Shooting, interacting and throwing are separate
    /// components; this one only moves and looks.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
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

        CharacterController _controller;
        InputAction _moveAction;
        InputAction _lookAction;
        InputAction _sprintAction;

        float _verticalVelocity;
        float _pitch;

        /// <summary>Current world position, for the blackboard.</summary>
        public Vector3 Position => transform.position;

        /// <summary>Velocity applied this frame (horizontal plus vertical), for the blackboard.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Current grid cell, computed from position using the shared cell size.</summary>
        public Vector2Int Cell => new Vector2Int(
            Mathf.FloorToInt(transform.position.x / cellSize),
            Mathf.FloorToInt(transform.position.z / cellSize));

        void Awake()
        {
            _controller = GetComponent<CharacterController>();

            InputActionMap map = inputActions.FindActionMap("Player");
            _moveAction = map.FindAction("Move");
            _lookAction = map.FindAction("Look");
            _sprintAction = map.FindAction("Sprint");
            map.Enable();
        }

        void Update()
        {
            Look();
            Move();
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
