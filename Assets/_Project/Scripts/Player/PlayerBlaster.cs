using System;
using UnityEngine;
using UnityEngine.InputSystem;
using ToyFactory.Interfaces;

namespace ToyFactory.Player
{
    /// <summary>
    /// The player's blaster. Holding Attack fires hitscan shots along the view; whatever
    /// the shot meets first takes a hit if it is an <see cref="IDamageable"/> (an agent, a
    /// spinning target, a power core). Every shot uses battery charge, shows a short tracer
    /// and is the loudest noise in the game, so the agents hear it.
    /// </summary>
    [RequireComponent(typeof(PlayerBattery))]
    public sealed class PlayerBlaster : MonoBehaviour
    {
        // NoiseEvent source id of the player; agents use their own ids, from 0 up.
        const int PlayerSourceId = -1;

        [Tooltip("Shared Input Actions asset. Must contain a 'Player' map with an Attack action.")]
        [SerializeField] InputActionAsset inputActions;

        [Tooltip("Where shots start and point: the camera pivot.")]
        [SerializeField] Transform aim;

        [SerializeField, Min(1f)] float range = 60f;

        [SerializeField, Min(0.5f)] float shotsPerSecond = 4f;

        [Tooltip("Layers a shot can hit. Left empty: level geometry, boxes, agents and task props.")]
        [SerializeField] LayerMask hitMask;

        [Header("Tracer")]
        [Tooltip("Where the tracer starts, in the aim's local space: the blaster's barrel, low and to the right.")]
        [SerializeField] Vector3 muzzleOffset = new Vector3(0.25f, -0.2f, 0.5f);
        [SerializeField] Color tracerColour = new Color(0.4f, 0.9f, 1f);
        [SerializeField, Min(0.01f)] float tracerSeconds = 0.05f;
        [SerializeField, Min(0.005f)] float tracerWidth = 0.04f;
        [Tooltip("Left empty, a plain unlit material is created.")]
        [SerializeField] Material lineMaterial;

        PlayerBattery _battery;
        InputAction _attackAction;
        LineRenderer _line;
        float _nextShotTime;
        float _tracerUntil;

        /// <summary>Game time of the latest shot, -1 before the first one.</summary>
        public float LastShotTime { get; private set; } = -1f;

        /// <summary>Raised for every shot fired, after its hit has been applied.</summary>
        public event Action OnFired;

        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        void Awake()
        {
            _battery = GetComponent<PlayerBattery>();

            InputActionMap map = inputActions.FindActionMap("Player");
            _attackAction = map.FindAction("Attack");
            map.Enable();

            if (hitMask.value == 0)
                hitMask = LayerMask.GetMask("Default", "Environment", "Pushable", "Agents", "TaskProp", "Throwable");

            // On its own object, on the Tracer layer, like the agents' shot lines.
            var lineObject = new GameObject("ShotLine");
            lineObject.transform.SetParent(transform, false);
            int tracerLayer = LayerMask.NameToLayer("Tracer");
            if (tracerLayer >= 0)
                lineObject.layer = tracerLayer;
            _line = lineObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.enabled = false;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.sharedMaterial = lineMaterial != null ? lineMaterial : new Material(Shader.Find("Sprites/Default"));
            _line.startColor = tracerColour;
            _line.endColor = tracerColour;
            _line.startWidth = tracerWidth;
            _line.endWidth = tracerWidth;
        }

        void Update()
        {
            if (_line.enabled && Now >= _tracerUntil)
                _line.enabled = false;

            if (GameClock.Current != null && GameClock.Current.State != GameState.Playing)
                return;

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                _battery.StartReload();

            // A free cursor means the click is for something else (it relocks the view).
            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            if (!_attackAction.IsPressed() || Now < _nextShotTime)
                return;

            if (_battery.TrySpendShot())
                Fire();
            else if (_battery.IsEmpty)
                _battery.StartReload();   // pulling the trigger on an empty battery reloads
        }

        void Fire()
        {
            float now = Now;
            _nextShotTime = now + 1f / shotsPerSecond;
            LastShotTime = now;

            Vector3 origin = aim.position;
            Vector3 direction = aim.forward;
            Vector3 end = origin + direction * range;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                hit.collider.GetComponentInParent<IDamageable>()?.TakeHit();
            }

            _line.SetPosition(0, aim.TransformPoint(muzzleOffset));
            _line.SetPosition(1, end);
            _line.enabled = true;
            _tracerUntil = now + tracerSeconds;

            NoiseEvents.Emit(new NoiseEvent(origin, NoiseLoudness.BlasterShot, PlayerSourceId, now));
            OnFired?.Invoke();
        }
    }
}
