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
    /// Offers CloseDoor(d) for every open door whose closing makes the player's route to the
    /// objective longer. Two considerations: the detour gain (a route that grows by 100% or more
    /// saturates) and how near the Saboteur is to the door. The squad layer then vetoes a door
    /// another Saboteur has claimed.
    /// </summary>
    /// <remarks>
    /// A door is not offered when it is already closed, when closing it would lock the player out
    /// (no route at all), when it does not lengthen the route, or when the player stands in or next
    /// to it. The Saboteur's travel distance is the straight octile distance to the door, not a
    /// search: it only ranks doors, so it must stay cheap for four Saboteurs deciding at 4 Hz, and
    /// the brain finds the real route once it commits.
    /// </remarks>
    public sealed class CloseDoorSource : ICandidateSource
    {
        /// <summary>Travel distance, in metres, at which a door is too far to be worth it.</summary>
        public const float MaxTravelMetres = 30f;

        /// <summary>Cells around a door in which the player counts as standing in its doorway.</summary>
        public const int DoorwayClearance = 1;

        readonly DetourCache _detours;
        readonly UtilityAction _action;
        readonly float[] _inputs = new float[2];

        /// <summary>Creates the source over the squad's shared detour cache.</summary>
        public CloseDoorSource(DetourCache detours)
        {
            _detours = detours ?? throw new ArgumentNullException(nameof(detours));
            _action = new UtilityAction(SaboteurActionKind.CloseDoor,
                Consideration.Direct("DetourGain"),
                new Consideration("DoorDistance", ResponseCurve.Inverse));
        }

        /// <summary>The cache this source reads, which the brain shares to find a door's cells.</summary>
        public DetourCache Detours => _detours;

        /// <inheritdoc />
        public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates)
        {
            if (ctx.World == null)
                return;

            _detours.Refresh(ctx.World, ctx.Time);
            if (!_detours.HasObjective)
                return;

            PlayerSnapshot player = ctx.World.Player;
            IReadOnlyList<DoorDetour> doors = _detours.Doors;
            for (int i = 0; i < doors.Count; i++)
            {
                DoorDetour door = doors[i];
                if (door.IsLockout)
                    continue;

                float gain = DetourCache.GainScore(door.Gain);
                if (gain <= 0f || IsInDoorway(player.Cell, door.Cells))
                    continue;

                _inputs[0] = gain;
                _inputs[1] = NearestDistanceMetres(ctx.Cell, door.Cells) / MaxTravelMetres;
                ActionScore score = _action.Evaluate(_inputs);
                if (!score.Vetoed)
                    candidates.Add(new ActionCandidate(new ActionKey(SaboteurActionKind.CloseDoor, door.DoorId), score.BaseScore));
            }
        }

        /// <summary>True when <paramref name="cell"/> is on or within <see cref="DoorwayClearance"/> cells of the door.</summary>
        public static bool IsInDoorway(Vector2Int cell, IReadOnlyList<Vector2Int> doorCells)
        {
            for (int i = 0; i < doorCells.Count; i++)
            {
                if (Math.Abs(cell.x - doorCells[i].x) <= DoorwayClearance &&
                    Math.Abs(cell.y - doorCells[i].y) <= DoorwayClearance)
                    return true;
            }

            return false;
        }

        /// <summary>The octile distance from <paramref name="cell"/> to the nearest cell of the door, in metres.</summary>
        public static float NearestDistanceMetres(Vector2Int cell, IReadOnlyList<Vector2Int> doorCells)
        {
            float best = float.MaxValue;
            for (int i = 0; i < doorCells.Count; i++)
                best = Math.Min(best, BaseCostModel.OctileDistance(cell, doorCells[i]));
            return best * GridGraph.CellSize;
        }
    }

    /// <summary>
    /// Combines several candidate sources into one, in order, so a brain can offer AttackPlayer
    /// and CloseDoor together.
    /// </summary>
    public sealed class CompositeCandidateSource : ICandidateSource, IHealthAware
    {
        readonly ICandidateSource[] _sources;

        /// <summary>Creates the combination; null entries are rejected.</summary>
        public CompositeCandidateSource(params ICandidateSource[] sources)
        {
            if (sources == null)
                throw new ArgumentNullException(nameof(sources));
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] == null)
                    throw new ArgumentException("Sources must not contain null.", nameof(sources));
            }

            _sources = (ICandidateSource[])sources.Clone();
        }

        /// <inheritdoc />
        public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates)
        {
            for (int i = 0; i < _sources.Length; i++)
                _sources[i].AddCandidates(ctx, identity, candidates);
        }

        /// <inheritdoc />
        public void OnHealthChanged(int hitPointsLeft, int maxHitPoints)
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                if (_sources[i] is IHealthAware aware)
                    aware.OnHealthChanged(hitPointsLeft, maxHitPoints);
            }
        }
    }
}
