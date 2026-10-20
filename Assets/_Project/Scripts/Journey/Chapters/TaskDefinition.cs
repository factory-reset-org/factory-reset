using UnityEngine;

namespace ToyFactory.Journey.Chapters
{
    /// <summary>
    /// One chapter task. Its <see cref="TaskId"/> matches the <c>ITask.Id</c> of the prop that
    /// completes it, and its objective is shown at the <see cref="AnchorId"/> TaskAnchor.
    /// </summary>
    [CreateAssetMenu(menuName = "Factory Reset/Task Definition", fileName = "Task")]
    public sealed class TaskDefinition : ScriptableObject
    {
        [Tooltip("Stable id; must equal the prop's ITask.Id, e.g. \"ch1.lever\".")]
        [SerializeField] string taskId;

        [Tooltip("Unique number; the objective target id is 100 + this.")]
        [SerializeField, Min(1)] int objectiveId = 1;

        [Tooltip("Shown on the HUD.")]
        [SerializeField] string displayName;

        [SerializeField] TaskType type;

        [Tooltip("TaskAnchor where the objective is shown. For a Sequence task, the prefix of its item anchors " +
                 "(\"ch3.relay\" gives ch3.relay.1, .2, ...). Empty: use the prop's own position.")]
        [SerializeField] string anchorId;

        [Tooltip("Pickups count whenever they are collected, even before their chapter starts.")]
        [SerializeField] bool isPickup;

        [Tooltip("The prop is created during play (the keycard Saboteur A drops), so it is not in a scene at the start.")]
        [SerializeField] bool spawnedAtRuntime;

        [Tooltip("Sequence: number of items, put in a random order each run. Others: informational (targets to hit).")]
        [SerializeField, Min(1)] int count = 1;

        public string TaskId => taskId;
        public int ObjectiveId => objectiveId;
        public string DisplayName => displayName;
        public TaskType Type => type;
        public string AnchorId => anchorId;
        public bool IsPickup => isPickup;
        public bool SpawnedAtRuntime => spawnedAtRuntime;
        public int Count => count;

        /// <summary>Builds a definition in code (tests and the asset generator).</summary>
        public static TaskDefinition Create(string taskId, int objectiveId, string displayName, TaskType type,
            string anchorId, bool isPickup = false, bool spawnedAtRuntime = false, int count = 1)
        {
            var task = CreateInstance<TaskDefinition>();
            task.taskId = taskId;
            task.objectiveId = objectiveId;
            task.displayName = displayName;
            task.type = type;
            task.anchorId = anchorId;
            task.isPickup = isPickup;
            task.spawnedAtRuntime = spawnedAtRuntime;
            task.count = Mathf.Max(1, count);
            task.name = taskId;
            return task;
        }
    }
}
