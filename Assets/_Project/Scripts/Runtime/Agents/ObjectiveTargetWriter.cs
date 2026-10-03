using System;
using System.Collections.Generic;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.World;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Copies the chapter's active objectives onto the blackboard so brains can read them.
    /// The chapter manager publishes world positions through <see cref="ObjectiveEvents"/>
    /// (it never sees a brain or the blackboard); this writer turns each position into a
    /// grid cell and replaces <see cref="WorldBlackboard.ObjectiveTargets"/>. Keeps the rule
    /// that only Runtime code writes the blackboard.
    /// </summary>
    /// <remarks>
    /// If targets arrive before the level grid exists, the latest list is kept and written
    /// as soon as <see cref="GridManager.Ready"/> fires, so nothing is lost if the load order
    /// slips. Converting reuses one list, so an update allocates nothing once it has grown.
    /// </remarks>
    public sealed class ObjectiveTargetWriter : IDisposable
    {
        readonly WorldBlackboard _blackboard;
        readonly List<ObjectiveTargetInfo> _latest = new List<ObjectiveTargetInfo>();
        readonly List<ObjectiveTarget> _converted = new List<ObjectiveTarget>();
        bool _hasPending;
        bool _disposed;

        public ObjectiveTargetWriter(WorldBlackboard blackboard)
        {
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            ObjectiveEvents.OnTargetsChanged += HandleTargetsChanged;
            GridManager.Ready += HandleGridReady;
        }

        /// <summary>Stops listening. The writer does nothing after this.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            ObjectiveEvents.OnTargetsChanged -= HandleTargetsChanged;
            GridManager.Ready -= HandleGridReady;
        }

        void HandleTargetsChanged(IReadOnlyList<ObjectiveTargetInfo> targets)
        {
            // Copy now: the sender may reuse its list after the event returns.
            _latest.Clear();
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                    _latest.Add(targets[i]);
            }
            _hasPending = true;
            TryWrite(GridManager.Current);
        }

        void HandleGridReady(GridGraph grid) => TryWrite(grid);

        void TryWrite(GridGraph grid)
        {
            if (!_hasPending || grid == null)
                return;

            _converted.Clear();
            for (int i = 0; i < _latest.Count; i++)
            {
                ObjectiveTargetInfo info = _latest[i];
                // Not clamped: a target on a prop or just off the grid keeps its true cell,
                // and brains snap it to the nearest walkable cell.
                _converted.Add(new ObjectiveTarget(info.Id, grid.WorldToCell(info.Position), ToBlackboardKind(info.Kind)));
            }

            _blackboard.SetObjectiveTargets(_converted);
            _hasPending = false;
        }

        // Explicit mapping rather than a cast, so reordering either enum cannot silently
        // turn a switch into a battery.
        static ObjectiveTargetKind ToBlackboardKind(ObjectiveKind kind)
        {
            switch (kind)
            {
                case ObjectiveKind.Task: return ObjectiveTargetKind.Task;
                case ObjectiveKind.Switch: return ObjectiveTargetKind.Switch;
                case ObjectiveKind.Console: return ObjectiveTargetKind.Console;
                case ObjectiveKind.Battery: return ObjectiveTargetKind.Battery;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown objective kind.");
            }
        }
    }
}
