using UnityEngine;

namespace ToyFactory.AI.Agents.Guard
{
    /// <summary>
    /// Line-of-sight answers from the player's eye, supplied by the Runtime layer, which
    /// owns the physics. Tests pass a fake.
    /// </summary>
    public interface ICoverVisibility
    {
        /// <summary>
        /// True if the line from the player's eye to the centre of <paramref name="cell"/>
        /// at <paramref name="height"/> metres is blocked by geometry.
        /// </summary>
        bool IsBlocked(Vector2Int cell, float height);
    }
}
