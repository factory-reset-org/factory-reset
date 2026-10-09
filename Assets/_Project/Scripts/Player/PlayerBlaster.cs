using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;
using ToyFactory.Runtime.Pooling;
using Random = UnityEngine.Random;

namespace ToyFactory.Player
{
    /// <summary>
    /// The player's blaster. Holding Attack fires hitscan shots along the view; whatever
    /// the shot meets first takes a hit if it is an <see cref="IDamageable"/> (an agent, a
    /// spinning target, a power core). Every shot uses battery charge, shows a glowing bolt
    /// from the gun and sparks where it lands, and is the loudest noise in the game, so the
    /// agents hear it. While overcharged it fires much faster, a little off line, in gold,
    /// and costs nothing.
    /// </summary>
    /// <remarks>
    /// The hit is decided and applied the moment the shot is fired. The bolt and the sparks
    /// are only the picture of it, taken from pools so firing makes no garbage.
    /// </remarks>
    [RequireComponent(typeof(PlayerBattery))]
    public sealed class PlayerBlaster : MonoBehaviour
    {
        // NoiseEvent source id of the player; agents use their own ids, from 0 up.
        const int PlayerSourceId = -1;

        // Most bolts and bursts that can be out at once with overcharge firing: about 13 shots a
        // second and a bolt lives for a third of a second at the longest range.
        const int BoltPoolSize = 12;

        // A wall this much past the barrel tip still leaves too short a streak to be worth drawing.
        const float BarrelClearance = 0.15f;
        const int ImpactPoolSize = 8;

        [Tooltip("Shared Input Actions asset. Must contain a 'Player' map with an Attack action.")]
        [SerializeField] InputActionAsset inputActions;

        [Tooltip("Where shots start and point: the camera pivot.")]
        [SerializeField] Transform aim;

        [SerializeField, Min(1f)] float range = 60f;

        [SerializeField, Min(0.5f)] float shotsPerSecond = 4f;

        [Tooltip("Layers a shot can hit. Left empty: level geometry, boxes, agents and task props.")]
        [SerializeField] LayerMask hitMask;

        [Header("Overcharge")]
        [SerializeField, Min(0.5f)] float overchargeShotsPerSecond = 13.3f;
        [Tooltip("Half-angle of the cone each overcharged shot is thrown into, in degrees.")]
        [SerializeField, Range(0f, 5f)] float overchargeSpreadDegrees = 1f;

        [Header("Look")]
        [Tooltip("The gun in view. Shots start at its muzzle and it kicks when they fire.")]
        [SerializeField] BlasterViewModel viewModel;
        [SerializeField] ShotBolt boltPrefab;
        [SerializeField] ImpactBurst impactPrefab;
        [SerializeField] Color tracerColour = new Color(0.4f, 0.9f, 1f);
        [SerializeField] Color overchargeColour = new Color(1f, 0.78f, 0.2f);

        PlayerBattery _battery;
        InputAction _attackAction;
        ObjectPool<ShotBolt> _bolts;
        ObjectPool<ImpactBurst> _impacts;
        Transform _effectsRoot;
        float _nextShotTime;

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

            // The bolts and sparks live in the world, not on the player, who moves away from
            // them. They sit in the player's scene so they go when it does.
            _effectsRoot = new GameObject("BlasterEffects").transform;
            SceneManager.MoveGameObjectToScene(_effectsRoot.gameObject, gameObject.scene);
            if (boltPrefab != null)
            {
                _bolts = new ObjectPool<ShotBolt>(boltPrefab, _effectsRoot, OnBoltCreated);
                _bolts.Prewarm(BoltPoolSize);
            }
            if (impactPrefab != null)
            {
                _impacts = new ObjectPool<ImpactBurst>(impactPrefab, _effectsRoot, OnImpactCreated);
                _impacts.Prewarm(ImpactPoolSize);
            }
        }

        void OnDestroy()
        {
            if (_effectsRoot != null)
                Destroy(_effectsRoot.gameObject);
        }

        void Update()
        {
            if (GameClock.Current != null && GameClock.Current.State != GameState.Playing)
                return;

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                _battery.StartReload();

            // A free cursor means the click is for something else (it relocks the view).
            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            if (_attackAction.IsPressed())
                TryShoot();
        }

        /// <summary>
        /// Fires if the blaster is ready: the time since the last shot has passed and there is
        /// charge (or overcharge). Pulling the trigger on an empty battery starts a reload.
        /// </summary>
        public bool TryShoot()
        {
            if (Now < _nextShotTime)
                return false;

            if (!_battery.TrySpendShot())
            {
                if (_battery.IsEmpty)
                    _battery.StartReload();
                return false;
            }

            Fire();
            return true;
        }

        void Fire()
        {
            float now = Now;
            bool overcharged = _battery.OverchargeTimeLeft > 0f;
            _nextShotTime = now + 1f / (overcharged ? overchargeShotsPerSecond : shotsPerSecond);
            LastShotTime = now;

            Vector3 origin = aim.position;
            Vector3 direction = overcharged ? Spread(aim, overchargeSpreadDegrees) : aim.forward;
            Vector3 end = origin + direction * range;
            Vector3 normal = -direction;
            bool hitSomething = false;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                normal = hit.normal;
                hitSomething = true;
                hit.collider.GetComponentInParent<IDamageable>()?.TakeHit();
            }

            Color colour = overcharged ? overchargeColour : tracerColour;
            Vector3 from = viewModel != null ? viewModel.Muzzle.position : origin + aim.forward * 0.5f;
            ShowShot(origin, direction, from, end, normal, hitSomething, colour);
            if (viewModel != null)
                viewModel.OnShot(colour);

            NoiseEvents.Emit(new NoiseEvent(origin, NoiseLoudness.BlasterShot, PlayerSourceId, now));
            OnFired?.Invoke();
        }

        // The gun is drawn on top of the world, so its barrel can be further forward than a wall the
        // player is standing against. A bolt from there would start behind the wall and show only
        // its far end. When the wall is closer than the barrel tip, the shot is just the sparks.
        void ShowShot(Vector3 origin, Vector3 direction, Vector3 from, Vector3 end, Vector3 normal, bool hitSomething,
            Color colour)
        {
            float barrelAhead = Vector3.Dot(from - origin, direction);
            float hitAt = Vector3.Dot(end - origin, direction);
            if (hitSomething && hitAt <= barrelAhead + BarrelClearance)
            {
                if (_impacts != null)
                    _impacts.Get().Play(end, normal, colour);
                return;
            }

            if (_bolts != null)
                _bolts.Get().Launch(from, end, colour, hitSomething, normal);
        }

        // A random direction inside a cone round the aim.
        static Vector3 Spread(Transform aim, float halfAngleDegrees)
        {
            Vector2 offset = Random.insideUnitCircle * Mathf.Tan(halfAngleDegrees * Mathf.Deg2Rad);
            return (aim.forward + aim.right * offset.x + aim.up * offset.y).normalized;
        }

        // The pools hand out the same few objects again and again, so each is wired up once.
        void OnBoltCreated(ShotBolt bolt)
        {
            bolt.Arrived += OnBoltArrived;
            bolt.Finished += ReleaseBolt;
        }

        void OnImpactCreated(ImpactBurst burst) => burst.Finished += ReleaseImpact;

        void OnBoltArrived(ShotBolt bolt)
        {
            if (bolt.HasImpact && _impacts != null)
                _impacts.Get().Play(bolt.ImpactPoint, bolt.ImpactNormal, bolt.Colour);
        }

        void ReleaseBolt(ShotBolt bolt) => _bolts.Release(bolt);

        void ReleaseImpact(ImpactBurst burst) => _impacts.Release(burst);
    }
}
