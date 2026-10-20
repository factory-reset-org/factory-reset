using UnityEngine;
using ToyFactory.AI.Agents.Guard;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Answers the Saboteur's one line-of-sight question: can the player see a Saboteur
    /// standing on this cell? The brain never touches physics, so the runtime supplies the
    /// answer (a ray from the player's eye) and tests supply a fake.
    /// </summary>
    public interface IPlayerSight
    {
        /// <summary>True if the player has a clear line to a Saboteur standing on <paramref name="cell"/>.</summary>
        bool CanSeeCell(Vector2Int cell);
    }

    /// <summary>
    /// <see cref="IPlayerSight"/> over the Guard's <see cref="ICoverVisibility"/>, so the
    /// Saboteur reuses the one physics sight check the runtime already has (S2's
    /// <c>PhysicsCoverVisibility</c>) instead of adding a second one.
    /// </summary>
    public sealed class CoverVisibilitySight : IPlayerSight
    {
        /// <summary>Height above the floor, in metres, of the point on a Saboteur the ray aims at (about its chest).</summary>
        public const float TargetHeight = 1f;

        readonly ICoverVisibility _visibility;

        /// <summary>Creates the adapter over a visibility query.</summary>
        public CoverVisibilitySight(ICoverVisibility visibility)
        {
            _visibility = visibility ?? throw new System.ArgumentNullException(nameof(visibility));
        }

        /// <inheritdoc />
        public bool CanSeeCell(Vector2Int cell) => !_visibility.IsBlocked(cell, TargetHeight);
    }
}
