using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Mock
{
    /// <summary>
    /// A plain chaser, for showing the Captain against the obvious alternative: every 0.5 s
    /// it plans the shortest route to the player's cell and runs it at the Captain's 4.6 m/s.
    /// No prediction, no shooting. The same rule as the chaser in the accuracy evidence
    /// (<c>CaptainAccuracyEvidenceTests</c>), so the demo shows what the numbers measured.
    /// </summary>
    public sealed class ChaserBrain : IAgentBrain
    {
        public const float ReplanInterval = 0.5f;
        public const string StateName = "Chase";

        const int SnapRadius = 6;

        readonly GridGraph _grid;
        readonly IPathfinder _pathfinder;
        float _nextPlan = float.NegativeInfinity;
        bool _moving;

        public ChaserBrain(GridGraph grid, IPathfinder pathfinder = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _pathfinder = pathfinder ?? new AStarSearch(grid);
        }

        public AgentIntent Tick(in AgentContext ctx)
        {
            PlayerSnapshot player = ctx.World != null ? ctx.World.Player : default;
            bool known = player.IsKnown && player.IsAlive;
            var intent = new AgentIntent
            {
                DesiredSpeed = CaptainBrain.InterceptSpeed,
                DebugState = known ? StateName : "Wait",
                Alert = known ? AlertLevel.Alert : AlertLevel.None
            };

            if (!known)
            {
                // No player (missing or dead): stop once.
                if (_moving)
                    intent.Path = new List<Vector3>();
                _moving = false;
                return intent;
            }

            intent.LookTarget = player.Position;
            if (ctx.Time < _nextPlan)
                return intent;
            _nextPlan = ctx.Time + ReplanInterval;

            if (!_grid.TryFindNearestTraversable(ctx.Cell, SnapRadius, out Vector2Int start) ||
                !_grid.TryFindNearestTraversable(player.Cell, SnapRadius, out Vector2Int goal))
                return intent;
            PathResult result = _pathfinder.FindPath(start, goal, BaseCostModel.Instance);
            if (!result.Found)
                return intent;

            var path = new List<Vector3>(result.Cells.Count);
            for (int i = 0; i < result.Cells.Count; i++)
                path.Add(_grid.CellToWorld(result.Cells[i]));
            intent.Path = path;
            _moving = true;
            return intent;
        }

        /// <summary>A changed grid may block the route: plan again on the next tick.</summary>
        public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) => _nextPlan = float.NegativeInfinity;

        /// <summary>The body stopped while down: plan again as soon as it is up.</summary>
        public void OnStunned(float duration) => _nextPlan = float.NegativeInfinity;

        /// <summary>Holds nothing on the blackboard.</summary>
        public void OnDestroyed() { }
    }
}
