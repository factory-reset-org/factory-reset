using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.AI.Core
{
    /// <summary>What kind of item an agent leaves behind when it is destroyed.</summary>
    public enum ItemDropKind
    {
        /// <summary>The chapter keycard carried by Saboteur A.</summary>
        Keycard = 0,

        /// <summary>A stolen battery. Added to the drops when the Saboteur can carry one.</summary>
        Battery = 1
    }

    /// <summary>One item an agent leaves behind, and where.</summary>
    public readonly struct ItemDrop
    {
        /// <summary>What was dropped.</summary>
        public ItemDropKind Kind { get; }

        /// <summary>Which item of that kind (for example the keycard's or the battery's id).</summary>
        public int ItemId { get; }

        /// <summary>
        /// The grid cell the item lands on: the nearest traversable cell to where the agent
        /// was, never a blocked cell or one inside a box. Valid only after
        /// <see cref="IAgentBrain.OnDestroyed"/> has run.
        /// </summary>
        public Vector2Int Cell { get; }

        /// <summary>Creates a drop.</summary>
        public ItemDrop(ItemDropKind kind, int itemId, Vector2Int cell)
        {
            Kind = kind;
            ItemId = itemId;
            Cell = cell;
        }
    }

    /// <summary>
    /// Implemented by a brain that can leave items behind when it is destroyed. A brain
    /// cannot spawn objects, so it only reports what it dropped and where; the runtime
    /// reads the drops after <see cref="IAgentBrain.OnDestroyed"/> and creates the pickups.
    /// </summary>
    public interface IDropsItems
    {
        /// <summary>
        /// Appends this agent's drops to <paramref name="buffer"/> and returns how many were
        /// appended. The caller owns the buffer, so nothing is allocated here, and existing
        /// entries are kept. Returns 0 until the agent has been destroyed, and 0 for an agent
        /// that carries nothing. Calling it again returns the same drops.
        /// </summary>
        int GetDrops(List<ItemDrop> buffer);
    }
}
