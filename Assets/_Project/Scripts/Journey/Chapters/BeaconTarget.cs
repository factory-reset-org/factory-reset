using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Chapters
{
    /// <summary>
    /// The one objective the player should head for next: where the objective beacon stands
    /// and what the HUD names. Built by <see cref="ChapterFlow.TryGetBeaconTarget"/>.
    /// </summary>
    public readonly struct BeaconTarget
    {
        /// <summary>The objective id, the same as in the published objective list (100 + objectiveId, 200 + n, 300).</summary>
        public int Id { get; }

        /// <summary>World position of the task's anchor, or of the prop itself.</summary>
        public Vector3 Position { get; }

        public ObjectiveKind Kind { get; }

        /// <summary>The task id ("ch1.lever", "switch.2", "console").</summary>
        public string TaskId { get; }

        /// <summary>The HUD line: the task's display name, or "Restore the {area} switch".</summary>
        public string Label { get; }

        public BeaconTarget(int id, Vector3 position, ObjectiveKind kind, string taskId, string label)
        {
            Id = id;
            Position = position;
            Kind = kind;
            TaskId = taskId;
            Label = label;
        }

        public override string ToString() => $"{TaskId} ({Id}) at {Position}";
    }
}
