using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Builds and publishes the level's flat navigation grid when the scene loader asks, and
    /// applies everything that changes it afterwards: doors (<see cref="SetDoorClosed"/>) and
    /// blockers such as pushed boxes and solid props (<see cref="SetBlocker"/>).
    /// </summary>
    public sealed class GridManager : MonoBehaviour
    {
        [Tooltip("Enabled surface with baked data. GridManager never builds or changes the NavMesh.")]
        [SerializeField] NavMeshSurface surface;
        [Tooltip("Minimum X/Z corner of cell (0,0). Y is the shared flat floor height.")]
        [SerializeField] Vector3 origin;
        [SerializeField, Min(1)] int width = 1;
        [SerializeField, Min(1)] int height = 1;
        [Tooltip("NavMesh area bit mask, not a Physics layer mask.")]
        [SerializeField] int areaMask = NavMesh.AllAreas;
        [SerializeField, Range(0.001f, 0.25f)] float sampleDistance = 0.1f;
        [Tooltip("Maximum XZ displacement of a sample from the cell centre, in metres.")]
        [SerializeField, Min(0f)] float horizontalTolerance = 0.01f;
        [Tooltip("Maximum sample height difference from Origin.y; does not change cell heights.")]
        [SerializeField, Min(0f)] float verticalTolerance = 0.1f;

        /// <summary>
        /// Clearance added round a blocker's bounds, matching the NavMesh agent radius, so a box
        /// blocks the same band of cells that a wall of the same size does after the bake.
        /// </summary>
        public const float BlockerClearance = 0.55f;

        static object s_session = new object();
        // What each owner wants blocked, kept even before the grid exists (a box that settles,
        // or a footprint that enables, before BuildGrid) and applied by the build.
        static readonly Dictionary<int, Bounds> s_blockerRequests = new Dictionary<int, Bounds>();
        object _session;
        GridGraph _grid;
        Dictionary<int, Vector2Int[]> _doorCells;
        // Cells each owner currently blocks in _grid, so moving or clearing undoes exactly them.
        readonly Dictionary<int, Vector2Int[]> _blockerCells = new Dictionary<int, Vector2Int[]>();
        bool _building;

        /// <summary>The active owner. Its existence does not imply that the grid is ready.</summary>
        public static GridManager Instance { get; private set; }

        /// <summary>The completed graph, or null before construction and after owner removal.</summary>
        public static GridGraph Current { get; private set; }

        /// <summary>Raised after publication, once per successful build. Late listeners check Current.</summary>
        public static event Action<GridGraph> Ready;

        void OnEnable()
        {
            EnsureSession();
            if (Instance != null && Instance != this)
                Debug.LogWarning("Another GridManager is already active; this manager cannot build.", this);
            else
                Instance = this;
        }

        void OnDisable() => Release();
        void OnDestroy() => Release();

        void Release()
        {
            if (Instance == this)
            {
                Current = null;
                Instance = null;
            }
            _grid = null;
            _doorCells = null;
            _blockerCells.Clear();
        }

        void EnsureSession()
        {
            // Scene reload can also be disabled: an existing component must discard its
            // previous session's graph even when Awake/OnEnable have not run again.
            if (ReferenceEquals(_session, s_session)) return;
            _session = s_session;
            _grid = null;
            _doorCells = null;
            _blockerCells.Clear();
            _building = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Current = null;
            Ready = null;
            s_blockerRequests.Clear();
            s_session = new object();
        }

        /// <summary>
        /// Blocks the cells under <paramref name="worldBounds"/> (inflated by
        /// <see cref="BlockerClearance"/>) for <paramref name="ownerId"/>, replacing whatever that
        /// owner blocked before. Old cells are released and new ones blocked in one batch, so the
        /// grid raises one change per call; a call that changes nothing raises none. Blocking
        /// counts per cell, so two overlapping boxes keep a cell blocked until both have gone.
        /// Called before the grid is built, the request is kept and applied by the build.
        /// </summary>
        /// <remarks>
        /// S2's PushableBox calls this on OnBoxSettled with a positive owner id of its choice;
        /// GridFootprint uses negative ids for static props.
        /// </remarks>
        public static void SetBlocker(int ownerId, Bounds worldBounds)
        {
            s_blockerRequests[ownerId] = worldBounds;
            if (Current == null || Instance == null) return;
            Instance.ApplyBlocker(ownerId, CellsUnder(Current, worldBounds));
        }

        /// <summary>
        /// Releases every cell <paramref name="ownerId"/> blocks, in one batch. Unknown owners are
        /// a no-op. S2's PushableBox calls this on OnBoxMoved.
        /// </summary>
        public static void ClearBlocker(int ownerId)
        {
            s_blockerRequests.Remove(ownerId);
            if (Current == null || Instance == null) return;
            Instance.ApplyBlocker(ownerId, Array.Empty<Vector2Int>());
        }

        /// <summary>Cells <paramref name="ownerId"/> currently blocks in the built grid (empty if none).</summary>
        public static IReadOnlyList<Vector2Int> BlockedCellsOf(int ownerId)
        {
            if (Instance != null && Instance._blockerCells.TryGetValue(ownerId, out Vector2Int[] cells))
                return cells;
            return Array.Empty<Vector2Int>();
        }

        void ApplyBlocker(int ownerId, Vector2Int[] cells)
        {
            _blockerCells.TryGetValue(ownerId, out Vector2Int[] old);
            old = old ?? Array.Empty<Vector2Int>();
            if (SameCells(old, cells)) return;

            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                foreach (Vector2Int cell in old) batch.RemoveBlocker(cell);
                foreach (Vector2Int cell in cells) batch.AddBlocker(cell);
                batch.Commit();
            }
            if (cells.Length == 0) _blockerCells.Remove(ownerId);
            else _blockerCells[ownerId] = cells;
        }

        /// <summary>
        /// Grid cells whose centres lie inside <paramref name="bounds"/> grown by
        /// <see cref="BlockerClearance"/> on X and Z, clamped to the grid. Pure: exposed for tests.
        /// </summary>
        public static Vector2Int[] CellsUnder(GridGraph graph, Bounds bounds)
        {
            float minX = bounds.min.x - BlockerClearance, maxX = bounds.max.x + BlockerClearance;
            float minZ = bounds.min.z - BlockerClearance, maxZ = bounds.max.z + BlockerClearance;
            Vector2Int low = graph.WorldToCell(new Vector3(minX, 0f, minZ));
            Vector2Int high = graph.WorldToCell(new Vector3(maxX, 0f, maxZ));
            int x0 = Mathf.Max(0, low.x), y0 = Mathf.Max(0, low.y);
            int x1 = Mathf.Min(graph.Width - 1, high.x), y1 = Mathf.Min(graph.Height - 1, high.y);

            var cells = new List<Vector2Int>();
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var cell = new Vector2Int(x, y);
                Vector3 centre = graph.CellToWorld(cell);
                if (centre.x >= minX && centre.x <= maxX && centre.z >= minZ && centre.z <= maxZ)
                    cells.Add(cell);
            }
            return cells.ToArray();
        }

        static bool SameCells(Vector2Int[] a, Vector2Int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;   // CellsUnder always lists cells in the same order
            return true;
        }

        /// <summary>
        /// Applies one logical state change to every cell owned by a door. Calls before a
        /// successful build and unknown IDs warn and do nothing. Repeating the current state
        /// is a no-op, so it does not increment the graph version or raise a graph change.
        /// </summary>
        public static void SetDoorClosed(int doorId, bool closed)
        {
            if (Current == null || Instance == null || Instance._doorCells == null)
            {
                Debug.LogWarning("Cannot update a door before the grid has been built.");
                return;
            }
            if (!Instance._doorCells.TryGetValue(doorId, out Vector2Int[] cells))
            {
                Debug.LogWarning($"Door ID {doorId} is not registered with the grid.", Instance);
                return;
            }
            bool changed = false;
            foreach (Vector2Int cell in cells)
                changed |= Current.GetNode(cell).IsDoorClosed != closed;
            if (!changed) return;

            using (GridGraph.Batch batch = Current.BeginBatch())
            {
                foreach (Vector2Int cell in cells)
                    batch.SetDoor(cell, doorId, closed);
                batch.Commit();
            }
        }

        /// <summary>
        /// Builds synchronously from an enabled, baked surface. Does not bake or alter its settings.
        /// Invalid configuration or no supported cells throws without publication; a failed build
        /// can be retried. Repeated successful calls warn and return the same graph without Ready.
        /// </summary>
        public GridGraph BuildGrid()
        {
            EnsureSession();
            if (!isActiveAndEnabled) throw new InvalidOperationException("GridManager must be active to build.");
            if (Instance != null && Instance != this)
                throw new InvalidOperationException("Another GridManager owns the grid.");
            Instance = this;
            if (_building) throw new InvalidOperationException("Grid construction is already in progress.");
            if (_grid != null)
            {
                Debug.LogWarning("Grid already built; returning the existing graph.", this);
                return _grid;
            }

            _building = true;
            GridGraph graph;
            Dictionary<int, Vector2Int[]> doorCells;
            var blockerCells = new Dictionary<int, Vector2Int[]>();
            try
            {
                if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions must be positive and fit an integer cell count.");
                if (!NavMeshGridSampler.IsFinite(origin.x) || !NavMeshGridSampler.IsFinite(origin.y) ||
                    !NavMeshGridSampler.IsFinite(origin.z) ||
                    !NavMeshGridSampler.IsFinite(origin.x + width * GridGraph.CellSize) ||
                    !NavMeshGridSampler.IsFinite(origin.z + height * GridGraph.CellSize))
                    throw new ArgumentException("Grid bounds must be finite.", nameof(origin));
                if (surface == null || !surface.isActiveAndEnabled || surface.navMeshData == null)
                    throw new InvalidOperationException("Assign an enabled NavMeshSurface with baked data before building.");

                var sampler = new NavMeshGridSampler(surface.agentTypeID, areaMask, sampleDistance,
                    horizontalTolerance, verticalTolerance);
                graph = new GridGraph(width, height, origin);
                DoorwayMarker[] markers = FindObjectsByType<DoorwayMarker>(FindObjectsInactive.Exclude);
                Array.Sort(markers, (left, right) => left.DoorId.CompareTo(right.DoorId));
                doorCells = MapDoorways(graph, markers);
                int supportedCells = 0;
                using (GridGraph.Batch batch = graph.BeginBatch())
                {
                    for (int i = 0; i < graph.CellCount; i++)
                    {
                        Vector2Int cell = graph.FromIndex(i);
                        bool walkable = sampler.IsWalkable(graph.CellToWorld(cell));
                        batch.SetWalkable(cell, walkable);
                        if (walkable) supportedCells++;
                    }
                    foreach (DoorwayMarker marker in markers)
                    foreach (Vector2Int cell in doorCells[marker.DoorId])
                        batch.SetDoor(cell, marker.DoorId, marker.InitiallyClosed);
                    // Footprints and boxes that asked before the grid existed join the same batch.
                    foreach (KeyValuePair<int, Bounds> request in s_blockerRequests)
                    {
                        Vector2Int[] cells = CellsUnder(graph, request.Value);
                        foreach (Vector2Int cell in cells) batch.AddBlocker(cell);
                        if (cells.Length > 0) blockerCells[request.Key] = cells;
                    }
                    if (supportedCells == 0)
                        throw new InvalidOperationException("No grid cell is supported by the selected NavMesh and sampling settings.");
                    batch.Commit();
                }
            }
            finally
            {
                _building = false;
            }

            _grid = graph;
            _doorCells = doorCells;
            _blockerCells.Clear();
            foreach (KeyValuePair<int, Vector2Int[]> entry in blockerCells)
                _blockerCells.Add(entry.Key, entry.Value);
            Current = graph;
            // Publication has succeeded even if a consumer's event handler throws.
            Ready?.Invoke(graph);
            return graph;
        }

        static Dictionary<int, Vector2Int[]> MapDoorways(GridGraph graph, DoorwayMarker[] markers)
        {
            var result = new Dictionary<int, Vector2Int[]>(markers.Length);
            var owners = new Dictionary<int, int>();
            foreach (DoorwayMarker marker in markers)
            {
                ValidateMarker(marker);
                if (result.ContainsKey(marker.DoorId))
                    throw new InvalidOperationException($"Door ID {marker.DoorId} is used by more than one doorway marker.");

                var cells = new List<Vector2Int>();
                for (int index = 0; index < graph.CellCount; index++)
                {
                    Vector2Int cell = graph.FromIndex(index);
                    if (!marker.Contains(graph.CellToWorld(cell))) continue;
                    if (owners.TryGetValue(index, out int owner))
                        throw new InvalidOperationException(
                            $"Doorway markers {owner} and {marker.DoorId} both own cell {cell}.");
                    owners.Add(index, marker.DoorId);
                    cells.Add(cell);
                }
                if (cells.Count == 0)
                    throw new InvalidOperationException($"Doorway marker {marker.DoorId} does not contain any grid cell centre.");
                result.Add(marker.DoorId, cells.ToArray());
            }
            return result;
        }

        static void ValidateMarker(DoorwayMarker marker)
        {
            if (marker.DoorId < 0)
                throw new InvalidOperationException($"Doorway marker {marker.name} has a negative door ID.");
            BoxCollider volume = marker.Volume;
            if (volume == null || !volume.enabled || !volume.isTrigger)
                throw new InvalidOperationException(
                    $"Doorway marker {marker.DoorId} requires an enabled trigger BoxCollider on the same object.");
            Vector3 size = volume.size;
            Vector3 scale = marker.transform.lossyScale;
            if (!IsPositiveFinite(size.x) || !IsPositiveFinite(size.y) || !IsPositiveFinite(size.z) ||
                !IsNonZeroFinite(scale.x) || !IsNonZeroFinite(scale.y) || !IsNonZeroFinite(scale.z))
                throw new InvalidOperationException($"Doorway marker {marker.DoorId} must have a finite, non-zero volume.");
        }

        static bool IsPositiveFinite(float value) => NavMeshGridSampler.IsFinite(value) && value > 0f;
        static bool IsNonZeroFinite(float value) => NavMeshGridSampler.IsFinite(value) && value != 0f;

#if UNITY_EDITOR
        const int MaxGizmoGridLines = 10000;
        const int MaxGizmoCells = 100000;
        static readonly Color GridLineColor = new Color(0.25f, 0.8f, 1f, 0.6f);
        static readonly Color WalkableColor = new Color(0.2f, 0.9f, 0.25f, 0.3f);
        static readonly Color UnwalkableColor = new Color(1f, 0.2f, 0.15f, 0.5f);
        static readonly Color OpenDoorColor = new Color(0.1f, 0.85f, 1f, 0.65f);
        static readonly Color ClosedDoorColor = new Color(1f, 0.55f, 0.05f, 0.75f);

        void OnDrawGizmosSelected()
        {
            if (width <= 0 || height <= 0 ||
                !NavMeshGridSampler.IsFinite(origin.x) || !NavMeshGridSampler.IsFinite(origin.y) ||
                !NavMeshGridSampler.IsFinite(origin.z))
                return;

            float worldWidth = width * GridGraph.CellSize;
            float worldHeight = height * GridGraph.CellSize;
            if (!NavMeshGridSampler.IsFinite(worldWidth) || !NavMeshGridSampler.IsFinite(worldHeight))
                return;

            DrawConfiguredGrid(worldWidth, worldHeight);
            if (_grid == null || _grid.CellCount > MaxGizmoCells) return;

            Vector3 cellSize = new Vector3(GridGraph.CellSize * 0.88f, 0.02f,
                GridGraph.CellSize * 0.88f);
            for (int index = 0; index < _grid.CellCount; index++)
            {
                GridNode node = _grid.GetNode(_grid.FromIndex(index));
                if (node.IsDoorway)
                    Gizmos.color = node.IsDoorClosed ? ClosedDoorColor : OpenDoorColor;
                else
                    Gizmos.color = node.Walkable ? WalkableColor : UnwalkableColor;
                Gizmos.DrawCube(node.WorldPosition + Vector3.up * 0.015f, cellSize);
            }
        }

        void DrawConfiguredGrid(float worldWidth, float worldHeight)
        {
            Gizmos.color = GridLineColor;
            Vector3 centre = origin + new Vector3(worldWidth * 0.5f, 0f, worldHeight * 0.5f);
            Gizmos.DrawWireCube(centre, new Vector3(worldWidth, 0.02f, worldHeight));
            if ((long)width + height > MaxGizmoGridLines) return;

            for (int x = 0; x <= width; x++)
            {
                float worldX = origin.x + x * GridGraph.CellSize;
                Gizmos.DrawLine(new Vector3(worldX, origin.y, origin.z),
                    new Vector3(worldX, origin.y, origin.z + worldHeight));
            }
            for (int y = 0; y <= height; y++)
            {
                float worldZ = origin.z + y * GridGraph.CellSize;
                Gizmos.DrawLine(new Vector3(origin.x, origin.y, worldZ),
                    new Vector3(origin.x + worldWidth, origin.y, worldZ));
            }
        }
#endif
    }
}
