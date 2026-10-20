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
    /// Offers CloseDoor(d) for the open ways out of the room the Saboteur and the player are both in, so it
    /// shuts the player in with itself. Two considerations: the door's value (<see cref="DoorValue"/>: 1 for
    /// the way out the player's route takes, less for another) and how near the Saboteur is to the door. The
    /// squad layer then vetoes a door another Saboteur has claimed.
    /// </summary>
    /// <remarks>
    /// <para><b>Same room only.</b> Nothing is offered unless the Saboteur stands in the player's room
    /// (<see cref="DetourCache.IsInPlayerRoom"/>), and only that room's own exits
    /// (<see cref="DetourCache.PlayerRoomExits"/>) are offered. A Saboteur therefore closes a door from the
    /// player's side and never shuts itself out in another room, and a squad mate elsewhere never closes a
    /// door it would have to cross the map for. The player can open any door a Saboteur closes, so shutting
    /// them in costs them a stop, not a soft lock. It counts with or without a known objective.</para>
    /// <para>A door is not offered when it is already closed, when it is not a way out of the shared room, or
    /// when the player stands in or next to it.</para>
    /// <para><b>Awareness.</b> Once a brain has bound its <see cref="SaboteurAwareness"/>, a door is only
    /// offered while the Saboteur knows the player is about (sees them, saw them in the last few seconds or
    /// heard them), so it does not shut doors in the path of a player it cannot know is coming.</para>
    /// <para><b>Distance.</b> The straight octile distance ranks the doors cheaply; with the brain's
    /// pathfinder bound, the real walk to the nearest door cell is then measured for up to
    /// <see cref="MaxSearchesPerDecision"/> doors, and a door with no route or a walk longer than
    /// <see cref="MaxTravelMetres"/> is not offered. The squad layer vetoes a door a squad mate failed to close.</para>
    /// </remarks>
    public sealed class CloseDoorSource : ICandidateSource, ISaboteurAwarenessUser
    {
        /// <summary>The most walks measured with the pathfinder in one decision.</summary>
        public const int MaxSearchesPerDecision = 2;

        /// <summary>Travel distance, in metres, at which a door is too far to be worth it.</summary>
        public const float MaxTravelMetres = 30f;

        /// <summary>Cells around a door in which the player counts as standing in its doorway.</summary>
        public const int DoorwayClearance = 1;

        readonly DetourCache _detours;
        readonly UtilityAction _action;
        readonly float[] _inputs = new float[2];
        SaboteurAwareness _awareness;
        IPathfinder _pathfinder;

        /// <summary>Creates the source over the squad's shared detour cache.</summary>
        public CloseDoorSource(DetourCache detours)
        {
            _detours = detours ?? throw new ArgumentNullException(nameof(detours));
            _action = new UtilityAction(SaboteurActionKind.CloseDoor,
                Consideration.Direct("DoorValue"),
                new Consideration("DoorDistance", ResponseCurve.Inverse));
        }

        /// <summary>The cache this source reads, which the brain shares to find a door's cells.</summary>
        public DetourCache Detours => _detours;

        /// <inheritdoc />
        public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates)
        {
            if (ctx.World == null)
                return;

            // The cache is up to half a second old: a player who has just died or gone missing must not
            // keep earning detour scores from its last result.
            PlayerSnapshot player = ctx.World.Player;
            if (!player.IsKnown || !player.IsAlive)
                return;

            if (_awareness != null)
            {
                _awareness.Update(ctx);
                if (!_awareness.IsAlerted(ctx.Time))
                    return;
            }

            _detours.Refresh(ctx.World, ctx.Time);

            // Only a Saboteur in the player's own room shuts its doors: from inside, so it never shuts itself out.
            if (!_detours.IsInPlayerRoom(ctx.Cell))
                return;

            int searches = 0;
            IReadOnlyList<int> exits = _detours.PlayerRoomExits;
            for (int i = 0; i < exits.Count; i++)
            {
                if (!_detours.TryGetCells(exits[i], out IReadOnlyList<Vector2Int> cells) || cells.Count == 0)
                    continue;
                float value = _detours.HasObjective && _detours.TryGet(exits[i], out DoorDetour door)
                    ? DoorValue(door, roomExit: true)
                    : RoomExitValue;
                Offer(ctx, player, exits[i], cells, value, ref searches, candidates);
            }
        }

        /// <summary>The value of a way out of the player's room that their route does not take.</summary>
        public const float RoomExitValue = 0.6f;

        /// <summary>
        /// What closing a door on the player's route is worth: 1 for their only route (they must stop and
        /// open it), else the detour gain clamped to [0, 1]; 0 for a door off the route.
        /// </summary>
        public static float RouteValue(in DoorDetour door) =>
            door.IsLockout ? 1f : DetourCache.GainScore(door.Gain);

        /// <summary>
        /// What closing a door is worth: a way out of the player's room that their route takes is worth 1 (it
        /// stops them where they are), another way out <see cref="RoomExitValue"/>. A door that is not a way out
        /// of their room is worth its <see cref="RouteValue"/>; the source offers only ways out.
        /// </summary>
        public static float DoorValue(in DoorDetour door, bool roomExit)
        {
            float route = RouteValue(door);
            if (!roomExit)
                return route;
            return route > 0f ? 1f : RoomExitValue;
        }

        void Offer(in AgentContext ctx, in PlayerSnapshot player, int doorId, IReadOnlyList<Vector2Int> cells, float value,
            ref int searches, List<ActionCandidate> candidates)
        {
            if (value <= 0f || IsInDoorway(player.Cell, cells))
                return;

            float travel = NearestDistanceMetres(ctx.Cell, cells);
            if (travel > MaxTravelMetres)
                return;

            if (_pathfinder != null)
            {
                if (searches >= MaxSearchesPerDecision)
                    return;
                searches++;
                if (!SaboteurPaths.TryWalkMetres(_detours.Grid, _pathfinder, ctx.Cell, NearestCell(ctx.Cell, cells), out travel)
                    || travel > MaxTravelMetres)
                    return;   // no way to the door, or too far round
            }

            _inputs[0] = value;
            _inputs[1] = travel / MaxTravelMetres;
            ActionScore score = _action.Evaluate(_inputs);
            if (!score.Vetoed)
                candidates.Add(new ActionCandidate(new ActionKey(SaboteurActionKind.CloseDoor, doorId), score.BaseScore));
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

        /// <inheritdoc />
        public void Bind(SaboteurAwareness awareness, IPathfinder pathfinder)
        {
            _awareness = awareness ?? throw new ArgumentNullException(nameof(awareness));
            _pathfinder = pathfinder;
        }

        static Vector2Int NearestCell(Vector2Int cell, IReadOnlyList<Vector2Int> doorCells)
        {
            Vector2Int best = doorCells[0];
            float bestDistance = float.MaxValue;
            for (int i = 0; i < doorCells.Count; i++)
            {
                float d = BaseCostModel.OctileDistance(cell, doorCells[i]);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = doorCells[i];
                }
            }

            return best;
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
    public sealed class CompositeCandidateSource : ICandidateSource, IHealthAware, ISaboteurAwarenessUser
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

        /// <summary>Passes the brain's awareness and pathfinder to every source that uses them.</summary>
        public void Bind(SaboteurAwareness awareness, IPathfinder pathfinder)
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                if (_sources[i] is ISaboteurAwarenessUser user)
                    user.Bind(awareness, pathfinder);
            }
        }

        /// <summary>The first line-of-sight query among the sources (the attack or flee source's), or null.</summary>
        public IPlayerSight FindSight()
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                if (_sources[i] is AttackPlayerSource attack)
                    return attack.Sight;
                if (_sources[i] is FleeSource flee)
                    return flee.Sight;
                if (_sources[i] is CompositeCandidateSource inner && inner.FindSight() is IPlayerSight found)
                    return found;
            }

            return null;
        }
    }
}
