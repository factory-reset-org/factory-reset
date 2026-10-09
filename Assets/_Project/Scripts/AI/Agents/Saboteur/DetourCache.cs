using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>One open door and what closing it would do to the player's route to the objective.</summary>
    public readonly struct DoorDetour
    {
        /// <summary>The door id (the grid's <c>DoorId</c>, which is also <c>AgentIntent.ActionTargetId</c>).</summary>
        public int DoorId { get; }

        /// <summary>The grid cells the door fills.</summary>
        public IReadOnlyList<Vector2Int> Cells { get; }

        /// <summary>The player's route cost with every door as it is now, in grid units.</summary>
        public float OpenCost { get; }

        /// <summary>The player's route cost with this door closed; infinite for a lockout.</summary>
        public float ClosedCost { get; }

        /// <summary>
        /// <c>(closed - open) / open</c>: 0 for a door off the player's route, 0.6 for a route that
        /// grows from 10 to 16. Infinite for a lockout, so check <see cref="IsLockout"/> first.
        /// </summary>
        public float Gain { get; }

        /// <summary>True when closing the door cuts the player off from the objective entirely.</summary>
        public bool IsLockout => float.IsInfinity(ClosedCost);

        /// <summary>Creates a result.</summary>
        public DoorDetour(int doorId, IReadOnlyList<Vector2Int> cells, float openCost, float closedCost)
        {
            DoorId = doorId;
            Cells = cells;
            OpenCost = openCost;
            ClosedCost = closedCost;
            Gain = DetourCache.GainOf(openCost, closedCost);
        }
    }

    /// <summary>
    /// Works out, once for the whole squad, how much longer the player's route to the objective
    /// becomes if each open door closes. Every Saboteur asks this cache, so four instances cost
    /// the same searches as one: a refresh runs at most every <see cref="RefreshSeconds"/> of game
    /// time (or sooner when the grid or the objectives change) and does one search for the current
    /// route plus one per door that lies on it.
    /// </summary>
    /// <remarks>
    /// <para><b>Objective.</b> The Captain's <see cref="PredictedGoal"/> when it is known with at
    /// least <see cref="MinPredictionConfidence"/>; otherwise the nearest entry of
    /// <see cref="WorldBlackboard.ObjectiveTargets"/>. The route starts at the player's cell.</para>
    /// <para><b>Never changes the grid.</b> A closed door is only a price (<see cref="DoorClosureCostModel"/>),
    /// so the live graph, its version and every other agent's routes are untouched.</para>
    /// <para><b>Lockouts.</b> A door that would leave no route at all has an infinite
    /// <see cref="DoorDetour.ClosedCost"/>. It is reported, not hidden, and the CloseDoor source
    /// rejects it: the design never traps the player.</para>
    /// </remarks>
    public sealed class DetourCache
    {
        /// <summary>Seconds of game time between two refreshes.</summary>
        public const float RefreshSeconds = 0.5f;

        /// <summary>Lowest prediction confidence at which the Captain's goal replaces the nearest objective.</summary>
        public const float MinPredictionConfidence = 0.5f;

        /// <summary>How far, in cells, an objective or player cell inside a prop may be moved to a walkable one.</summary>
        public const int SnapRadius = 4;

        static readonly ProfilerMarker Marker = new ProfilerMarker("AI.Saboteur.Detour");

        sealed class DoorEntry
        {
            public int Id;
            public readonly List<Vector2Int> Cells = new List<Vector2Int>();
            public bool Closed;
        }

        readonly GridGraph _grid;
        readonly IPathfinder _pathfinder;
        readonly DoorClosureCostModel _closure = new DoorClosureCostModel();
        readonly List<DoorEntry> _entries = new List<DoorEntry>();
        readonly List<DoorDetour> _results = new List<DoorDetour>();
        readonly Dictionary<int, int> _resultIndex = new Dictionary<int, int>();
        readonly HashSet<Vector2Int> _routeNeighbourhood = new HashSet<Vector2Int>();

        int _doorsVersion = -1;
        int _gridVersion = -1;
        int _objectivesVersion = -1;
        float _computedAt;
        bool _hasComputed;

        /// <summary>Creates a cache over the level grid and the shared search.</summary>
        public DetourCache(GridGraph grid, IPathfinder pathfinder)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        }

        /// <summary>The open doors from the latest refresh. Empty while <see cref="HasObjective"/> is false.</summary>
        public IReadOnlyList<DoorDetour> Doors => _results;

        /// <summary>True when the latest refresh found a player, an objective and a route between them.</summary>
        public bool HasObjective { get; private set; }

        /// <summary>The cell the latest refresh measured the player's route to.</summary>
        public Vector2Int ObjectiveCell { get; private set; }

        /// <summary>Refreshes done so far, for tests and the evidence log.</summary>
        public int Refreshes { get; private set; }

        /// <summary>Searches made so far (the route plus one per door on it), for tests and the evidence log.</summary>
        public int Searches { get; private set; }

        /// <summary>
        /// Brings the results up to date for game time <paramref name="now"/>. Cheap when nothing
        /// is due, so every Saboteur calls it before reading <see cref="Doors"/>.
        /// </summary>
        public void Refresh(WorldBlackboard world, float now)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            bool due = !_hasComputed
                || now < _computedAt
                || now - _computedAt >= RefreshSeconds
                || _grid.Version != _gridVersion
                || world.ObjectivesVersion != _objectivesVersion;
            if (!due)
                return;

            using (Marker.Auto())
            {
                Compute(world);
                _computedAt = now;
                _gridVersion = _grid.Version;
                _objectivesVersion = world.ObjectivesVersion;
                _hasComputed = true;
                Refreshes++;
            }
        }

        /// <summary>The latest result for a door, or false if it is closed, unknown or the route is unknown.</summary>
        public bool TryGet(int doorId, out DoorDetour detour)
        {
            if (_resultIndex.TryGetValue(doorId, out int index))
            {
                detour = _results[index];
                return true;
            }

            detour = default;
            return false;
        }

        /// <summary>
        /// The cells of a door by id, open or closed, from the latest scan of the grid. False for an
        /// id the grid does not have. Valid after the first <see cref="Refresh"/>.
        /// </summary>
        public bool TryGetCells(int doorId, out IReadOnlyList<Vector2Int> cells)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id == doorId)
                {
                    cells = _entries[i].Cells;
                    return true;
                }
            }

            cells = null;
            return false;
        }

        /// <summary>
        /// The detour gain: <c>(closed - open) / open</c>, infinite for a lockout. Returns 0 when the
        /// open route is free (the player is already there), because the ratio is undefined.
        /// </summary>
        public static float GainOf(float openCost, float closedCost)
        {
            if (float.IsNaN(openCost) || float.IsNaN(closedCost) || openCost <= 0f || float.IsInfinity(openCost))
                return 0f;
            if (float.IsInfinity(closedCost))
                return float.PositiveInfinity;

            return Math.Max(0f, (closedCost - openCost) / openCost);
        }

        /// <summary>
        /// The consideration input for a gain: clamped to [0, 1], so a route that grows by 100% or
        /// more saturates. A lockout scores 0, because it is rejected, never rewarded.
        /// </summary>
        public static float GainScore(float gain)
        {
            if (float.IsNaN(gain) || float.IsInfinity(gain) || gain <= 0f)
                return 0f;
            return gain >= 1f ? 1f : gain;
        }

        void Compute(WorldBlackboard world)
        {
            _results.Clear();
            _resultIndex.Clear();
            HasObjective = false;
            ScanDoors();

            if (!TryGetRoute(world, out Vector2Int start, out Vector2Int goal, out List<Vector2Int> route, out float openCost))
                return;

            HasObjective = true;
            ObjectiveCell = goal;
            MarkRouteNeighbourhood(route);

            for (int i = 0; i < _entries.Count; i++)
            {
                DoorEntry door = _entries[i];
                if (door.Closed)
                    continue;

                float closedCost = openCost;
                if (TouchesRoute(door))
                {
                    _closure.SetClosed(door.Cells);
                    Searches++;
                    PathResult result = _pathfinder.FindPath(start, goal, _closure);
                    closedCost = result.Found && result.Cells != null
                        ? DoorClosureCostModel.PathCost(result.Cells, _closure)
                        : float.PositiveInfinity;
                    _closure.Clear();
                }

                _resultIndex[door.Id] = _results.Count;
                _results.Add(new DoorDetour(door.Id, door.Cells, openCost, closedCost));
            }
        }

        // The grid's doors change only with its version, so the scan is redone then and not per refresh.
        // Door state (open or closed) is read each time because it changes with the version too.
        void ScanDoors()
        {
            if (_doorsVersion == _grid.Version)
                return;

            _doorsVersion = _grid.Version;
            for (int i = 0; i < _entries.Count; i++)
                _entries[i].Cells.Clear();
            int used = 0;

            for (int y = 0; y < _grid.Height; y++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    GridNode node = _grid.GetNode(new Vector2Int(x, y));
                    if (!node.DoorId.HasValue)
                        continue;

                    DoorEntry entry = FindEntry(node.DoorId.Value, used, out bool isNew);
                    if (isNew)
                        used++;
                    entry.Cells.Add(node.Cell);
                    entry.Closed = node.IsDoorClosed;
                }
            }

            _entries.RemoveRange(used, _entries.Count - used);
        }

        // Entries are kept in a prefix of _entries so the lists are reused from scan to scan.
        DoorEntry FindEntry(int doorId, int used, out bool isNew)
        {
            for (int i = 0; i < used; i++)
            {
                if (_entries[i].Id == doorId)
                {
                    isNew = false;
                    return _entries[i];
                }
            }

            isNew = true;
            if (used < _entries.Count)
            {
                _entries[used].Id = doorId;
                return _entries[used];
            }

            var created = new DoorEntry { Id = doorId };
            _entries.Add(created);
            return created;
        }

        bool TryGetRoute(WorldBlackboard world, out Vector2Int start, out Vector2Int goal,
            out List<Vector2Int> route, out float openCost)
        {
            start = default;
            goal = default;
            route = null;
            openCost = 0f;

            PlayerSnapshot player = world.Player;
            if (!player.IsKnown || !player.IsAlive)
                return false;
            if (!Snap(player.Cell, out start) || !TryPickObjective(world, start, out Vector2Int objective) || !Snap(objective, out goal))
                return false;
            if (start == goal)
                return false;

            Searches++;
            PathResult open = _pathfinder.FindPath(start, goal, BaseCostModel.Instance);
            if (!open.Found || open.Cells == null || open.Cells.Count < 2)
                return false;

            route = open.Cells;
            openCost = DoorClosureCostModel.PathCost(route, BaseCostModel.Instance);
            return openCost > 0f;
        }

        bool TryPickObjective(WorldBlackboard world, Vector2Int from, out Vector2Int cell)
        {
            PredictedGoal prediction = world.PredictedGoal;
            if (prediction.IsKnown && prediction.Confidence >= MinPredictionConfidence)
            {
                cell = prediction.Cell;
                return true;
            }

            IReadOnlyList<ObjectiveTarget> targets = world.ObjectiveTargets;
            bool found = false;
            float best = float.MaxValue;
            cell = default;
            for (int i = 0; i < targets.Count; i++)
            {
                float distance = BaseCostModel.OctileDistance(from, targets[i].Cell);
                if (!found || distance < best)
                {
                    found = true;
                    best = distance;
                    cell = targets[i].Cell;
                }
            }

            return found;
        }

        bool Snap(Vector2Int cell, out Vector2Int result)
        {
            if (_grid.IsTraversable(cell))
            {
                result = cell;
                return true;
            }

            return _grid.TryFindNearestTraversable(cell, SnapRadius, out result);
        }

        // A door matters to the route if the route passes through it or squeezes diagonally past it,
        // so any route cell within one cell of a door cell counts.
        void MarkRouteNeighbourhood(List<Vector2Int> route)
        {
            _routeNeighbourhood.Clear();
            for (int i = 0; i < route.Count; i++)
                _routeNeighbourhood.Add(route[i]);
        }

        bool TouchesRoute(DoorEntry door)
        {
            for (int i = 0; i < door.Cells.Count; i++)
            {
                Vector2Int cell = door.Cells[i];
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (_routeNeighbourhood.Contains(new Vector2Int(cell.x + dx, cell.y + dy)))
                            return true;
                    }
                }
            }

            return false;
        }
    }
}
