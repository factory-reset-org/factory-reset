using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Offers StealBattery(b) for every battery lying around that the Saboteur can take before the
    /// player does. Two considerations: how short of ammunition the player is (the less they have
    /// loaded, the more taking a spare cell hurts) and how near the Saboteur is to the battery.
    /// </summary>
    /// <remarks>
    /// <para><b>Eligibility.</b> The battery's fact is on the blackboard and available (still there);
    /// the player is known and alive; the Saboteur's walk is no longer than
    /// <see cref="MaxTravelMetres"/> and shorter in time than the player's straight walk to it at
    /// their top speed. A player with a full battery makes the ammunition consideration 0, which
    /// vetoes the theft: it would not hurt them. A missing fact is not a battery, so it is not offered.</para>
    /// <para>As for doors and traps, the walk is a straight octile distance and the brain finds the real
    /// route on commit. The battery's cell is moved to the nearest walkable cell (batteries can stand
    /// on shelves and inside props) and a cell outside the grid is skipped. The squad layer vetoes a
    /// battery another Saboteur has claimed.</para>
    /// </remarks>
    public sealed class StealBatterySource : ICandidateSource
    {
        /// <summary>Travel distance, in metres, at which a battery is too far to be worth it.</summary>
        public const float MaxTravelMetres = 30f;

        readonly GridGraph _grid;
        readonly UtilityAction _action;
        readonly float[] _inputs = new float[2];

        /// <summary>Creates the source over the level grid.</summary>
        public StealBatterySource(GridGraph grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _action = new UtilityAction(SaboteurActionKind.StealBattery,
                new Consideration("PlayerAmmo", ResponseCurve.Inverse),
                new Consideration("BatteryDistance", ResponseCurve.Inverse));
        }

        /// <inheritdoc />
        public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates)
        {
            if (ctx.World == null)
                return;

            PlayerSnapshot player = ctx.World.Player;
            if (!player.IsKnown || !player.IsAlive)
                return;

            IReadOnlyList<SabotageFact> facts = ctx.World.SabotageFacts;
            for (int i = 0; i < facts.Count; i++)
            {
                SabotageFact fact = facts[i];
                if (fact.Kind != SabotageFactKind.Battery || !fact.Available)
                    continue;
                if (!SabotageTargetCells.TrySnap(_grid, fact.Cell, out Vector2Int cell))
                    continue;

                float travel = SabotageTargetCells.OctileMetres(ctx.Cell, cell);
                if (travel > MaxTravelMetres)
                    continue;

                // The player's straight walk is the least time they need, so beating it is a fair race.
                if (player.SprintSpeed > 0f &&
                    travel / SaboteurBrain.MoveSpeed >= SabotageTargetCells.OctileMetres(player.Cell, cell) / player.SprintSpeed)
                    continue;

                _inputs[0] = Mathf.Clamp01(player.AmmoFraction);
                _inputs[1] = travel / MaxTravelMetres;
                ActionScore score = _action.Evaluate(_inputs);
                if (!score.Vetoed)
                    candidates.Add(new ActionCandidate(new ActionKey(SaboteurActionKind.StealBattery, fact.Id), score.BaseScore));
            }
        }
    }
}
