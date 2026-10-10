using System;
using System.Collections.Generic;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Offers AttackPlayer when the player is alive, within <see cref="Range"/> metres and in
    /// the Saboteur's line of sight. Two considerations: <c>1 - d / Range</c>, so the closer the
    /// player the more the Saboteur wants to attack, and its own health fraction, so a damaged
    /// Saboteur attacks less. The squad layer (attack saturation) is applied afterwards by the
    /// brain, so this source looks at nothing but its own instance.
    /// </summary>
    /// <remarks>
    /// Own health arrives through <see cref="IHealthAware"/>: the controller tells the brain, and the
    /// brain passes it on to the sources that want it. It starts at full health.
    /// </remarks>
    public sealed class AttackPlayerSource : ICandidateSource, IHealthAware
    {
        /// <summary>Attack range in metres (design: 8 m).</summary>
        public const float Range = 8f;

        static readonly ActionKey AttackKey = new ActionKey(SaboteurActionKind.AttackPlayer);

        readonly IPlayerSight _sight;
        readonly UtilityAction _action;
        readonly float[] _inputs = new float[2];
        float _ownHealth = 1f;

        /// <summary>Creates the source.</summary>
        /// <param name="sight">The line-of-sight query; supplied by the runtime.</param>
        public AttackPlayerSource(IPlayerSight sight)
        {
            _sight = sight ?? throw new ArgumentNullException(nameof(sight));
            _action = new UtilityAction(SaboteurActionKind.AttackPlayer,
                new Consideration("AttackDistance", ResponseCurve.Inverse),
                Consideration.Direct("OwnHealth"));
        }

        /// <summary>The Saboteur's own health fraction as last reported, 0 to 1.</summary>
        public float OwnHealth => _ownHealth;

        /// <inheritdoc />
        public void OnHealthChanged(int hitPointsLeft, int maxHitPoints) =>
            _ownHealth = maxHitPoints > 0 ? Math.Min(1f, Math.Max(0f, hitPointsLeft / (float)maxHitPoints)) : 1f;

        /// <summary>
        /// True if the player can be attacked from <paramref name="position"/>: known, alive and
        /// within range on the ground plane. Line of sight is checked separately.
        /// </summary>
        public static bool InRange(in PlayerSnapshot player, UnityEngine.Vector3 position, out float distance)
        {
            distance = 0f;
            if (!player.IsKnown || !player.IsAlive)
                return false;

            float dx = player.Position.x - position.x;
            float dz = player.Position.z - position.z;
            distance = (float)Math.Sqrt(dx * dx + dz * dz);
            return distance < Range;
        }

        /// <inheritdoc />
        public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates)
        {
            if (ctx.World == null)
                return;

            PlayerSnapshot player = ctx.World.Player;
            if (!InRange(player, ctx.Position, out float distance) || !_sight.CanSeeCell(ctx.Cell))
                return;

            _inputs[0] = distance / Range;
            _inputs[1] = _ownHealth;
            ActionScore score = _action.Evaluate(_inputs);
            if (!score.Vetoed)
                candidates.Add(new ActionCandidate(AttackKey, score.BaseScore));
        }
    }
}
