using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Movement;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Base for a close-range attack move played by the body: the Tracker's pounce
    /// (<see cref="AgentBite"/>) and the Saboteur's claw swipe (<see cref="AgentClawSwipe"/>).
    /// A move starts when <see cref="PlayerStandOff"/> holds the body in front of the player
    /// (then again every <see cref="interval"/> seconds, start to start), and also whenever
    /// the body starts an attack of its own (<see cref="AgentController.IsAttacking"/>).
    /// Each move deals <see cref="Damage"/> once, at the moment of contact, if the player is
    /// still alive, within reach and in front; the brain never sees it. A move with no damage
    /// (the Tracker's pounce) is cosmetic.
    /// </summary>
    /// <remarks>
    /// The subclass poses the rig in <see cref="Pose"/>, from normalised time 0 to 1, in
    /// LateUpdate after the Animator has written the clip pose. Pivots the clips key are
    /// added to (the Animator resets them next frame); the model root, which no clip animates,
    /// is set from its rest pose here and never added to.
    /// The hit lands at <c>contactAt</c>, not when the move starts, so a player who backs off
    /// during the wind-up dodges it: the wind-up is the telegraph.
    /// </remarks>
    [RequireComponent(typeof(PlayerStandOff), typeof(AgentController))]
    public abstract class AgentMeleeMove : MonoBehaviour
    {
        [Tooltip("The model root (no clip animates it): moved from its rest pose during the move.")]
        [SerializeField] protected Transform root;

        [Tooltip("Seconds from the start of one move to the start of the next while held in front of the player.")]
        [SerializeField, Min(0.2f)] protected float interval = 1.3f;

        [Tooltip("Seconds one move takes.")]
        [SerializeField, Min(0.1f)] protected float duration = 0.6f;

        [Tooltip("When in the move (normalised time) the hit lands: the end of the wind-up, as the strike connects.")]
        [SerializeField, Range(0f, 1f)] protected float contactAt = 0.5f;

        [Tooltip("Metres beyond the stand-off distance the player may be and still be hit (the strike lunges forward).")]
        [SerializeField, Min(0f)] protected float reachMargin = 0.6f;

        const float MaxHeightDifference = 1.5f;

        PlayerStandOff _standOff;
        AgentController _agent;
        Vector3 _rootRestPosition;
        Quaternion _rootRestRotation;
        float _sinceStart = float.PositiveInfinity;
        float _time = -1f;
        bool _wasAttacking;
        bool _contactDone;

        /// <summary>Moves started since this body spawned (for tests and the debug overlay).</summary>
        public int Strikes { get; private set; }

        /// <summary>Moves that hit the player (for tests and the debug overlay).</summary>
        public int Hits { get; private set; }

        /// <summary>Damage one move deals to the player (Saboteur 10; the Tracker's pounce 0, cosmetic). 0 never hits.</summary>
        public abstract float Damage { get; }

        /// <summary>True while a move is playing.</summary>
        public bool IsStriking => _time >= 0f;

        /// <summary>How far through the current move it is, 0 to 1 (0 at rest).</summary>
        public float Progress => IsStriking ? Mathf.Clamp01(_time / duration) : 0f;

        protected virtual void Awake()
        {
            _standOff = GetComponent<PlayerStandOff>();
            _agent = GetComponent<AgentController>();
            if (root != null)
            {
                _rootRestPosition = root.localPosition;
                _rootRestRotation = root.localRotation;
            }
        }

        void OnDisable()
        {
            _time = -1f;
            if (root != null)
                root.SetLocalPositionAndRotation(_rootRestPosition, _rootRestRotation);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool able = !_agent.IsFrozen && !_agent.IsDisabled && !_agent.IsDead;
            bool held = able && _standOff.IsHolding;
            bool attackStarted = able && _agent.IsAttacking && !_wasAttacking;
            _wasAttacking = _agent.IsAttacking;

            if (held || IsStriking)
                _sinceStart += dt;

            if (IsStriking)
            {
                _time += dt;
                if (_time >= duration || !able)
                    _time = -1f;
            }
            else if (attackStarted || (held && _sinceStart >= interval))
            {
                _sinceStart = 0f;
                _time = 0f;
                _contactDone = false;
                Strikes++;
                OnStrikeStarted(Strikes);
            }

            if (IsStriking && !_contactDone && Progress >= contactAt)
            {
                _contactDone = true;
                TryHitPlayer();
            }
            else if (!held)
            {
                // The first move comes straight away the next time it reaches the player.
                _sinceStart = interval;
            }

            if (root != null)
            {
                Vector3 offset = Vector3.zero;
                Quaternion turn = Quaternion.identity;
                if (IsStriking)
                    RootOffset(Progress, out offset, out turn);
                root.SetLocalPositionAndRotation(_rootRestPosition + offset, _rootRestRotation * turn);
            }
            if (IsStriking)
                Pose(Progress);
        }

        // The player must be alive, within the stand-off distance plus the lunge, and in front
        // (the body faces the player while held; a move started for some other reason, from
        // too far or facing away, whiffs). Distance is flat, like the stand-off itself.
        void TryHitPlayer()
        {
            IPlayerState player = PlayerState.Current;
            if (player == null || !player.IsAlive || Damage <= 0f)
                return;

            Vector3 offset = player.Position - transform.position;
            if (Mathf.Abs(offset.y) > MaxHeightDifference)
                return;   // on a ledge or a box above or below, like the stand-off ignores
            offset.y = 0f;
            float reach = _standOff.StandOffDistance + reachMargin;
            if (offset.sqrMagnitude > reach * reach || Vector3.Dot(offset, transform.forward) <= 0f)
                return;

            Hits++;
            player.TakeDamage(Damage, _agent.Identity.Id);
        }

        /// <summary>Called when a move starts; <paramref name="count"/> is 1 for the first.</summary>
        protected virtual void OnStrikeStarted(int count) { }

        /// <summary>The model root's offset from rest at normalised time <paramref name="t"/>.</summary>
        protected abstract void RootOffset(float t, out Vector3 position, out Quaternion rotation);

        /// <summary>Adds the move to the clip-keyed pivots at normalised time <paramref name="t"/>.</summary>
        protected abstract void Pose(float t);

        /// <summary>A curve through the given (time, value) keys with smooth tangents.</summary>
        protected static AnimationCurve Keys(params (float time, float value)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (time, value) in keys)
                curve.AddKey(time, value);
            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0f);
            return curve;
        }
    }
}
