using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.AI.Core.Blackboard
{
    /// <summary>
    /// Which agent holds which cell, so two agents never take the same cover or ambush
    /// cell. An agent holds at most one cell: reserving a new one gives up the old.
    /// </summary>
    public sealed class CellReservations
    {
        readonly Dictionary<Vector2Int, int> _holderByCell = new Dictionary<Vector2Int, int>();
        readonly Dictionary<int, Vector2Int> _cellByHolder = new Dictionary<int, Vector2Int>();

        /// <summary>Reserves <paramref name="cell"/> for <paramref name="agentId"/>. False if another agent holds it.</summary>
        public bool TryReserve(Vector2Int cell, int agentId)
        {
            if (_holderByCell.TryGetValue(cell, out int holder) && holder != agentId)
                return false;

            Release(agentId);
            _holderByCell[cell] = agentId;
            _cellByHolder[agentId] = cell;
            return true;
        }

        /// <summary>Releases whatever cell <paramref name="agentId"/> holds, if any.</summary>
        public void Release(int agentId)
        {
            if (_cellByHolder.TryGetValue(agentId, out Vector2Int cell))
            {
                _cellByHolder.Remove(agentId);
                _holderByCell.Remove(cell);
            }
        }

        /// <summary>The agent holding <paramref name="cell"/>, or null if it is free.</summary>
        public int? ReservedBy(Vector2Int cell) =>
            _holderByCell.TryGetValue(cell, out int holder) ? holder : (int?)null;
    }
}
