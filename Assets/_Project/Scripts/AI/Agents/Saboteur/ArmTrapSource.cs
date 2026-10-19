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
    /// Offers ArmTrap(t) for every unarmed trap the player is about to walk over. Two
    /// considerations: how soon the player passes it (sooner is more worth it) and how near the
    /// Saboteur is to it.
    /// </summary>
    /// <remarks>
    /// <para><b>Eligibility.</b> The trap's fact is on the blackboard and available (not armed yet);
    /// the player is alive and the squad's <see cref="DetourCache"/> has their route to the objective;
    /// that route passes within <see cref="RouteClearanceCells"/> of the trap, no more than
    /// <see cref="PassHorizonSeconds"/> from now at the player's top speed (the earliest they can
    /// arrive); the Saboteur's walk is no longer than <see cref="MaxTravelMetres"/> and ends before the
    /// player gets there, since arming a trap the player has already passed does nothing. A missing
    /// fact is not a trap, so it is not offered.</para>
    /// <para><b>Awareness.</b> The Saboteur must know the player is about: it sees them, saw them in the
    /// last few seconds, or heard them (<see cref="SaboteurAwareness.IsAlerted"/>). It does not lay traps for
    /// a player it has no reason to expect.</para>
    /// <para><b>Placement.</b> Traps are the level's own trap props, so the places are always valid; the
    /// trap's cell is moved to the nearest walkable cell (traps can stand inside props) and a cell outside
    /// the grid is skipped. A trap closer than <see cref="MinPlayerMetres"/> to the player is not armed: it
    /// would go off under their feet with no chance to see it.</para>
    /// <para><b>Reachability.</b> Straight distances rank the traps cheaply first; then the Saboteur's real
    /// walk (the squad's pathfinder, at most <see cref="MaxSearchesPerDecision"/> per decision) must be no
    /// longer than <see cref="MaxTravelMetres"/> and must get it there <see cref="ArrivalMarginSeconds"/>
    /// before the player. The squad layer then vetoes a trap another Saboteur has claimed or a squad mate
    /// failed to arm. Until a brain binds its awareness and pathfinder, awareness is not required and the
    /// walk is the straight distance.</para>
    /// </remarks>
    public sealed class ArmTrapSource : ICandidateSource, ISaboteurAwarenessUser
    {
        /// <summary>The closest, in metres, the player may be to a trap that is armed.</summary>
        public const float MinPlayerMetres = 4f;

        /// <summary>Seconds by which the Saboteur must beat the player to the trap.</summary>
        public const float ArrivalMarginSeconds = 1f;

        /// <summary>The most walks measured with the pathfinder in one decision.</summary>
        public const int MaxSearchesPerDecision = 2;

        /// <summary>Seconds ahead in which the player must be expected to pass the trap.</summary>
        public const float PassHorizonSeconds = 10f;

        /// <summary>Cells either side of the route within which the player triggers a trap (about 1 m).</summary>
        public const int RouteClearanceCells = 2;

        /// <summary>Walking distance, in metres, at which a trap is too far to be worth it.</summary>
        public const float MaxTravelMetres = 20f;

        readonly GridGraph _grid;
        readonly DetourCache _detours;
        readonly UtilityAction _action;
        readonly float[] _inputs = new float[2];
        SaboteurAwareness _awareness;
        IPathfinder _pathfinder;

        /// <summary>Creates the source over the level grid and the squad's shared detour cache.</summary>
        public ArmTrapSource(GridGraph grid, DetourCache detours)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _detours = detours ?? throw new ArgumentNullException(nameof(detours));
            _action = new UtilityAction(SaboteurActionKind.ArmTrap,
                new Consideration("PassSoon", ResponseCurve.Inverse),
                new Consideration("TrapDistance", ResponseCurve.Inverse));
        }

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
            if (!player.IsKnown || !player.IsAlive || player.SprintSpeed <= 0f)
                return;

            if (_awareness != null)
            {
                _awareness.Update(ctx);
                if (!_awareness.IsAlerted(ctx.Time))
                    return;
            }

            IReadOnlyList<SabotageFact> facts = ctx.World.SabotageFacts;
            if (facts.Count == 0)
                return;

            _detours.Refresh(ctx.World, ctx.Time);
            if (!_detours.HasObjective)
                return;

            IReadOnlyList<Vector2Int> route = _detours.PlayerRoute;
            int searches = 0;
            for (int i = 0; i < facts.Count; i++)
            {
                SabotageFact fact = facts[i];
                if (fact.Kind != SabotageFactKind.Trap || !fact.Available)
                    continue;
                if (!SabotageTargetCells.TrySnap(_grid, fact.Cell, out Vector2Int cell))
                    continue;
                if (!SabotageTargetCells.TryMetresAlong(route, cell, RouteClearanceCells, out float routeMetres))
                    continue;

                float playerSeconds = routeMetres / player.SprintSpeed;
                if (playerSeconds >= PassHorizonSeconds)
                    continue;

                // Never under the player's feet: they must have the chance to see it armed.
                if (SabotageTargetCells.OctileMetres(player.Cell, cell) < MinPlayerMetres)
                    continue;

                float travel = SabotageTargetCells.OctileMetres(ctx.Cell, cell);
                if (travel > MaxTravelMetres || travel / SaboteurBrain.MoveSpeed + ArrivalMarginSeconds >= playerSeconds)
                    continue;

                if (_pathfinder != null)
                {
                    if (searches >= MaxSearchesPerDecision)
                        continue;
                    searches++;
                    if (!SaboteurPaths.TryWalkMetres(_grid, _pathfinder, ctx.Cell, cell, out travel))
                        continue;   // no way to the trap
                    if (travel > MaxTravelMetres || travel / SaboteurBrain.MoveSpeed + ArrivalMarginSeconds >= playerSeconds)
                        continue;
                }

                _inputs[0] = playerSeconds / PassHorizonSeconds;
                _inputs[1] = travel / MaxTravelMetres;
                ActionScore score = _action.Evaluate(_inputs);
                if (!score.Vetoed)
                    candidates.Add(new ActionCandidate(new ActionKey(SaboteurActionKind.ArmTrap, fact.Id), score.BaseScore));
            }
        }
    }
}
