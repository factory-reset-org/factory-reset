using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.Interfaces;
using Object = UnityEngine.Object;

namespace ToyFactory.Journey.Chapters
{
    /// <summary>
    /// Runs the four-chapter journey in the level. All the rules live in <see cref="ChapterFlow"/>;
    /// this component only wires the scene to it: it finds the task props and anchors once in
    /// <see cref="Begin"/>, forwards prop completions and cutscene endings to the flow, and
    /// forwards the flow's events to <see cref="ChapterEvents"/> and the live objective list
    /// to <see cref="ObjectiveEvents"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Start-up.</b> S2's scene loader calls <see cref="Begin"/> after BuildGrid and
    /// SpawnAll. Nothing starts in Awake or Start unless <c>beginOnStart</c> is ticked (test
    /// scenes only), because the props live in other scenes that may not be loaded yet.</para>
    /// <para><b>Props.</b> Any MonoBehaviour implementing <see cref="ITask"/> whose Id matches a
    /// TaskDefinition, or "switch.n" for a control switch. Props created during play (the
    /// keycard) announce themselves with <see cref="TaskEvents.RaiseTaskSpawned"/>.</para>
    /// <para><b>Moving targets.</b> An objective placed on the prop itself (no anchor) is
    /// re-published, with the same id, when the prop moves more than 0.5 m or disappears.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ChapterManager : MonoBehaviour, IObjectiveLocator
    {
        [Tooltip("The four chapters, any order.")]
        [SerializeField] ChapterDefinition[] chapters = new ChapterDefinition[0];

        [Tooltip("Test scenes only: begin in Start. The game's scene loader calls Begin() itself.")]
        [SerializeField] bool beginOnStart;

        [Tooltip("Seed for the relay order. 0 = a different order every run.")]
        [SerializeField] int sequenceSeed;

        [Tooltip("A prop-placed objective is re-published once its prop moves this far (metres).")]
        [SerializeField, Min(0.05f)] float republishDistance = 0.5f;

        /// <summary>The manager in the loaded level, or null.</summary>
        public static ChapterManager Current { get; private set; }

        /// <summary>The journey's rules and state; null if the chapter data is invalid.</summary>
        public ChapterFlow Flow { get; private set; }

        /// <summary>The objective list as last published.</summary>
        public IReadOnlyList<ObjectiveTargetInfo> Objectives => _objectives;

        readonly Dictionary<string, ITaskAnchor> _anchors = new Dictionary<string, ITaskAnchor>();
        readonly Dictionary<string, ITask> _props = new Dictionary<string, ITask>();
        readonly List<Subscription> _subscriptions = new List<Subscription>();
        readonly List<ObjectiveTargetInfo> _objectives = new List<ObjectiveTargetInfo>();
        readonly List<Tracked> _tracked = new List<Tracked>();

        struct Subscription
        {
            public ITask Task;
            public Action Completed;
            public Action<float> Progress;
        }

        struct Tracked
        {
            public Component Prop;
            public Vector3 Position;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;   // domain reload is off

        void Awake()
        {
            if (Current != null && Current != this)
                Debug.LogError($"Two ChapterManagers: '{Current.name}' and '{name}'. Only the first is used.", this);
            else
                Current = this;

            try
            {
                Flow = new ChapterFlow(chapters, sequenceSeed != 0 ? sequenceSeed : Environment.TickCount);
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"Chapter data is invalid, the journey cannot run: {e.Message}", this);
                return;
            }

            Flow.ChapterStarted += ChapterEvents.RaiseChapterStarted;
            Flow.TaskCompleted += ChapterEvents.RaiseTaskCompleted;
            Flow.SwitchUnsealed += ChapterEvents.RaiseSwitchUnsealed;
            Flow.SwitchRestored += ChapterEvents.RaiseSwitchRestored;
            Flow.SequenceChosen += ChapterEvents.RaiseSequenceChosen;
            Flow.ObjectivesChanged += Publish;
        }

        void OnEnable()
        {
            CutsceneEvents.OnCutsceneEnded += HandleCutsceneEnded;
            TaskEvents.OnTaskSpawned += HandleTaskSpawned;
        }

        void OnDisable()
        {
            CutsceneEvents.OnCutsceneEnded -= HandleCutsceneEnded;
            TaskEvents.OnTaskSpawned -= HandleTaskSpawned;
        }

        void Start()
        {
            if (beginOnStart)
                Begin();
        }

        void OnDestroy()
        {
            foreach (Subscription s in _subscriptions)
            {
                s.Task.OnCompleted -= s.Completed;
                s.Task.OnProgress -= s.Progress;
            }
            _subscriptions.Clear();
            if (Current == this)
                Current = null;
        }

        /// <summary>
        /// Starts the journey: finds the props and anchors (once), reports anything missing,
        /// makes Chapter 1 active and publishes the first objectives. Call after the level, the
        /// interactables, the grid and the agents exist.
        /// </summary>
        public void Begin()
        {
            if (Flow == null)
            {
                Debug.LogError("ChapterManager.Begin: the chapter data is invalid (see the earlier error).", this);
                return;
            }
            if (Flow.HasBegun)
            {
                Debug.LogWarning("ChapterManager.Begin was called twice; the second call is ignored.", this);
                return;
            }

            FindAnchorsAndProps();
            ReportMissing();
            Flow.Begin();
        }

        // ---- IObjectiveLocator ---------------------------------------------------------

        public bool TryLocate(string taskId, string anchorId, out Vector3 position)
        {
            if (!string.IsNullOrEmpty(anchorId) && _anchors.TryGetValue(anchorId, out ITaskAnchor anchor) &&
                !(anchor is Object anchorObject && anchorObject == null))
            {
                position = anchor.Position;
                return true;
            }
            if (taskId != null && _props.TryGetValue(taskId, out ITask prop) &&
                prop is Component component && component != null)
            {
                position = component.transform.position;
                _tracked.Add(new Tracked { Prop = component, Position = position });
                return true;
            }
            position = default;
            return false;
        }

        // ---- Wiring --------------------------------------------------------------------

        void Update()
        {
            float limit = republishDistance * republishDistance;
            for (int i = 0; i < _tracked.Count; i++)
            {
                Component prop = _tracked[i].Prop;
                if (prop == null || (prop.transform.position - _tracked[i].Position).sqrMagnitude > limit)
                {
                    Publish();
                    return;
                }
            }
        }

        void Publish()
        {
            _tracked.Clear();
            Flow.BuildObjectives(_objectives, this);
            ObjectiveEvents.RaiseTargetsChanged(_objectives);
        }

        void HandleCutsceneEnded(string cutsceneId) => Flow?.CutsceneEnded(cutsceneId);

        void HandleTaskSpawned(ITask task)
        {
            if (Flow == null || task == null)
                return;
            if (Register(task) && Flow.HasBegun)
                Publish();
        }

        void FindAnchorsAndProps()
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is ITaskAnchor anchor && !string.IsNullOrEmpty(anchor.AnchorId))
                {
                    if (_anchors.ContainsKey(anchor.AnchorId))
                        Debug.LogError($"Two task anchors share the id \"{anchor.AnchorId}\".", behaviour);
                    else
                        _anchors.Add(anchor.AnchorId, anchor);
                }
                if (behaviour is ITask task)
                    Register(task);
            }
        }

        // Subscribes to a prop whose id the journey knows. Returns false for unknown or duplicate ids.
        bool Register(ITask task)
        {
            string id = task.Id;
            Object context = task as Object;
            if (string.IsNullOrEmpty(id) || !IsKnownId(id))
            {
                Debug.LogWarning($"Task prop with id \"{id}\" matches no TaskDefinition or switch; it is ignored.", context);
                return false;
            }
            if (_props.TryGetValue(id, out ITask existing) && !(existing is Object old && old == null))
            {
                if (!ReferenceEquals(existing, task))
                    Debug.LogError($"Two task props share the id \"{id}\"; only the first counts.", context);
                return false;
            }

            _props[id] = task;
            var subscription = new Subscription
            {
                Task = task,
                Completed = () => Flow.CompleteTask(id),
                Progress = progress => Flow.ReportProgress(id, progress)
            };
            task.OnCompleted += subscription.Completed;
            task.OnProgress += subscription.Progress;
            _subscriptions.Add(subscription);
            return true;
        }

        bool IsKnownId(string id)
        {
            if (id.StartsWith(ChapterFlow.SwitchTaskPrefix, StringComparison.Ordinal))
            {
                foreach (int number in Flow.SwitchNumbers)
                    if (id == ChapterFlow.SwitchTaskPrefix + number)
                        return true;
                return false;
            }
            foreach (TaskDefinition task in Flow.AllTasks)
                if (task.TaskId == id)
                    return true;
            return false;
        }

        // Logs every task or switch with no prop, and every anchor id the data names that the
        // level does not have, so a missing piece shows up at the start of a run instead of
        // as a chapter that can never finish.
        void ReportMissing()
        {
            foreach (TaskDefinition task in Flow.AllTasks)
            {
                if (!task.SpawnedAtRuntime && !_props.ContainsKey(task.TaskId))
                    Debug.LogError($"No task prop with id \"{task.TaskId}\" ({task.DisplayName}); chapter cannot finish.", this);
                if (string.IsNullOrEmpty(task.AnchorId))
                    continue;
                if (task.Type == TaskType.Sequence)
                {
                    for (int item = 1; item <= task.Count; item++)
                        if (!_anchors.ContainsKey(task.AnchorId + "." + item))
                            Debug.LogError($"No task anchor \"{task.AnchorId}.{item}\" for \"{task.TaskId}\".", this);
                }
                else if (!_anchors.ContainsKey(task.AnchorId))
                {
                    Debug.LogError($"No task anchor \"{task.AnchorId}\" for \"{task.TaskId}\".", this);
                }
            }
            foreach (int number in Flow.SwitchNumbers)
            {
                string id = ChapterFlow.SwitchTaskPrefix + number;
                if (!_props.ContainsKey(id))
                    Debug.LogError($"No switch prop with id \"{id}\"; switch {number} can never be restored.", this);
                if (!_anchors.ContainsKey(id))
                    Debug.LogError($"No task anchor \"{id}\" for switch {number}.", this);
            }
        }
    }
}
