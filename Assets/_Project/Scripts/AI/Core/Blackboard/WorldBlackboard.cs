using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ToyFactory.AI.Core.Blackboard
{
    /// <summary>
    /// Read-only, shared world state every agent's brain reads from
    /// <see cref="AgentContext.World"/>: player state, active noises, door states,
    /// battery positions and cell reservations.
    /// </summary>
    /// <remarks>
    /// Stub: fields are added here as the systems that produce them land (noise
    /// propagation, doors, batteries). Brains only ever read this; only Runtime
    /// code writes to it.
    /// </remarks>
    public sealed class WorldBlackboard
    {
        readonly List<ObjectiveTarget> _objectiveTargets = new List<ObjectiveTarget>();
        readonly ReadOnlyCollection<ObjectiveTarget> _objectiveTargetView;

        /// <summary>The player as of this frame. <see cref="PlayerSnapshot.IsKnown"/> is false when there is no player.</summary>
        public PlayerSnapshot Player { get; private set; }

        /// <summary>Replaces the player snapshot. Runtime only.</summary>
        public void SetPlayer(in PlayerSnapshot snapshot) => Player = snapshot;

        /// <summary>Currently active objectives in their published order.</summary>
        public IReadOnlyList<ObjectiveTarget> ObjectiveTargets => _objectiveTargetView;

        /// <summary>Changes once for each effective replacement of <see cref="ObjectiveTargets"/>.</summary>
        public int ObjectivesVersion { get; private set; }

        public WorldBlackboard()
        {
            _objectiveTargetView = _objectiveTargets.AsReadOnly();
        }

        /// <summary>Replaces the active objective sequence with a blackboard-owned copy.</summary>
        public void SetObjectiveTargets(IReadOnlyList<ObjectiveTarget> targets)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            if (MatchesCurrentSequence(targets))
            {
                return;
            }

            _objectiveTargets.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                _objectiveTargets.Add(targets[i]);
            }

            ObjectivesVersion++;
        }

        bool MatchesCurrentSequence(IReadOnlyList<ObjectiveTarget> targets)
        {
            if (_objectiveTargets.Count != targets.Count)
            {
                return false;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                ObjectiveTarget current = _objectiveTargets[i];
                ObjectiveTarget candidate = targets[i];
                if (current.Id != candidate.Id || current.Cell != candidate.Cell ||
                    current.Kind != candidate.Kind)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
