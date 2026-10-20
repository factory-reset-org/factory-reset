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
    /// Offers ArmTrap(t) for every unarmed trap the player is near or about to walk over. Two
    /// considerations: the urgency (<see cref="Urgency"/>: how near the player is to it, or how soon
    /// they pass it on their route, whichever is more) and how near the Saboteur is to it.
    /// </summary>
    /// <remarks>
    /// <para><b>Eligibility.</b> The trap's fact is on the blackboard and available (not armed yet) and
    /// the player is alive, and either of these holds:</para>
    /// <list type="bullet">
    /// <item><b>Near:</b> the player is within <see cref="NearTrapMetres"/> of the trap. Any walk up to
    /// <see cref="MaxTravelMetres"/> is worth it: a player moving about a room with a trap in it will
    /// likely cross it.</item>
    /// <item><b>On the route:</b> the squad's <see cref="DetourCache"/> has the player's route to the
    /// objective, it passes within <see cref="RouteClearanceCells"/> of the trap no more than
    /// <see cref="PassHorizonSeconds"/> from now at their top speed, and the Saboteur's walk ends
    /// <see cref="ArrivalMarginSeconds"/> before they get there, since arming a trap the player has
    /// already passed does nothing.</item>
    /// </list>
    /// <para>A missing fact is not a trap, so it is not offered.</para>
    /// <para><b>Awareness.</b> The Saboteur must know the player is about: it sees them, saw them in the
    /// last few seconds, or heard them (<see cref="SaboteurAwareness.IsAlerted"/>). It does not lay traps for
    /// a player it has no reason to expect.</para>
    /// <para><b>Placement.</b> Traps are the level's own trap props, so the places are always valid; the
    /// trap's cell is moved to the nearest walkable cell (traps can stand inside props) and a cell outside
    /// the grid is skipped. A trap closer than <see cref="MinPlayerMetres"/> to the player is not armed: they
    /// would be standing on it with no chance to see it armed.</para>
    /// <para><b>Reachability.</b> Straight distances rank the traps cheaply first; then the Saboteur's real
    /// walk (the squad's pathfinder, at most <see cref="MaxSearchesPerDecision"/> per decision) must be no
    /// longer than <see cref="MaxTravelMetres"/> and, for a trap offered only for being on the route, must get
    /// it there <see cref="ArrivalMarginSeconds"/> before the player. The squad layer then vetoes a trap another Saboteur has claimed or a squad mate
    /// failed to arm. Until a brain binds its awareness and pathfinder, awareness is not required and the
    /// walk is the straight distance.</para>
    /// </remarks>
    public sealed class ArmTrapSource : ICandidateSource, ISaboteurAwarenessUser
    {
        /// <summary>The closest, in metres, the player may be to a trap that is armed.</summary>
        public const float MinPlayerMetres = 2f;

        /// <summary>Metres from the trap within which the player counts as near it.</summary>
        public const float NearTrapMetres = 10f;

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
                Consideration.Direct("Urgency"),
                new Consideration("TrapDistance", ResponseCurve.Inverse));
        }

        /// <summary>
        /// How pressing a trap is, 0 to 1: for a player <paramref name="playerMetres"/> from it, from 1 beside it
        /// down to 0.5 at <see cref="NearTrapMetres"/>; for one passing it in <paramref name="passSeconds"/> on their
        /// route, <c>1 - passSeconds / PassHorizonSeconds</c>. The larger of the two; pass infinity for one
        /// that does not apply.
        /// </summary>
        public static float Urgency(float playerMetres, float passSeconds)
        {
            float near = playerMetres <= NearTrapMetres ? 1f - 0.5f * Mathf.Max(0f, playerMetres) / NearTrapMetres : 0f;
            float pass = passSeconds < PassHorizonSeconds ? 1f - Mathf.Max(0f, passSeconds) / PassHorizonSeconds : 0f;
            return Mathf.Max(near, pass);
        }

        // True when a walk of travel metres gets the Saboteur to the trap with the margin to spare before a
        // player who reaches it in playerSeconds.
        static bool InTime(float travel, float playerSeconds) =>
            travel / SaboteurBrain.MoveSpeed + ArrivalMarginSeconds < playerSeconds;

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
            IReadOnlyList<Vector2Int> route = _detours.HasObjective ? _detours.PlayerRoute : null;
            int searches = 0;
            for (int i = 0; i < facts.Count; i++)
            {
                SabotageFact fact = facts[i];
                if (fact.Kind != SabotageFactKind.Trap || !fact.Available)
                    continue;
                if (!SabotageTargetCells.TrySnap(_grid, fact.Cell, out Vector2Int cell))
                    continue;

                float playerMetres = SabotageTargetCells.OctileMetres(player.Cell, cell);
                bool near = playerMetres <= NearTrapMetres;
                float playerSeconds = float.PositiveInfinity;
                if (route != null && SabotageTargetCells.TryMetresAlong(route, cell, RouteClearanceCells, out float routeMetres))
                    playerSeconds = routeMetres / player.SprintSpeed;
                bool onRoute = playerSeconds < PassHorizonSeconds;
                if (!near && !onRoute)
                    continue;

                // Never under the player's feet: they must have the chance to see it armed.
                if (playerMetres < MinPlayerMetres)
                    continue;

                float travel = SabotageTargetCells.OctileMetres(ctx.Cell, cell);
                if (travel > MaxTravelMetres || (!near && !InTime(travel, playerSeconds)))
                    continue;

                if (_pathfinder != null)
                {
                    if (searches >= MaxSearchesPerDecision)
                        continue;
                    searches++;
                    if (!SaboteurPaths.TryWalkMetres(_grid, _pathfinder, ctx.Cell, cell, out travel))
                        continue;   // no way to the trap
                    if (travel > MaxTravelMetres || (!near && !InTime(travel, playerSeconds)))
                        continue;
                }

                _inputs[0] = Urgency(near ? playerMetres : float.PositiveInfinity, InTime(travel, playerSeconds) ? playerSeconds : float.PositiveInfinity);
                _inputs[1] = travel / MaxTravelMetres;
                ActionScore score = _action.Evaluate(_inputs);
                if (!score.Vetoed)
                    candidates.Add(new ActionCandidate(new ActionKey(SaboteurActionKind.ArmTrap, fact.Id), score.BaseScore));
            }
        }
    }
}
