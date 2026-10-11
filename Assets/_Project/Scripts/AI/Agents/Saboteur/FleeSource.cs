using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Offers Flee when the player is aiming at a hurt Saboteur it can see. Three considerations:
    /// how hurt it is (a logistic curve on <c>1 - health</c>, so a scratch barely counts and half
    /// health or less counts a lot), whether the player's forward direction points at it, and how
    /// close the player is (<c>1 - d / Range</c>). Saboteur A, who carries the chapter keycard,
    /// gets a bonus on top, because losing it loses the task item.
    /// </summary>
    /// <remarks>
    /// <para><b>Aiming.</b> The blackboard has no "is aiming at" fact, so aiming is worked out from
    /// <see cref="PlayerSnapshot.Forward"/>: the angle between the player's flat facing direction
    /// and the direction to this Saboteur must be within <see cref="AimHalfAngleDegrees"/>. It is 1 when
    /// the player faces the Saboteur dead on and falls to 0 at the edge of the cone, so a player who
    /// looks away stops being a reason to run.</para>
    /// <para><b>Eligibility</b> comes first and scoring second: no known or dead player, a player
    /// <see cref="Range"/> metres or more away, a Saboteur the player cannot see, or a player not facing
    /// it, and Flee is not a candidate at all. Own health arrives through <see cref="IHealthAware"/>.</para>
    /// <para><b>Keycard bonus.</b> The carrier's score is multiplied by <c>1 + <see cref="KeycardCarrierBonus"/></c>
    /// and capped at 1. A multiplier rather than an addition, so a carrier at full health (score near 0) still
    /// does not run from a player who has not hurt it.</para>
    /// </remarks>
    public sealed class FleeSource : ICandidateSource, IHealthAware
    {
        /// <summary>Metres from the player beyond which a Saboteur does not flee.</summary>
        public const float Range = 14f;

        /// <summary>Half the width, in degrees, of the cone in front of the player that counts as aimed at.</summary>
        public const float AimHalfAngleDegrees = 40f;

        /// <summary>Share added to the keycard carrier's Flee score (x1.25), capped at 1.</summary>
        public const float KeycardCarrierBonus = 0.25f;

        /// <summary>Steepness of the low-health curve.</summary>
        public const float LowHealthSteepness = 10f;

        /// <summary>Health lost, as a fraction, at which the low-health curve is 0.5.</summary>
        public const float LowHealthMidpoint = 0.5f;

        static readonly ActionKey FleeKey = new ActionKey(SaboteurActionKind.Flee);
        static readonly float AimCosine = Mathf.Cos(AimHalfAngleDegrees * Mathf.Deg2Rad);

        readonly IPlayerSight _sight;
        readonly UtilityAction _action;
        readonly float[] _inputs = new float[3];
        float _ownHealth = 1f;

        /// <summary>Creates the source.</summary>
        /// <param name="sight">The line-of-sight query; supplied by the runtime.</param>
        public FleeSource(IPlayerSight sight)
        {
            _sight = sight ?? throw new ArgumentNullException(nameof(sight));
            _action = new UtilityAction(SaboteurActionKind.Flee,
                new Consideration("LowHealth", x => ResponseCurve.Logistic(x, LowHealthSteepness, LowHealthMidpoint)),
                Consideration.Direct("PlayerAimsAtMe"),
                new Consideration("PlayerClose", ResponseCurve.Inverse));
        }

        /// <summary>The Saboteur's own health fraction as last reported, 0 to 1.</summary>
        public float OwnHealth => _ownHealth;

        /// <inheritdoc />
        public void OnHealthChanged(int hitPointsLeft, int maxHitPoints) =>
            _ownHealth = maxHitPoints > 0 ? Math.Min(1f, Math.Max(0f, hitPointsLeft / (float)maxHitPoints)) : 1f;

        /// <summary>
        /// How directly the player faces <paramref name="position"/>, from 0 (outside the aim cone, or
        /// no usable direction) to 1 (dead on). Both directions are flattened onto the ground plane.
        /// </summary>
        public static float AimAt(in PlayerSnapshot player, Vector3 position)
        {
            float fx = player.Forward.x;
            float fz = player.Forward.z;
            float tx = position.x - player.Position.x;
            float tz = position.z - player.Position.z;
            float forwardLength = (float)Math.Sqrt(fx * fx + fz * fz);
            float targetLength = (float)Math.Sqrt(tx * tx + tz * tz);
            if (forwardLength < 1e-4f || targetLength < 1e-4f)
                return targetLength < 1e-4f && forwardLength >= 1e-4f ? 1f : 0f;

            float dot = (fx * tx + fz * tz) / (forwardLength * targetLength);
            float t = (dot - AimCosine) / (1f - AimCosine);
            return t <= 0f ? 0f : t >= 1f ? 1f : t;
        }

        /// <inheritdoc />
        public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates)
        {
            if (ctx.World == null)
                return;

            PlayerSnapshot player = ctx.World.Player;
            if (!player.IsKnown || !player.IsAlive)
                return;

            float dx = player.Position.x - ctx.Position.x;
            float dz = player.Position.z - ctx.Position.z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distance >= Range)
                return;

            float aim = AimAt(player, ctx.Position);
            if (aim <= 0f || !_sight.CanSeeCell(ctx.Cell))
                return;

            _inputs[0] = 1f - _ownHealth;
            _inputs[1] = aim;
            _inputs[2] = distance / Range;
            ActionScore score = _action.Evaluate(_inputs);
            if (score.Vetoed)
                return;

            float final = identity.CarriesKeycard ? Math.Min(1f, score.BaseScore * (1f + KeycardCarrierBonus)) : score.BaseScore;
            candidates.Add(new ActionCandidate(FleeKey, final));
        }
    }
}
