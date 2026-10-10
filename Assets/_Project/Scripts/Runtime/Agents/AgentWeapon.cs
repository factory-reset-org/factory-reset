using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Carries out a brain's <see cref="ToyFactory.AI.Core.AgentAction.Shoot"/>. A request
    /// starts a 0.3 s aim (the telegraph: a thin aim line from the cannon to the player), then
    /// fires one hitscan ray at the player's chest. If the first thing the ray meets is the
    /// player, <see cref="IPlayerState.TakeDamage"/> is called; a wall or a box in between
    /// takes the shot instead. A bright tracer shows the shot either way.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a telegraph:</b> hitscan cannot be dodged once fired, so the 0.3 s aim is the
    /// player's chance to step behind cover. It is the same for every agent, so the brains only
    /// decide when to shoot and how often, never the timing of the shot itself.</para>
    /// <para><b>No friendly fire:</b> the Agents layer is not in the hit mask, so another agent
    /// standing in the line of fire neither blocks nor takes the shot.</para>
    /// <para><b>Time:</b> game time, like the controller, so a cutscene or the pause menu holds
    /// an aim where it is. A knock-out or scrap cancels it.</para>
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class AgentWeapon : MonoBehaviour
    {
        [Tooltip("Cannon meshes. Shots come from the front of each in turn (the Captain alternates its two).")]
        [SerializeField] Renderer[] barrels = new Renderer[0];

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

        [Tooltip("Height of the player's chest above their feet, the point aimed at.")]
        [SerializeField, Min(0f)] float chestHeight = 1.2f;

        [Tooltip("Seconds the tracer stays on screen after a shot.")]
        [SerializeField, Min(0f)] float tracerSeconds = 0.08f;

        [Tooltip("Material for the aim line and tracer: unlit, using the line's vertex colour.")]
        [SerializeField] Material lineMaterial;

        [SerializeField] Color aimColour = new Color(1f, 0.25f, 0.2f, 0.6f);
        [SerializeField] Color tracerColour = new Color(1f, 0.9f, 0.4f, 1f);

        AgentController _agent;
        ToyFactory.Runtime.Movement.AgentPathFollower _follower;
        LineRenderer _line;
        int _hitMask;
        int _nextBarrel;
        float _fireAt;
        float _tracerUntil;

        // True while the weapon is turning the body (aiming, and until the tracer fades). It
        // lets go once: stopping the facing every idle frame also cancelled the controller's
        // look target, so a standing agent did not turn to face where its brain asked.
        bool _ownsFacing;

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

        /// <summary>Index into the barrels of the most recent shot (the Captain alternates 0 and 1).</summary>
        public int LastBarrel { get; private set; }

        /// <summary>Shots dropped because the body could not face the target in time.</summary>
        public int ShotsDropped { get; private set; }

        /// <summary>Raised when a shot leaves the barrel, with the barrel's index.</summary>
        public event System.Action<int> Fired;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            _follower = GetComponent<ToyFactory.Runtime.Movement.AgentPathFollower>();

            // Everything solid blocks a shot except agents (no friendly fire). Layers that do
            // not exist in a project simply add nothing to the mask.
            _hitMask = LayerMask.GetMask("Default", "Environment", "Pushable", "Player", "TaskProp", "HeldItem", "Throwable");

            // On its own child, on the Tracer layer, so cameras and lights can treat shots apart.
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
            if (_line != null)
                _line.enabled = false;
        }

        void Update()
        {
            if (_agent.IsFrozen)
            {
                // Game time is stopped too, so the aim resumes where it was and redraws its
                // line. Hide the line meanwhile: a tracer left showing stayed on screen for the
                // whole pause, cutscene or Results screen.
                if (_line != null)
                    _line.enabled = false;
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
                Show(from, to, aimColour, 0.02f);
                if (Now >= _fireAt)
                {
                    bool facing = _follower == null || _follower.FacingErrorTo(player.Position) <= maxFireAngle;
                    if (facing)
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

            if (_line.enabled && Now >= _tracerUntil)
                _line.enabled = false;
        }

        void ReleaseFacing()
        {
            if (_ownsFacing && _follower != null)
                _follower.StopFacing();
            _ownsFacing = false;
        }

        void Fire(Vector3 from, Vector3 to, IPlayerState player)
        {
            IsAiming = false;
            ShotsFired++;

            Vector3 direction = to - from;
            float distance = Mathf.Min(direction.magnitude, range);
            direction.Normalize();

            Vector3 end = from + direction * distance;
            LastShotHit = false;
            if (Physics.Raycast(from, direction, out RaycastHit hit, range, _hitMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var hitPlayer = hit.collider.GetComponentInParent<IPlayerState>();
                if (hitPlayer != null && ReferenceEquals(hitPlayer, player))
                {
                    LastShotHit = true;
                    Hits++;
                    player.TakeDamage(damage, _agent.Identity.Id);
                }
            }

            Show(from, end, tracerColour, 0.05f);
            _tracerUntil = Now + tracerSeconds;
            LastBarrel = barrels.Length > 0 ? _nextBarrel % barrels.Length : 0;
            _nextBarrel++;
            Fired?.Invoke(LastBarrel);
        }

        // The front of the current barrel, along the agent's facing; the agent's chest if it has none.
        Vector3 Muzzle()
        {
            if (barrels.Length == 0)
                return transform.position + Vector3.up * 1.2f + transform.forward * 0.5f;
            Renderer barrel = barrels[_nextBarrel % barrels.Length];
            if (barrel == null)
                return transform.position + Vector3.up * 1.2f;
            Bounds bounds = barrel.bounds;
            return bounds.center + transform.forward * Vector3.Dot(bounds.extents, Abs(transform.forward));
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static IPlayerState LivePlayer()
        {
            IPlayerState player = PlayerState.Current;
            // A destroyed MonoBehaviour is not C# null, so ask Unity as well.
            if (player == null || (player is Object unityObject && unityObject == null) || !player.IsAlive)
                return null;
            return player;
        }

        void Show(Vector3 from, Vector3 to, Color colour, float width)
        {
            _line.enabled = true;
            _line.startColor = colour;
            _line.endColor = colour;
            _line.startWidth = width;
            _line.endWidth = width;
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
        }
    }
}
