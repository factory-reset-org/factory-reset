using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Runtime.Movement
{
    /// <summary>
    /// How wide a body is at the shoulders, for a body much wider than its capsule (the
    /// Captain: a 1.1 m capsule, 1.9 m across its shoulders, arms and cannon). The grid keeps
    /// only the capsule clear, so the body measures, once, where its shoulders would scrape the
    /// level, and its brain routes round those places.
    /// </summary>
    /// <remarks>
    /// The test is a box the size of its shoulders, at shoulder and arm height, centred on a
    /// cell and facing along X or along Z. Agents, the player and pushable boxes do not count:
    /// they move, and the capsule already keeps clear of them.
    /// </remarks>
    public sealed class ShoulderFit : MonoBehaviour
    {
        [Tooltip("Half the width across the shoulders, arms and cannon, m (measured on the model).")]
        [SerializeField, Min(0.1f)] float halfWidth = 0.95f;
        [Tooltip("Half the body's depth front to back at that height, m.")]
        [SerializeField, Min(0.05f)] float halfDepth = 0.3f;
        [Tooltip("Height of the bottom of the wide part (the hanging cannon), m.")]
        [SerializeField, Min(0f)] float bandBottom = 0.6f;
        [Tooltip("Height of the top of the wide part (the epaulettes), m.")]
        [SerializeField, Min(0.1f)] float bandTop = 2.35f;
        [Tooltip("What the shoulders can scrape: the level and its props. Nothing set = Default, Environment and TaskProp.")]
        [SerializeField] LayerMask blockingLayers;

        int Mask => blockingLayers.value != 0 ? blockingLayers.value : LayerMask.GetMask("Default", "Environment", "TaskProp");

        /// <summary>
        /// True if the shoulders fit at <paramref name="feet"/> facing <paramref name="direction"/>:
        /// the box across them touches nothing on the blocking layers.
        /// </summary>
        public bool Fits(Vector3 feet, Vector3 direction)
        {
            float half = (bandTop - bandBottom) * 0.5f;
            Vector3 centre = feet + Vector3.up * (bandBottom + half);
            return !Physics.CheckBox(centre, new Vector3(halfWidth, half, halfDepth),
                Quaternion.LookRotation(direction, Vector3.up), Mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Where the shoulders fit on <paramref name="grid"/>, each way round, at every walkable
        /// cell: walking along X (facing X, shoulders across Z) and along Z. Cells that are not
        /// walkable are left as fitting (the grid already keeps the body out).
        /// </summary>
        public BodyFit MeasureFit(GridGraph grid)
        {
            Physics.SyncTransforms();
            var fit = new BodyFit(grid.Width, grid.Height);
            for (int i = 0; i < grid.CellCount; i++)
            {
                Vector2Int cell = grid.FromIndex(i);
                if (!grid.GetNode(cell).Walkable)
                    continue;
                Vector3 feet = grid.CellToWorld(cell);
                fit.Set(cell, Fits(feet, Vector3.right), Fits(feet, Vector3.forward));
            }
            return fit;
        }
    }
}
