using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Offers StealBattery(b) for a battery nearer the Saboteur than the player, once it has noticed them.
    /// Two considerations: how much the player needs it (<see cref="NeedScore"/>: half for a full blaster,
    /// rising to 1 for an empty one, since a battery also gives a spare cell and repairs) and how near the
    /// Saboteur is to the battery.
    /// </summary>
    /// <remarks>
    /// <para><b>Eligibility.</b> The Saboteur is alerted to the player (its <see cref="SaboteurAwareness"/>:
    /// it sees them, saw them in the last few seconds or heard them), so a battery is never taken from a
    /// player it has not noticed. The battery's fact is on the blackboard and available (still there); its
    /// walkable cell is within <see cref="InteractionRangeMetres"/> of the Saboteur by the walk it would really
    /// take (the squad's pathfinder), and that walk is shorter than the player's straight line to the
    /// battery: the battery is closer to the Saboteur than to the player. A full blaster no longer rules a
    /// theft out: the player would still lose the spare cell and the repair.</para>
    /// <para><b>Cost.</b> The straight distances rule most batteries out first; at most
    /// <see cref="MaxSearchesPerDecision"/> walks are measured per decision. The battery's cell is moved to
    /// the nearest walkable cell (batteries can stand on shelves and inside props) and a cell outside the
    /// grid is skipped. The squad layer vetoes a battery another Saboteur has claimed, and one a squad mate
    /// failed to take.</para>
    /// <para><b>Unbound.</b> Until a brain binds its awareness and pathfinder (a source used on its own),
    /// sight is not required and the walk is the straight octile distance.</para>
    /// </remarks>
    public sealed class StealBatterySource : ICandidateSource, ISaboteurAwarenessUser
    {
        /// <summary>The farthest walk, in metres, at which a battery is within reach of a theft.</summary>
        public const float InteractionRangeMetres = 12f;

        /// <summary>Kept for the debug text: the range the distance consideration is scored against.</summary>
        public const float MaxTravelMetres = InteractionRangeMetres;

        /// <summary>The most walks measured with the pathfinder in one decision.</summary>
        public const int MaxSearchesPerDecision = 2;

        readonly GridGraph _grid;
        readonly UtilityAction _action;
        readonly float[] _inputs = new float[2];
        SaboteurAwareness _awareness;
        IPathfinder _pathfinder;

        /// <summary>Creates the source over the level grid.</summary>
        public StealBatterySource(GridGraph grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _action = new UtilityAction(SaboteurActionKind.StealBattery,
                new Consideration("PlayerNeed", NeedScore),
                new Consideration("BatteryDistance", ResponseCurve.Inverse));
        }

        /// <summary>The player's need for a battery from their loaded ammunition fraction: 0.5 when full, 1 when empty.</summary>
        public static float NeedScore(float ammoFraction) => ResponseCurve.Linear(ammoFraction, -0.5f, 1f);

        /// <inheritdoc />
        public void Bind(SaboteurAwareness awareness, IPathfinder pathfinder)
        {
            _awareness = awareness ?? throw new ArgumentNullException(nameof(awareness));
            _pathfinder = pathfinder;
        }

        /// <inheritdoc />
        public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates)
        {
            if (ctx.World == null)
                return;

            PlayerSnapshot player = ctx.World.Player;
            if (!player.IsKnown || !player.IsAlive)
                return;

            if (_awareness != null)
            {
                _awareness.Update(ctx);
                if (!_awareness.IsAlerted(ctx.Time))
                    return;
            }

            int searches = 0;
            IReadOnlyList<SabotageFact> facts = ctx.World.SabotageFacts;
            for (int i = 0; i < facts.Count; i++)
            {
                SabotageFact fact = facts[i];
                if (fact.Kind != SabotageFactKind.Battery || !fact.Available)
                    continue;
                if (!SabotageTargetCells.TrySnap(_grid, fact.Cell, out Vector2Int cell))
                    continue;

                // Straight lines first: the walk can only be longer, so these rule out most batteries for free.
                float straight = SabotageTargetCells.OctileMetres(ctx.Cell, cell);
                float playerStraight = SabotageTargetCells.OctileMetres(player.Cell, cell);
                if (straight > InteractionRangeMetres || straight >= playerStraight)
                    continue;

                float travel = straight;
                if (_pathfinder != null)
                {
                    if (searches >= MaxSearchesPerDecision)
                        continue;
                    searches++;
                    if (!SaboteurPaths.TryWalkMetres(_grid, _pathfinder, ctx.Cell, cell, out travel))
                        continue;   // no way to the battery
                    if (travel > InteractionRangeMetres || travel >= playerStraight)
                        continue;
                }

                _inputs[0] = Mathf.Clamp01(player.AmmoFraction);
                _inputs[1] = travel / InteractionRangeMetres;
                ActionScore score = _action.Evaluate(_inputs);
                if (!score.Vetoed)
                    candidates.Add(new ActionCandidate(new ActionKey(SaboteurActionKind.StealBattery, fact.Id), score.BaseScore));
            }
        }
    }
}
