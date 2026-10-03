using System;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Builds and publishes the level's flat navigation grid when the scene loader asks.
    /// This adapter handles static walkability only; dynamic occupancy is integrated separately.
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

        static object s_session = new object();
        object _session;
        GridGraph _grid;
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
        }

        void EnsureSession()
        {
            // Scene reload can also be disabled: an existing component must discard its
            // previous session's graph even when Awake/OnEnable have not run again.
            if (ReferenceEquals(_session, s_session)) return;
            _session = s_session;
            _grid = null;
            _building = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Current = null;
            Ready = null;
            s_session = new object();
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
            Current = graph;
            // Publication has succeeded even if a consumer's event handler throws.
            Ready?.Invoke(graph);
            return graph;
        }
    }
}
