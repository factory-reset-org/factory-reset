using System;
using System.Collections.Generic;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Offers AttackPlayer when the Saboteur sees the player (its <see cref="SaboteurAwareness"/>: the
    /// forward cone, the short all-round range, or a player it is already tracking, always with a line of
    /// sight) and they are within <see cref="Range"/> metres. Two considerations: <c>1 - d / Range</c>, so the
    /// closer the player the more the Saboteur wants to attack, and its own health fraction, so a damaged
    /// Saboteur attacks less. The squad layer (attack saturation) is applied afterwards by the brain, so this
    /// source looks at nothing but its own instance.
    /// </summary>
    /// <remarks>
    /// <para><b>Holding on.</b> Once it has offered an attack, it keeps offering it for a player who backs
    /// off up to <see cref="KeepRange"/>, or drops out of sight for less than
    /// <see cref="SaboteurAwareness.TrackGraceSeconds"/>, scoring the distance against the longer range. A
    /// player stepping in and out of 8 m therefore does not flip the Saboteur between attacking and patrolling
    /// every quarter second.</para>
    /// <para>Own health arrives through <see cref="IHealthAware"/>: the controller tells the brain, and the
    /// brain passes it on to the sources that want it. It starts at full health.</para>
    /// </remarks>
    public sealed class AttackPlayerSource : ICandidateSource, IHealthAware, ISaboteurAwarenessUser
    {
        /// <summary>Range in metres within which a new attack starts (design: 8 m).</summary>
        public const float Range = 8f;

        /// <summary>Range in metres within which an attack under way carries on.</summary>
        public const float KeepRange = 10f;

        /// <summary>Seconds after the last offer during which the attack counts as under way.</summary>
        public const float EngageGraceSeconds = 1f;

        static readonly ActionKey AttackKey = new ActionKey(SaboteurActionKind.AttackPlayer);

        readonly IPlayerSight _sight;
        readonly UtilityAction _action;
        readonly float[] _inputs = new float[2];
        SaboteurAwareness _awareness;
        float _ownHealth = 1f;
        float _lastOfferAt = float.NegativeInfinity;

        /// <summary>Creates the source.</summary>
        /// <param name="sight">The line-of-sight query; supplied by the runtime.</param>
        public AttackPlayerSource(IPlayerSight sight)
        {
            _sight = sight ?? throw new ArgumentNullException(nameof(sight));
            _awareness = new SaboteurAwareness(sight);
            _action = new UtilityAction(SaboteurActionKind.AttackPlayer,
                new Consideration("AttackDistance", ResponseCurve.Inverse),
                Consideration.Direct("OwnHealth"));
        }

        /// <summary>The line-of-sight query this source was built with; the brain uses it for its awareness.</summary>
        public IPlayerSight Sight => _sight;

        /// <summary>The Saboteur's own health fraction as last reported, 0 to 1.</summary>
        public float OwnHealth => _ownHealth;

        /// <inheritdoc />
        public void OnHealthChanged(int hitPointsLeft, int maxHitPoints) =>
            _ownHealth = maxHitPoints > 0 ? Math.Min(1f, Math.Max(0f, hitPointsLeft / (float)maxHitPoints)) : 1f;

        /// <inheritdoc />
        public void Bind(SaboteurAwareness awareness, IPathfinder pathfinder) =>
            _awareness = awareness ?? throw new ArgumentNullException(nameof(awareness));

        /// <summary>
        /// True if the player can be attacked from <paramref name="position"/>: known, alive and
        /// within <see cref="Range"/> on the ground plane. Sight is checked separately.
        /// </summary>
        public static bool InRange(in PlayerSnapshot player, UnityEngine.Vector3 position, out float distance) =>
            Within(player, position, Range, out distance);

        /// <summary>
        /// True if an attack under way can carry on from <paramref name="position"/>: known, alive and
        /// within <see cref="KeepRange"/>.
        /// </summary>
        public static bool InKeepRange(in PlayerSnapshot player, UnityEngine.Vector3 position, out float distance) =>
            Within(player, position, KeepRange, out distance);

        static bool Within(in PlayerSnapshot player, UnityEngine.Vector3 position, float range, out float distance)
        {
            distance = 0f;
            if (!player.IsKnown || !player.IsAlive)
                return false;

            float dx = player.Position.x - position.x;
            float dz = player.Position.z - position.z;
            distance = (float)Math.Sqrt(dx * dx + dz * dz);
            return distance < range;
        }

        /// <inheritdoc />
        public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates)
        {
            if (ctx.World == null)
                return;

            _awareness.Update(ctx);
            PlayerSnapshot player = ctx.World.Player;
            bool engaged = ctx.Time - _lastOfferAt <= EngageGraceSeconds && ctx.Time >= _lastOfferAt;
            float range = engaged ? KeepRange : Range;
            if (!Within(player, ctx.Position, range, out float distance))
                return;

            // A new attack needs the player seen now; one under way survives a moment out of sight.
            bool seen = _awareness.Sees || (engaged && ctx.Time - _awareness.LastSeenTime <= SaboteurAwareness.TrackGraceSeconds);
            if (!seen)
                return;

            _inputs[0] = distance / range;
            _inputs[1] = _ownHealth;
            ActionScore score = _action.Evaluate(_inputs);
            if (score.Vetoed)
                return;

            _lastOfferAt = ctx.Time;
            candidates.Add(new ActionCandidate(AttackKey, score.BaseScore));
        }
    }
}
