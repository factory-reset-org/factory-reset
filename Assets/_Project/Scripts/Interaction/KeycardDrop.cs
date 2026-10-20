using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.World;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// Puts the master keycard pickup into the level when Saboteur A is scrapped, on the
    /// nearest walkable cell to where it fell, and tells the chapter manager about it.
    /// One per scene; it drops the keycard once.
    /// </summary>
    public sealed class KeycardDrop : MonoBehaviour
    {
        // Saboteur A is the keycard carrier.
        const char CarrierLetter = 'A';

        [Tooltip("The keycard pickup: a Collectible whose task id is the keycard task.")]
        [SerializeField] Collectible keycardPrefab;

        [Tooltip("Height of the pickup above the floor (metres).")]
        [SerializeField, Min(0f)] float hoverHeight = 1f;

        [Tooltip("How far to look for a walkable cell round the Saboteur, in grid cells.")]
        [SerializeField, Min(0)] int searchRadius = 8;

        bool _dropped;

        void OnEnable() => AgentEvents.OnDestroyed += HandleDestroyed;

        void OnDisable() => AgentEvents.OnDestroyed -= HandleDestroyed;

        void HandleDestroyed(IAgentState agent)
        {
            if (_dropped || agent == null || agent.Type != AgentType.Saboteur ||
                agent.Identity.SquadLetter != CarrierLetter)
                return;
            if (!(agent is Component body) || keycardPrefab == null)
                return;

            _dropped = true;
            Vector3 position = LandingPoint(body.transform.position) + Vector3.up * hoverHeight;
            Collectible keycard = Instantiate(keycardPrefab, position, Quaternion.identity);
            TaskEvents.RaiseTaskSpawned(keycard);
        }

        // The Saboteur can go down on a blocked cell (against a box, in a doorway); the
        // keycard must always land where the player can walk.
        Vector3 LandingPoint(Vector3 fallen)
        {
            GridGraph grid = GridManager.Current;
            if (grid == null || !grid.TryWorldToCell(fallen, out Vector2Int cell) ||
                !grid.TryFindNearestTraversable(cell, searchRadius, out Vector2Int free))
                return fallen;

            Vector3 landing = grid.CellToWorld(free);
            landing.y = fallen.y;
            return landing;
        }
    }
}
