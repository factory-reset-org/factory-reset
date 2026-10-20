using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// A cost model that pretends one door is closed: a step into a closed cell, or a diagonal
    /// step that cuts the corner of one, costs <see cref="float.PositiveInfinity"/>. Everything
    /// else costs the base step. The brain uses it to ask "how much longer is the player's route
    /// if this door closes?" without changing the real grid.
    /// </summary>
    /// <remarks>
    /// <para>The real grid still shows the door as open, so the search considers those steps and
    /// the infinite price rules them out. A route that has to pass the door gets an infinite
    /// total, which is how a lockout shows up (see <see cref="PathCost"/>).</para>
    /// <para>The closed cells are set before each search and the model is reused, so asking about
    /// many doors allocates nothing after the first.</para>
    /// </remarks>
    public sealed class DoorClosureCostModel : ICostModel
    {
        readonly HashSet<Vector2Int> _closed = new HashSet<Vector2Int>();

        /// <summary>Number of cells currently treated as closed.</summary>
        public int ClosedCellCount => _closed.Count;

        /// <summary>Replaces the closed cells with the cells of one door.</summary>
        public void SetClosed(IReadOnlyList<Vector2Int> doorCells)
        {
            if (doorCells == null)
                throw new ArgumentNullException(nameof(doorCells));

            _closed.Clear();
            for (int i = 0; i < doorCells.Count; i++)
                _closed.Add(doorCells[i]);
        }

        /// <summary>Opens everything again: the model then prices steps like <see cref="BaseCostModel"/>.</summary>
        public void Clear() => _closed.Clear();

        /// <inheritdoc />
        public float StepCost(Vector2Int from, Vector2Int to)
        {
            if (_closed.Count > 0)
            {
                if (_closed.Contains(from) || _closed.Contains(to))
                    return float.PositiveInfinity;

                // A diagonal step is only legal when both side cells are open, so closing a
                // door also closes the diagonals that squeeze past it.
                if (from.x != to.x && from.y != to.y &&
                    (_closed.Contains(new Vector2Int(to.x, from.y)) || _closed.Contains(new Vector2Int(from.x, to.y))))
                    return float.PositiveInfinity;
            }

            return BaseCostModel.Instance.StepCost(from, to);
        }

        /// <summary>
        /// The total cost of <paramref name="cells"/> under <paramref name="cost"/>, in grid units
        /// (multiply by <c>GridGraph.CellSize</c> for metres). Infinite if any step is. Fewer than
        /// two cells cost 0.
        /// </summary>
        public static float PathCost(IReadOnlyList<Vector2Int> cells, ICostModel cost)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));
            if (cost == null)
                throw new ArgumentNullException(nameof(cost));

            float total = 0f;
            for (int i = 1; i < cells.Count; i++)
                total += cost.StepCost(cells[i - 1], cells[i]);
            return total;
        }
    }
}
