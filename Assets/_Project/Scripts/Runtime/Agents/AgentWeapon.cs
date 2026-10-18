using UnityEngine;
using UnityEngine.SceneManagement;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;
using ToyFactory.Runtime.Pooling;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Carries out a brain's <see cref="ToyFactory.AI.Core.AgentAction.Shoot"/>. A request
    /// starts a 0.3 s aim (the telegraph), then fires one hitscan ray at the player's chest. If
    /// the first thing the ray meets is the player, <see cref="IPlayerState.TakeDamage"/> is
    /// called; a wall or a box in between takes the shot instead. A flying energy bolt, the same
    /// effect as the player's blaster (<see cref="ShotBolt"/>), shows the shot either way, with a
    /// burst of sparks where it lands.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a telegraph:</b> hitscan cannot be dodged once fired, so the 0.3 s aim is the
    /// player's chance to step behind cover. It is the same for every agent, so the brains only
    /// decide when to shoot and how often, never the timing of the shot itself.</para>
    /// <para><b>No friendly fire:</b> the Agents layer is not in the hit mask, so another agent
    /// standing in the line of fire neither blocks nor takes the shot.</para>
    /// <para><b>A cannon pushed into cover:</b> the shot is traced from inside the body out to
    /// the muzzle before it leaves, so a cannon held out into a crate, a pillar or a wall hits
    /// that, rather than starting inside it and passing through.</para>
    /// <para><b>How it looks (17 October):</b> the shots were thin laser lines, a red aim line
    /// and a yellow tracer, which read as lasers rather than toy blasters. Now each robot fires a
    /// bolt in its own warm colour (the Captain red, the Guard orange), never the player's cyan,
    /// so enemy fire is told apart at a glance. The telegraph stays: the Captain's power cell
    /// charges (<c>CaptainCannon</c>), and a weapon with <see cref="chargeGlow"/> grows a glow at
    /// its muzzle through the aim, brighter and flickering faster, then flashes as it fires.
    /// Bolts and sparks come from small pools, so a shot makes no garbage. They are pictures
    /// only: the hit was decided, and the damage dealt, when the shot was fired.</para>
    /// <para><b>Time:</b> game time, like the controller, so a cutscene or the pause menu holds
    /// an aim where it is. A knock-out or scrap cancels it.</para>
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class AgentWeapon : MonoBehaviour
    {
        [Tooltip("Cannon meshes. Shots come from the front of each in turn.")]
        [SerializeField] Renderer[] barrels = new Renderer[0];

        [Tooltip("Optional, per barrel: the model's muzzle point. Shots leave from it, along the line from the barrel's centre to it. Without one, from the front of the barrel's longest side.")]
        [SerializeField] Transform[] muzzles = new Transform[0];

        [Tooltip("Seconds between the request and the shot: the telegraph.")]
        [SerializeField, Min(0f)] float aimSeconds = 0.3f;

        [Tooltip("The shot waits until the body faces within this many degrees of the target, so it never leaves sideways.")]
        [SerializeField, Range(1f, 90f)] float maxFireAngle = 25f;

        [Tooltip("Extra seconds the aim may wait to face the target; past that the shot is dropped.")]
        [SerializeField, Min(0f)] float maxTurnWait = 0.5f;

        [Tooltip("Damage per hit, passed to the player's TakeDamage.")]
        [SerializeField, Min(0f)] float damage = 10f;

        [Tooltip("Maximum shot distance in metres.")]
        [SerializeField, Min(1f)] float range = 20f;

        [Tooltip("Height above the player's feet that shots aim at: the chest by default. The Captain aims at the upper chest (1.45 m) so its ray comes at the first-person camera instead of dropping below it.")]
        [SerializeField, Min(0f)] float chestHeight = 1.2f;

        [Tooltip("Seconds the body holds its attack pose after a shot.")]
        [SerializeField, Min(0f)] float tracerSeconds = 0.08f;

        [Header("How a shot looks")]
        [Tooltip("The flying bolt (the player's blaster uses the same one).")]
        [SerializeField] ShotBolt boltPrefab;
        [Tooltip("The sparks where a bolt lands on something.")]
        [SerializeField] ImpactBurst impactPrefab;
        [Tooltip("This robot's shot colour: warm, so it is never mistaken for the player's cyan.")]
        [SerializeField] Color boltColour = new Color(1f, 0.5f, 0.15f, 1f);
        [Tooltip("Grow a glow at the muzzle through the aim (the telegraph) and flash it on firing. Off for a body with its own charge effect (the Captain's power cell).")]
        [SerializeField] bool chargeGlow = true;
        [Tooltip("Additive glow material for the charge (the bolt's glow). A plain sprite material is used without one.")]
        [SerializeField] Material chargeMaterial;
        [Tooltip("Width of the charge glow when fully charged (m).")]
        [SerializeField, Min(0.05f)] float chargeSize = 0.7f;
        [Tooltip("Width of the muzzle flash (m).")]
        [SerializeField, Min(0.05f)] float flashSize = 1f;
        [Tooltip("Seconds the muzzle flash lasts.")]
        [SerializeField, Min(0.01f)] float flashSeconds = 0.07f;

        const int PoolSize = 3;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        AgentController _agent;
        ToyFactory.Runtime.Movement.AgentPathFollower _follower;
        int _hitMask;
        Transform _effectsRoot;
        ObjectPool<ShotBolt> _bolts;
        ObjectPool<ImpactBurst> _impacts;
        Renderer _glow;
        MaterialPropertyBlock _glowBlock;
        float _flashUntil = -1f;
        int _nextBarrel;
        float _fireAt;
        float _tracerUntil;

        // True while the weapon is turning the body (aiming, and until the tracer fades). It
        // lets go once: stopping the facing every idle frame also cancelled the controller's
        // look target, so a standing agent did not turn to face where its brain asked.
        bool _ownsFacing;

        // Each barrel's long axis in its own space, pointing out of the muzzle: worked out once
        // from the rest pose (the longest side of the mesh; on a tie, the side nearest the
        // agent's facing), so the shot can leave along the barrel however the arm is posed.
        Vector3[] _barrelAxes = new Vector3[0];

        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        /// <summary>True while aiming (the telegraph); the shot follows when the aim time is up.</summary>
        public bool IsAiming { get; private set; }

        /// <summary>True while aiming or while the last shot's tracer is showing: the attack pose.</summary>
        public bool IsBusy => IsAiming || Now < _tracerUntil;

        /// <summary>Shots fired so far, for tests and the evidence log.</summary>
        public int ShotsFired { get; private set; }

        /// <summary>Shots that hit the player.</summary>
        public int Hits { get; private set; }

        /// <summary>True if the most recent shot hit the player.</summary>
        public bool LastShotHit { get; private set; }

        /// <summary>Index into the barrels of the most recent shot (agents with several alternate them).</summary>
        public int LastBarrel { get; private set; }

        /// <summary>Shots dropped because the body could not face the target in time.</summary>
        public int ShotsDropped { get; private set; }

        /// <summary>Where the most recent shot's bolt left from: the muzzle.</summary>
        public Vector3 LastShotFrom { get; private set; }

        /// <summary>Where the most recent shot ended: what it hit, or its full range.</summary>
        public Vector3 LastShotTo { get; private set; }

        /// <summary>Bolts launched so far (one per shot when the weapon has a bolt).</summary>
        public int BoltsLaunched { get; private set; }

        /// <summary>Bursts of sparks played where bolts landed.</summary>
        public int ImpactsPlayed { get; private set; }

        /// <summary>True while the muzzle glow shows: charging through the aim, or the flash.</summary>
        public bool ChargeGlowVisible => _glow != null && _glow.enabled;

        /// <summary>How big the muzzle glow is now (m), 0 when hidden.</summary>
        public float ChargeGlowSize => ChargeGlowVisible ? _glow.transform.localScale.x : 0f;

        /// <summary>Raised when a shot leaves the barrel, with the barrel's index.</summary>
        public event System.Action<int> Fired;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            _follower = GetComponent<ToyFactory.Runtime.Movement.AgentPathFollower>();

            // Everything solid blocks a shot except agents (no friendly fire). Layers that do
            // not exist in a project simply add nothing to the mask.
            _hitMask = LayerMask.GetMask("Default", "Environment", "Pushable", "Player", "TaskProp", "HeldItem", "Throwable");

            // Bolts and sparks live in the world, not on the body, which moves on; they are
            // kept in this agent's scene so they go with it.
            if (boltPrefab != null || impactPrefab != null)
            {
                _effectsRoot = new GameObject("ShotEffects (" + name + ")").transform;
                SceneManager.MoveGameObjectToScene(_effectsRoot.gameObject, gameObject.scene);
            }
            if (boltPrefab != null)
            {
                _bolts = new ObjectPool<ShotBolt>(boltPrefab, _effectsRoot, OnBoltCreated);
                _bolts.Prewarm(PoolSize);
            }
            if (impactPrefab != null)
            {
                _impacts = new ObjectPool<ImpactBurst>(impactPrefab, _effectsRoot, OnImpactCreated);
                _impacts.Prewarm(PoolSize);
            }
            if (chargeGlow)
                MakeChargeGlow();

            _barrelAxes = new Vector3[barrels.Length];
            for (int i = 0; i < barrels.Length; i++)
                _barrelAxes[i] = barrels[i] != null ? BarrelAxis(barrels[i], transform.forward) : Vector3.forward;
        }

        void OnDestroy()
        {
            if (_effectsRoot != null)
                Destroy(_effectsRoot.gameObject);
        }

        // A quad on the Tracer layer that faces the camera: round from every side, like the
        // bolt's own glow, and lit by nothing.
        void MakeChargeGlow()
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ChargeGlow";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            int tracerLayer = LayerMask.NameToLayer("Tracer");
            if (tracerLayer >= 0)
                quad.layer = tracerLayer;
            _glow = quad.GetComponent<Renderer>();
            _glow.sharedMaterial = chargeMaterial != null ? chargeMaterial : new Material(Shader.Find("Sprites/Default"));
            _glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _glow.receiveShadows = false;
            _glow.enabled = false;
            _glowBlock = new MaterialPropertyBlock();
        }

        // Charging: grows to full size through the aim, brighter and flickering faster.
        // Flashing: flash size for a moment after the shot. Otherwise hidden.
        void UpdateChargeGlow(bool aiming)
        {
            if (_glow == null)
                return;
            float now = Now;
            float size, brightness;
            if (aiming)
            {
                float charge = aimSeconds > 0f ? Mathf.Clamp01(1f - (_fireAt - now) / aimSeconds) : 1f;
                float flicker = 0.85f + 0.15f * Mathf.Sin(now * Mathf.Lerp(20f, 70f, charge));
                size = Mathf.Lerp(0.15f, 1f, charge) * chargeSize * flicker;
                brightness = Mathf.Lerp(1f, 3f, charge);
            }
            else if (now < _flashUntil)
            {
                size = flashSize;
                brightness = 4f;
            }
            else
            {
                _glow.enabled = false;
                return;
            }
            Transform glow = _glow.transform;
            glow.position = Muzzle();
            Camera camera = Camera.main;
            if (camera != null)
                glow.rotation = Quaternion.LookRotation(glow.position - camera.transform.position);
            glow.localScale = Vector3.one * size;
            Color tint = boltColour * brightness;
            tint.a = 1f;
            _glow.GetPropertyBlock(_glowBlock);
            _glowBlock.SetColor(BaseColorId, tint);
            _glowBlock.SetColor(ColorId, tint);
            _glow.SetPropertyBlock(_glowBlock);
            _glow.enabled = true;
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
            if (!bolt.HasImpact || _impacts == null)
                return;
            _impacts.Get().Play(bolt.ImpactPoint, bolt.ImpactNormal, bolt.Colour);
            ImpactsPlayed++;
        }

        void ReleaseBolt(ShotBolt bolt) => _bolts?.Release(bolt);

        void ReleaseImpact(ImpactBurst burst) => _impacts?.Release(burst);

        static Vector3 BarrelAxis(Renderer barrel, Vector3 facing)
        {
            Vector3 size = barrel.localBounds.size;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            Vector3 best = Vector3.forward;
            float bestAlign = -1f;
            foreach (Vector3 axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                float length = Mathf.Abs(Vector3.Dot(size, axis));
                if (length < longest * 0.95f)
                    continue;
                float align = Vector3.Dot(barrel.transform.TransformDirection(axis).normalized, facing);
                if (Mathf.Abs(align) > bestAlign)
                {
                    bestAlign = Mathf.Abs(align);
                    best = align >= 0f ? axis : -axis;
                }
            }
            return best;
        }

        /// <summary>Number of barrels; their order is the order shots alternate in.</summary>
        public int BarrelCount => barrels.Length;

        /// <summary>
        /// The tip of barrel <paramref name="index"/> and the direction it points, in world space,
        /// from the barrel's pose now. False if there is no such barrel.
        /// </summary>
        public bool TryGetBarrel(int index, out Vector3 tip, out Vector3 direction)
        {
            if (index < 0 || index >= barrels.Length || barrels[index] == null)
            {
                tip = default;
                direction = default;
                return false;
            }
            Renderer barrel = barrels[index];
            Transform muzzle = index < muzzles.Length ? muzzles[index] : null;
            if (muzzle != null)
            {
                // The model says where the muzzle is: the Captain's cannon hangs down at rest,
                // so its longest side would not tell which end shoots.
                tip = muzzle.position;
                direction = (tip - barrel.transform.position).normalized;
                return true;
            }
            Vector3 axis = _barrelAxes[index];
            Bounds local = barrel.localBounds;
            tip = barrel.transform.TransformPoint(local.center + Vector3.Scale(axis, local.extents));
            direction = barrel.transform.TransformDirection(axis).normalized;
            return true;
        }

        /// <summary>The point shots aim at: the live player's chest. False with no live player.</summary>
        public bool TryGetTarget(out Vector3 target)
        {
            IPlayerState player = LivePlayer();
            target = player != null ? player.Position + Vector3.up * chestHeight : default;
            return player != null;
        }

        /// <summary>Starts an aim if the weapon is free. Ignored while aiming or showing a tracer.</summary>
        public void RequestShot()
        {
            if (IsBusy || !_agent.isActiveAndEnabled)
                return;
            IsAiming = true;
            _fireAt = Now + aimSeconds;
        }

        /// <summary>Drops a pending aim without firing (knock-out, scrap).</summary>
        public void Cancel()
        {
            IsAiming = false;
            ReleaseFacing();
            _tracerUntil = 0f;
            _flashUntil = -1f;
            if (_glow != null)
                _glow.enabled = false;
        }

        void Update()
        {
            if (_agent.IsFrozen)
            {
                // Game time is stopped too, so the aim resumes where it was and its glow comes
                // back. Hide the glow meanwhile, or it would hang over the whole pause, cutscene
                // or Results screen.
                if (_glow != null)
                    _glow.enabled = false;
                return;
            }

            IPlayerState player = LivePlayer();
            if (IsAiming)
            {
                if (player == null)
                {
                    Cancel();
                    return;
                }
                // Turn to the target while aiming, even on the move, and only fire once
                // facing it: the shot leaves along the cannon, never out of its side.
                if (_follower != null)
                {
                    _follower.FaceTowards(player.Position);
                    _ownsFacing = true;
                }
                Vector3 from = Muzzle();
                Vector3 to = player.Position + Vector3.up * chestHeight;
                UpdateChargeGlow(true);
                if (Now >= _fireAt)
                {
                    bool facing = _follower == null || _follower.FacingErrorTo(player.Position) <= maxFireAngle;
                    if (facing && !LineClear(from, to, player))
                    {
                        // The brain judges sight from the player's eye to its cell; the real shot leaves the muzzle,
                        // lower and off the cell's centre, and low cover or a crate next to it can be in the way.
                        // Holding fire beats wasting the shot on the obstacle: the brain tries again next interval.
                        ShotsDropped++;
                        Cancel();
                    }
                    else if (facing)
                        Fire(from, to, player);
                    else if (Now >= _fireAt + maxTurnWait)
                    {
                        ShotsDropped++;
                        Cancel();
                    }
                }
                return;
            }

            if (Now >= _tracerUntil)
                ReleaseFacing();

            UpdateChargeGlow(false);
        }

        void ReleaseFacing()
        {
            if (_ownsFacing && _follower != null)
                _follower.StopFacing();
            _ownsFacing = false;
        }

        /// <summary>True if a shot from <paramref name="from"/> to <paramref name="to"/> would reach the player first.</summary>
        bool LineClear(Vector3 from, Vector3 to, IPlayerState player)
        {
            Vector3 body = new Vector3(transform.position.x, from.y, transform.position.z);
            if (Physics.Linecast(body, from, out RaycastHit inBody, _hitMask, QueryTriggerInteraction.Ignore))
                return false;   // the cannon is pushed into something
            Vector3 direction = to - from;
            float distance = Mathf.Min(direction.magnitude + 0.5f, range);
            if (!Physics.Raycast(from, direction.normalized, out RaycastHit hit, distance, _hitMask, QueryTriggerInteraction.Ignore))
                return true;
            var hitPlayer = hit.collider.GetComponentInParent<IPlayerState>();
            return hitPlayer != null && ReferenceEquals(hitPlayer, player);
        }

        void Fire(Vector3 from, Vector3 to, IPlayerState player)
        {
            IsAiming = false;
            ShotsFired++;

            Vector3 direction = to - from;
            float distance = Mathf.Min(direction.magnitude, range);
            direction.Normalize();

            Vector3 end = from + direction * distance;
            Vector3 normal = -direction;
            bool landed = false;
            LastShotHit = false;
            // The muzzle can be held out well past the body (the Captain's arm cannon reaches
            // 1.5 m in front), into a crate, a pillar or a wall. A ray that starts inside a
            // collider does not hit it, so the shot passed straight through. Trace from inside
            // the body, at the muzzle's height, out to the muzzle first: whatever the cannon is
            // pushed into takes the shot.
            Vector3 body = new Vector3(transform.position.x, from.y, transform.position.z);
            bool blocked = Physics.Linecast(body, from, out RaycastHit hit, _hitMask, QueryTriggerInteraction.Ignore);
            if (blocked || Physics.Raycast(from, direction, out hit, range, _hitMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                normal = hit.normal;
                landed = true;
                var hitPlayer = hit.collider.GetComponentInParent<IPlayerState>();
                if (hitPlayer != null && ReferenceEquals(hitPlayer, player))
                {
                    LastShotHit = true;
                    Hits++;
                    player.TakeDamage(damage, _agent.Identity.Id);
                }
            }

            LastShotFrom = from;
            LastShotTo = end;
            if (_bolts != null)
            {
                _bolts.Get().Launch(from, end, boltColour, landed, normal);
                BoltsLaunched++;
            }
            _flashUntil = Now + flashSeconds;
            _tracerUntil = Now + tracerSeconds;
            LastBarrel = barrels.Length > 0 ? _nextBarrel % barrels.Length : 0;
            _nextBarrel++;
            Fired?.Invoke(LastBarrel);
        }

        // The tip of the current barrel, along its own axis (so the shot leaves the barrel even
        // while the arm is raised or kicking); the agent's chest if it has none.
        Vector3 Muzzle()
        {
            if (barrels.Length == 0)
                return transform.position + Vector3.up * 1.2f + transform.forward * 0.5f;
            return TryGetBarrel(_nextBarrel % barrels.Length, out Vector3 tip, out _)
                ? tip
                : transform.position + Vector3.up * 1.2f;
        }

        static IPlayerState LivePlayer()
        {
            IPlayerState player = PlayerState.Current;
            // A destroyed MonoBehaviour is not C# null, so ask Unity as well.
            if (player == null || (player is Object unityObject && unityObject == null) || !player.IsAlive)
                return null;
            return player;
        }

    }
}
