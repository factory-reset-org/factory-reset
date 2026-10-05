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

        /// <summary>Cover and ambush cells held by agents, so two agents never pick the same one.</summary>
        public CellReservations Reservations { get; } = new CellReservations();

        /// <summary>Target claims, one shared instance so the Saboteur squad coordinates.</summary>
        public TargetClaims Claims { get; } = new TargetClaims();

        /// <summary>Currently active objectives in their published order.</summary>
        public IReadOnlyList<ObjectiveTarget> ObjectiveTargets => _objectiveTargetView;

        /// <summary>Changes once for each effective replacement of <see cref="ObjectiveTargets"/>.</summary>
        public int ObjectivesVersion { get; private set; }

        /// <summary>Number of chapters in the journey; the last one is the final chapter.</summary>
        public const int FinalChapter = 4;

        /// <summary>The active chapter, 1 to <see cref="FinalChapter"/>; 0 before the journey starts.</summary>
        public int ChapterIndex { get; private set; }

        /// <summary>True in the final chapter (the Control Room), where the console is the last goal.</summary>
        public bool IsFinalChapter => ChapterIndex == FinalChapter;

        /// <summary>
        /// True once the Chapter 3 cutscene has woken the Captain (its CaptainWake Critical
        /// signal, which also fires when the cutscene is skipped). Never becomes false again.
        /// </summary>
        public bool CaptainAwake { get; private set; }

        /// <summary>The Captain's latest goal prediction; not known until the Captain has one.</summary>
        public PredictedGoal PredictedGoal { get; private set; }

        /// <summary>Wakes the Captain. Runtime only (S4's writer, from the CaptainWake cutscene signal).</summary>
        public void SetCaptainAwake() => CaptainAwake = true;

        /// <summary>Replaces the goal prediction. Runtime only, copied from the Captain's brain after its tick.</summary>
        public void SetPredictedGoal(in PredictedGoal prediction) => PredictedGoal = prediction;

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

        /// <summary>
        /// Sets the active chapter. Runtime only (S4's writer, from
        /// <c>ChapterEvents.OnChapterStarted</c>); 0 means the journey has not started.
        /// </summary>
        public void SetChapterIndex(int chapter)
        {
            if (chapter < 0 || chapter > FinalChapter)
            {
                throw new ArgumentOutOfRangeException(nameof(chapter), chapter,
                    $"Chapter must be 0 (not started) or 1 to {FinalChapter}.");
            }

            ChapterIndex = chapter;
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
