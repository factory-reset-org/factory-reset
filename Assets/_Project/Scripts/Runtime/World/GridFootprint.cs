using UnityEngine;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Marks a solid prop that is not part of the NavMesh bake (it lives outside the Level
    /// hierarchy, e.g. the hack terminal and relays in Interactables.unity) as a grid blocker.
    /// The footprint is the collider's bounds plus the agent clearance; it is applied by the
    /// grid build (or straight away if the grid already exists) and removed when disabled.
    /// </summary>
    /// <remarks>
    /// For static props only. Things that move, such as pushable boxes, call
    /// <see cref="GridManager.SetBlocker"/> and <see cref="GridManager.ClearBlocker"/> themselves
    /// when they settle and move.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GridFootprint : MonoBehaviour
    {
        [Tooltip("Collider whose bounds block the grid. Empty: the first collider on this object or its children.")]
        [SerializeField] Collider footprint;

        // Footprints take negative owner ids so they never clash with the ids boxes use.
        static int s_lastOwnerId;
        int _ownerId;

        int OwnerId => _ownerId != 0 ? _ownerId : (_ownerId = --s_lastOwnerId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_lastOwnerId = 0;   // domain reload is off

        void OnEnable()
        {
            if (footprint == null)
                footprint = GetComponentInChildren<Collider>();
            if (footprint == null)
            {
                Debug.LogWarning($"GridFootprint on '{name}' has no collider, so it blocks nothing.", this);
                return;
            }
            GridManager.SetBlocker(OwnerId, footprint.bounds);
        }

        void OnDisable() => GridManager.ClearBlocker(OwnerId);
    }
}
