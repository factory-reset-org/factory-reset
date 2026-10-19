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

        /// <summary>
        /// True if the line from the player's eye to <paramref name="point"/> (a world position, not a cell centre)
        /// is blocked. The Guard asks it about the spot it really stands on. With nothing more to say, a check
        /// answers true, so only the game's physics check can make a cover give way.
        /// </summary>
        bool IsBlockedAt(Vector3 point) => true;
    }
}
