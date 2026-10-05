using System;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Answers the Guard brain's line-of-sight questions with a physics ray from the
    /// player's eye to a point above a grid cell. This is the only place the Guard's cover
    /// logic touches physics; the brain sees it as <see cref="ICoverVisibility"/>.
    /// </summary>
    /// <remarks>
    /// Characters never count as cover: a CharacterController in the way (the Guard itself
    /// standing on the cell, another agent, the player) is skipped, and so are triggers.
    /// With no player in the scene nothing is blocked. Allocates nothing per query.
    /// </remarks>
    public sealed class PhysicsCoverVisibility : ICoverVisibility
    {
        /// <summary>Height of the player's eye above their feet, in metres.</summary>
        public const float PlayerEyeHeight = 1.6f;

        readonly GridGraph _grid;
        readonly int _layerMask;
        readonly RaycastHit[] _hits = new RaycastHit[8];

        /// <param name="grid">The level grid, for cell positions.</param>
        /// <param name="layerMask">Layers that can block sight.</param>
        public PhysicsCoverVisibility(GridGraph grid, int layerMask = Physics.DefaultRaycastLayers)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _layerMask = layerMask;
        }

        public bool IsBlocked(Vector2Int cell, float height)
        {
            IPlayerState player = PlayerState.Current;

            // A destroyed MonoBehaviour is not C# null, so ask Unity as well.
            if (player == null || (player is UnityEngine.Object unityObject && unityObject == null))
                return false;
            if (!_grid.Contains(cell))
                return false;

            Vector3 eye = player.Position + Vector3.up * PlayerEyeHeight;
            Vector3 target = _grid.CellToWorld(cell) + Vector3.up * height;
            Vector3 toTarget = target - eye;
            float distance = toTarget.magnitude;
            if (distance <= Mathf.Epsilon)
                return false;

            int count = Physics.RaycastNonAlloc(eye, toTarget / distance, _hits, distance,
                _layerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!(_hits[i].collider is CharacterController))
                    return true;
            }
            return false;
        }
    }
}
