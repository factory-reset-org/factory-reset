using System.Collections.Generic;
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

        [Header("Footsteps")]
        [Tooltip("Seconds between footstep noises while walking. The agents hear them (NoiseLoudness.Footsteps, 3.75 m) and the Tracker follows them.")]
        [SerializeField, Min(0.05f)] float walkStepInterval = 0.45f;
        [Tooltip("Seconds between footstep noises while sprinting (NoiseLoudness.SprintFootsteps, 9 m).")]
        [SerializeField, Min(0.05f)] float sprintStepInterval = 0.32f;
        [Tooltip("Moving slower than this (m/s) makes no footsteps.")]
        [SerializeField, Min(0f)] float silentBelowSpeed = 0.5f;

        // Every noise the player makes carries this source id (the NoiseEvent contract).
        const int PlayerSourceId = -1;

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

        // Footsteps are noise the Tracker can follow: walking carries a few metres, running more.
        // The prototype's step spacing: 0.45 s walking, 0.32 s running.
        const float WalkStepSeconds = 0.45f;
        const float RunStepSeconds = 0.32f;
        const float RunStepLoudness = 36f;
        const int PlayerNoiseSourceId = -1;
        float _stepTimer;

        // Slippery patches the player is standing in, and the walking velocity carried over
        // between frames so the player can slide on them.
        readonly List<SlipperyFloor> _slipperyFloors = new List<SlipperyFloor>();
        Vector3 _walkVelocity;
        float _nextStepIn;

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

            // The view's bob and shake. Added here so no prefab has to carry it.
            if (!TryGetComponent(out PlayerCameraEffects _))
                gameObject.AddComponent<PlayerCameraEffects>();

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
            // Title, cutscenes, pause and results all take the controls away.
            bool playing = GameClock.Current == null || GameClock.Current.State == GameState.Playing;
            HandleCursorLock(playing);
            HandleSoundToggle();

            if (!playing)
            {
                Velocity = Vector3.zero;
                return;
            }

            Look();
            Move();
        }

        // Domain reload is off, so a mute from the last session must not carry on.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSound() => AudioListener.volume = 1f;

        // M mutes and unmutes everything, as in the prototype.
        static void HandleSoundToggle()
        {
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
                AudioListener.volume = AudioListener.volume > 0f ? 0f : 1f;
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
        // delta, not an on-screen pointer. The pause menu frees it and locks it again;
        // if it is freed any other way, clicking back into the view relocks it, but only
        // while playing, so a click on the pause panel leaves the cursor free.
        void HandleCursorLock(bool playing)
        {
            if (playing && Cursor.lockState != CursorLockMode.Locked
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
            bool sprinting = _sprintAction.IsPressed();
            float speed = sprinting ? sprintSpeed : walkSpeed;

            Vector3 wanted = (transform.right * moveInput.x + transform.forward * moveInput.y) * speed;
            MakeFootsteps(moveInput.sqrMagnitude > 0.01f, sprinting);

            // On normal floor the player moves exactly as asked. On a slippery patch their
            // speed only eases towards it, so they slide on when they turn or let go.
            float traction = SlipperyTraction();
            _walkVelocity = traction > 0f
                ? Vector3.Lerp(_walkVelocity, wanted, 1f - Mathf.Exp(-traction * Time.deltaTime))
                : wanted;
            Vector3 horizontal = _walkVelocity;

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

            Footsteps(sprinting);
        }

        // Footsteps are noises the agents hear, so walking near the Tracker gives the player
        // away. Only the player's own walking counts: standing on a running belt is silent.
        // The first step sounds as soon as the player sets off.
        void Footsteps(bool sprinting)
        {
            float speed = new Vector2(_walkVelocity.x, _walkVelocity.z).magnitude;
            if (!_controller.isGrounded || speed < silentBelowSpeed)
            {
                _nextStepIn = 0f;
                return;
            }

            _nextStepIn -= Time.deltaTime;
            if (_nextStepIn > 0f)
                return;
            _nextStepIn = sprinting ? sprintStepInterval : walkStepInterval;
            float now = GameClock.Current != null ? GameClock.Current.GameTime : Time.time;
            float loudness = sprinting ? NoiseLoudness.SprintFootsteps : NoiseLoudness.Footsteps;
            NoiseEvents.Emit(new NoiseEvent(transform.position, loudness, PlayerSourceId, now));
        }

        // A step is a noise now and then while the player is moving on the ground.
        void MakeFootsteps(bool moving, bool sprinting)
        {
            if (!moving || !_controller.isGrounded)
            {
                _stepTimer = 0f;
                return;
            }

            _stepTimer -= Time.deltaTime;
            if (_stepTimer > 0f)
                return;

            _stepTimer = sprinting ? RunStepSeconds : WalkStepSeconds;
            float time = GameClock.Current != null ? GameClock.Current.GameTime : Time.time;
            float loudness = sprinting ? RunStepLoudness : NoiseLoudness.Footsteps;
            NoiseEvents.Emit(new NoiseEvent(transform.position, loudness, PlayerNoiseSourceId, time));
        }

        /// <summary>Called by a slippery patch when the player steps into it.</summary>
        public void EnterSlipperyFloor(SlipperyFloor floor)
        {
            if (!_slipperyFloors.Contains(floor))
                _slipperyFloors.Add(floor);
        }

        /// <summary>Called by a slippery patch when the player steps out of it.</summary>
        public void ExitSlipperyFloor(SlipperyFloor floor) => _slipperyFloors.Remove(floor);

        // The lowest traction of the patches the player is in, or 0 on normal floor. No exit
        // is reported when a patch, or the player's own collider, is switched off while the
        // player stands in it, so a patch that no longer holds the player is dropped here.
        float SlipperyTraction()
        {
            float lowest = 0f;
            for (int i = _slipperyFloors.Count - 1; i >= 0; i--)
            {
                SlipperyFloor floor = _slipperyFloors[i];
                if (floor == null || !floor.isActiveAndEnabled || !floor.Contains(transform.position))
                {
                    _slipperyFloors.RemoveAt(i);
                    continue;
                }
                if (lowest == 0f || floor.Traction < lowest)
                    lowest = floor.Traction;
            }
            return lowest;
        }
    }
}
