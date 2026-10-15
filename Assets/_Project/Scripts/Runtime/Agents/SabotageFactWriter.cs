using System;
using System.Collections.Generic;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Copies the traps and batteries a Saboteur could act on onto the blackboard. Reads what is
    /// registered in <see cref="SabotageTargets"/> and each target's <see cref="ISabotageFact"/>
    /// state, turns the position into a grid cell and replaces
    /// <see cref="WorldBlackboard.SabotageFacts"/>. Only Runtime code writes the blackboard.
    /// </summary>
    /// <remarks>
    /// Doors are not listed: the brain gets them from the grid. The facts are sorted by kind and
    /// id so a write that changes nothing is recognised as the same list. Reuses its lists, so a
    /// call allocates nothing once they have grown.
    /// </remarks>
    public sealed class SabotageFactWriter
    {
        readonly WorldBlackboard _blackboard;
        readonly List<SabotageTargetInfo> _targets = new List<SabotageTargetInfo>();
        readonly List<SabotageFact> _facts = new List<SabotageFact>();

        public SabotageFactWriter(WorldBlackboard blackboard)
        {
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        }

        /// <summary>Rebuilds the facts from what is registered now. Does nothing before the grid exists.</summary>
        public void Write(GridGraph grid)
        {
            if (grid == null)
                return;

            SabotageTargets.CopyTo(_targets);
            _facts.Clear();
            for (int i = 0; i < _targets.Count; i++)
            {
                SabotageTargetInfo info = _targets[i];
                if (info.Kind == SabotageKind.Door || !(info.Target is ISabotageFact state))
                    continue;

                SabotageFactKind kind = info.Kind == SabotageKind.Trap ? SabotageFactKind.Trap : SabotageFactKind.Battery;
                _facts.Add(new SabotageFact(info.Id, grid.WorldToCell(info.Where.position), kind, state.Available));
            }

            _facts.Sort(ByKindThenId);
            _blackboard.SetSabotageFacts(_facts);
        }

        static int ByKindThenId(SabotageFact a, SabotageFact b)
        {
            int kind = a.Kind.CompareTo(b.Kind);
            return kind != 0 ? kind : a.Id.CompareTo(b.Id);
        }
    }
}
