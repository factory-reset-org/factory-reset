using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.FSM;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Chapters
{
    /// <summary>Where a chapter is in its life: Locked → Active → TasksDone → SwitchRestored → Transition → Done.</summary>
    public enum ChapterPhase
    {
        /// <summary>An earlier chapter is still running.</summary>
        Locked,
        /// <summary>Its tasks can be done (pickups count in any phase).</summary>
        Active,
        /// <summary>Every task is done; the switch is unsealed and waiting to be restored.</summary>
        TasksDone,
        /// <summary>The switch was restored (passed straight through).</summary>
        SwitchRestored,
        /// <summary>Waiting for the chapter's cutscene to end (or be skipped).</summary>
        Transition,
        /// <summary>Finished; the next chapter is Active.</summary>
        Done
    }

    /// <summary>Finds world positions for objectives. Implemented by <see cref="ChapterManager"/>; faked in tests.</summary>
    public interface IObjectiveLocator
    {
        /// <summary>
        /// Position of the objective for <paramref name="taskId"/>: the anchor
        /// <paramref name="anchorId"/> if it is set and exists, otherwise the prop's own position.
        /// False when neither exists (a keycard not dropped yet).
        /// </summary>
        bool TryLocate(string taskId, string anchorId, out Vector3 position);
    }

    /// <summary>
    /// The journey's rules, with no scene or prop in sight: which tasks count, when a switch
    /// unseals, when the next chapter starts, and which objectives are live. Each chapter runs
    /// its own <see cref="StateMachine{T}"/> on the shared FSM framework.
    /// <see cref="ChapterManager"/> feeds it prop and cutscene events and forwards its events
    /// to <see cref="ChapterEvents"/> and <see cref="ObjectiveEvents"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Rules.</b> Tasks of the active chapter count in any order. Pickups (fuses,
    /// keycard) count whenever they are collected, even before their chapter starts. Other
    /// tasks of a later chapter are ignored until it is active. A task completes once. A
    /// switch can only be restored after its chapter's tasks are all done, and the next
    /// chapter starts only when the cutscene after that switch has ended (a skip ends it too).</para>
    /// <para><b>Objectives</b> (ids stable while live): every incomplete task of the active
    /// chapter at 100 + objectiveId; every switch not yet restored, sealed or not, at 200 + n;
    /// the console at 300 until it is used. Never batteries (S2 owns those).</para>
    /// </remarks>
    public sealed class ChapterFlow
    {
        public const int TaskTargetIdBase = 100;
        public const int SwitchTargetIdBase = 200;
        public const int ConsoleTargetId = 300;

        /// <summary>A switch prop's <c>ITask.Id</c> is this prefix plus its number, e.g. "switch.2".</summary>
        public const string SwitchTaskPrefix = "switch.";

        /// <summary>A chapter became active (1-based).</summary>
        public event Action<int> ChapterStarted;
        /// <summary>A task completed, by task id. Raised once per task.</summary>
        public event Action<string> TaskCompleted;
        /// <summary>A switch's chapter finished its tasks: open its cage.</summary>
        public event Action<int> SwitchUnsealed;
        /// <summary>A switch was restored.</summary>
        public event Action<int> SwitchRestored;
        /// <summary>An in-order task's order was chosen (in <see cref="Begin"/>).</summary>
        public event Action<string, IReadOnlyList<int>> SequenceChosen;
        /// <summary>A chapter changed phase: chapter, new phase.</summary>
        public event Action<int, ChapterPhase> PhaseChanged;
        /// <summary>The live objective list may have changed; rebuild it with <see cref="BuildObjectives"/>.</summary>
        public event Action ObjectivesChanged;

        sealed class Run
        {
            public ChapterDefinition Definition;
            public StateMachine<Run> Machine;
            public ChapterPhase Phase = ChapterPhase.Locked;
            public bool Unlocked;
            public bool SwitchFlipped;
            public bool CutsceneEnded;
            public Run Next;
        }

        sealed class PhaseState : IState<Run>
        {
            readonly ChapterFlow _flow;
            public readonly ChapterPhase Phase;

            public PhaseState(ChapterFlow flow, ChapterPhase phase)
            {
                _flow = flow;
                Phase = phase;
            }

            public void Enter(Run run) => _flow.OnEntered(run, Phase);
            public void Tick(Run run) { }
            public void Exit(Run run) { }
            public override string ToString() => Phase.ToString();
        }

        readonly Run[] _runs;
        readonly Dictionary<string, TaskDefinition> _tasks = new Dictionary<string, TaskDefinition>();
        readonly Dictionary<string, Run> _taskRun = new Dictionary<string, Run>();
        readonly HashSet<string> _completed = new HashSet<string>();
        readonly Dictionary<string, int[]> _orders = new Dictionary<string, int[]>();
        readonly Dictionary<string, int> _sequenceStep = new Dictionary<string, int>();
        readonly System.Random _random;
        readonly TaskDefinition _console;
        bool _begun;
        bool _settling;
        bool _dirty;

        /// <param name="chapters">The chapters, any order; indices must run 1..N.</param>
        /// <param name="seed">Seed for the per-run random orders (the relays).</param>
        public ChapterFlow(IReadOnlyList<ChapterDefinition> chapters, int seed)
        {
            if (chapters == null || chapters.Count == 0)
                throw new ArgumentException("At least one chapter is needed.", nameof(chapters));
            _random = new System.Random(seed);

            var sorted = new List<ChapterDefinition>(chapters);
            foreach (ChapterDefinition chapter in sorted)
                if (chapter == null)
                    throw new ArgumentException("A chapter is missing.", nameof(chapters));
            sorted.Sort((a, b) => a.Index.CompareTo(b.Index));

            List<Transition<Run>> transitions = BuildTransitions(out IState<Run> locked);
            _runs = new Run[sorted.Count];
            var objectiveIds = new HashSet<int>();
            var switches = new HashSet<int>();
            for (int i = 0; i < sorted.Count; i++)
            {
                ChapterDefinition chapter = sorted[i];
                if (chapter.Index != i + 1)
                    throw new ArgumentException($"Chapter indices must run 1..{sorted.Count}; found {chapter.Index} at position {i + 1}.", nameof(chapters));
                if (chapter.HasSwitch && !switches.Add(chapter.SwitchNumber))
                    throw new ArgumentException($"Switch {chapter.SwitchNumber} is used by two chapters.", nameof(chapters));

                var run = new Run { Definition = chapter };
                run.Machine = new StateMachine<Run>(locked, transitions);
                _runs[i] = run;
                if (i > 0)
                    _runs[i - 1].Next = run;

                foreach (TaskDefinition task in chapter.Tasks)
                {
                    if (task == null || string.IsNullOrEmpty(task.TaskId))
                        throw new ArgumentException($"Chapter {chapter.Index} has a task with no id.", nameof(chapters));
                    if (task.TaskId.StartsWith(SwitchTaskPrefix, StringComparison.Ordinal))
                        throw new ArgumentException($"Task id \"{task.TaskId}\" is reserved for switches.", nameof(chapters));
                    if (_tasks.ContainsKey(task.TaskId))
                        throw new ArgumentException($"Task id \"{task.TaskId}\" is used twice.", nameof(chapters));
                    if (!objectiveIds.Add(task.ObjectiveId))
                        throw new ArgumentException($"Objective id {task.ObjectiveId} is used twice.", nameof(chapters));
                    _tasks.Add(task.TaskId, task);
                    _taskRun.Add(task.TaskId, run);
                    if (task.TaskId == ChapterEvents.ConsoleTaskId)
                        _console = task;
                }
            }
        }

        // ---- Inputs ---------------------------------------------------------------------

        /// <summary>Starts the journey: picks the in-order tasks' orders and makes Chapter 1 active. Once only.</summary>
        public bool Begin()
        {
            if (_begun)
                return false;
            _begun = true;

            foreach (Run run in _runs)
                foreach (TaskDefinition task in run.Definition.Tasks)
                    if (task.Type == TaskType.Sequence)
                    {
                        int[] order = RandomOrder(task.Count);
                        _orders[task.TaskId] = order;
                        _sequenceStep[task.TaskId] = 0;
                        SequenceChosen?.Invoke(task.TaskId, order);
                    }

            _runs[0].Unlocked = true;
            Settle();
            ObjectivesChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// A prop reported completion: a task id, or "switch.n". Returns false when it is
        /// ignored (unknown, already done, a later chapter's non-pickup, a sealed switch).
        /// </summary>
        public bool CompleteTask(string taskId)
        {
            if (!_begun || string.IsNullOrEmpty(taskId))
                return false;
            if (taskId.StartsWith(SwitchTaskPrefix, StringComparison.Ordinal))
                return RestoreSwitch(taskId);
            if (!_tasks.TryGetValue(taskId, out TaskDefinition task) || _completed.Contains(taskId))
                return false;
            if (!task.IsPickup && _taskRun[taskId].Phase != ChapterPhase.Active)
                return false;

            _completed.Add(taskId);
            TaskCompleted?.Invoke(taskId);
            Settle();
            ObjectivesChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// A prop reported progress in [0, 1]. Only in-order tasks use it: it moves the
        /// objective to the next item (back to the first after a wrong press resets it).
        /// </summary>
        public bool ReportProgress(string taskId, float progress)
        {
            if (!_begun || taskId == null || !_orders.TryGetValue(taskId, out int[] order))
                return false;
            int step = Mathf.Clamp(Mathf.FloorToInt(progress * order.Length + 1e-3f), 0, order.Length);
            if (_sequenceStep[taskId] == step)
                return false;
            _sequenceStep[taskId] = step;
            ObjectivesChanged?.Invoke();
            return true;
        }

        /// <summary>A cutscene ended or was skipped. Starts the next chapter if it was the one being waited for.</summary>
        public bool CutsceneEnded(string cutsceneId)
        {
            if (!_begun || string.IsNullOrEmpty(cutsceneId))
                return false;
            foreach (Run run in _runs)
            {
                if (run.Phase != ChapterPhase.Transition || run.Definition.CutsceneId != cutsceneId)
                    continue;
                run.CutsceneEnded = true;
                Settle();
                ObjectivesChanged?.Invoke();
                return true;
            }
            return false;
        }

        bool RestoreSwitch(string taskId)
        {
            if (!int.TryParse(taskId.Substring(SwitchTaskPrefix.Length), out int number))
                return false;
            foreach (Run run in _runs)
            {
                if (run.Definition.SwitchNumber != number)
                    continue;
                if (run.Phase != ChapterPhase.TasksDone || run.SwitchFlipped)
                    return false;   // sealed, or already restored
                run.SwitchFlipped = true;
                Settle();
                ObjectivesChanged?.Invoke();
                return true;
            }
            return false;
        }

        // ---- Queries --------------------------------------------------------------------

        public bool HasBegun => _begun;

        /// <summary>The latest chapter that has started (1-based), 0 before <see cref="Begin"/>.</summary>
        public int CurrentChapter
        {
            get
            {
                for (int i = _runs.Length - 1; i >= 0; i--)
                    if (_runs[i].Phase != ChapterPhase.Locked)
                        return i + 1;
                return 0;
            }
        }

        /// <summary>Definition of <see cref="CurrentChapter"/>, or null before <see cref="Begin"/>.</summary>
        public ChapterDefinition CurrentDefinition => CurrentChapter > 0 ? _runs[CurrentChapter - 1].Definition : null;

        /// <summary>True once every chapter is Done.</summary>
        public bool IsFinished => _runs[_runs.Length - 1].Phase == ChapterPhase.Done;

        public int ChapterCount => _runs.Length;

        public ChapterPhase GetPhase(int chapter)
        {
            if (chapter < 1 || chapter > _runs.Length)
                throw new ArgumentOutOfRangeException(nameof(chapter));
            return _runs[chapter - 1].Phase;
        }

        public ChapterDefinition GetDefinition(int chapter)
        {
            if (chapter < 1 || chapter > _runs.Length)
                throw new ArgumentOutOfRangeException(nameof(chapter));
            return _runs[chapter - 1].Definition;
        }

        public bool IsTaskComplete(string taskId) => taskId != null && _completed.Contains(taskId);

        /// <summary>Every task id, for the manager to match props against.</summary>
        public IEnumerable<TaskDefinition> AllTasks => _tasks.Values;

        /// <summary>Switch numbers used by the chapters.</summary>
        public IEnumerable<int> SwitchNumbers
        {
            get
            {
                foreach (Run run in _runs)
                    if (run.Definition.HasSwitch)
                        yield return run.Definition.SwitchNumber;
            }
        }

        /// <summary>The order chosen for an in-order task (1-based item numbers).</summary>
        public bool TryGetSequenceOrder(string taskId, out IReadOnlyList<int> order)
        {
            if (taskId != null && _orders.TryGetValue(taskId, out int[] array))
            {
                order = array;
                return true;
            }
            order = null;
            return false;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the live objectives (see the class remarks).
        /// Objectives the locator cannot place are left out.
        /// </summary>
        public void BuildObjectives(List<ObjectiveTargetInfo> into, IObjectiveLocator locator)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (locator == null) throw new ArgumentNullException(nameof(locator));
            into.Clear();
            if (!_begun)
                return;

            Vector3 position;
            foreach (Run run in _runs)
            {
                if (run.Phase != ChapterPhase.Active)
                    continue;
                foreach (TaskDefinition task in run.Definition.Tasks)
                {
                    if (_completed.Contains(task.TaskId) || task == _console)
                        continue;   // the console is listed once, as the Console target
                    if (locator.TryLocate(task.TaskId, AnchorFor(task), out position))
                        into.Add(new ObjectiveTargetInfo(TaskTargetIdBase + task.ObjectiveId, position, ObjectiveKind.Task));
                }
            }

            foreach (Run run in _runs)
            {
                if (!run.Definition.HasSwitch || run.SwitchFlipped)
                    continue;
                string id = SwitchTaskPrefix + run.Definition.SwitchNumber;
                if (locator.TryLocate(id, id, out position))
                    into.Add(new ObjectiveTargetInfo(SwitchTargetIdBase + run.Definition.SwitchNumber, position, ObjectiveKind.Switch));
            }

            if (_console != null && !_completed.Contains(_console.TaskId) &&
                locator.TryLocate(_console.TaskId, _console.AnchorId, out position))
                into.Add(new ObjectiveTargetInfo(ConsoleTargetId, position, ObjectiveKind.Console));
        }

        /// <summary>
        /// The single target the objective beacon marks: the first incomplete task of the
        /// active chapter, in the chapter's data order; once all its tasks are done, its
        /// unsealed switch. Chapter 4 lists the cores before the console, so the console comes
        /// last. A task the locator cannot place yet (the keycard before it drops) is passed
        /// over for the next one. False before <see cref="Begin"/>, between chapters (while the
        /// cutscene plays) and once the journey is finished.
        /// </summary>
        public bool TryGetBeaconTarget(IObjectiveLocator locator, out BeaconTarget target)
        {
            if (locator == null) throw new ArgumentNullException(nameof(locator));
            target = default;
            if (!_begun)
                return false;

            Vector3 position;
            foreach (Run run in _runs)
            {
                if (run.Phase == ChapterPhase.Active)
                {
                    foreach (TaskDefinition task in run.Definition.Tasks)
                    {
                        if (_completed.Contains(task.TaskId) ||
                            !locator.TryLocate(task.TaskId, AnchorFor(task), out position))
                            continue;
                        bool console = task == _console;
                        target = new BeaconTarget(console ? ConsoleTargetId : TaskTargetIdBase + task.ObjectiveId, position,
                            console ? ObjectiveKind.Console : ObjectiveKind.Task, task.TaskId, task.DisplayName);
                        return true;
                    }
                    return false;
                }

                if (run.Phase == ChapterPhase.TasksDone && run.Definition.HasSwitch && !run.SwitchFlipped)
                {
                    string id = SwitchTaskPrefix + run.Definition.SwitchNumber;
                    if (!locator.TryLocate(id, id, out position))
                        return false;
                    target = new BeaconTarget(SwitchTargetIdBase + run.Definition.SwitchNumber, position,
                        ObjectiveKind.Switch, id, $"Restore the {run.Definition.Area} switch");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The anchor that shows <paramref name="task"/>'s objective now: for an in-order task,
        /// the next item's anchor ("ch3.relay" + "." + item); otherwise its own anchor id.
        /// </summary>
        public string AnchorFor(TaskDefinition task)
        {
            if (task.Type == TaskType.Sequence && _orders.TryGetValue(task.TaskId, out int[] order))
            {
                int step = _sequenceStep[task.TaskId];
                if (step < order.Length)
                    return task.AnchorId + "." + order[step];
            }
            return task.AnchorId;
        }

        // ---- The chapter FSM ------------------------------------------------------------

        List<Transition<Run>> BuildTransitions(out IState<Run> locked)
        {
            var lockedState = new PhaseState(this, ChapterPhase.Locked);
            var active = new PhaseState(this, ChapterPhase.Active);
            var tasksDone = new PhaseState(this, ChapterPhase.TasksDone);
            var restored = new PhaseState(this, ChapterPhase.SwitchRestored);
            var transition = new PhaseState(this, ChapterPhase.Transition);
            var done = new PhaseState(this, ChapterPhase.Done);
            locked = lockedState;

            return new List<Transition<Run>>
            {
                new Transition<Run>(lockedState, active, r => r.Unlocked, 0),
                new Transition<Run>(active, tasksDone, AllTasksDone, 0),
                new Transition<Run>(tasksDone, restored, r => r.SwitchFlipped, 1),
                new Transition<Run>(tasksDone, done, r => !r.Definition.HasSwitch, 0),   // Chapter 4
                new Transition<Run>(restored, transition, r => true, 0),
                new Transition<Run>(transition, done,
                    r => r.CutsceneEnded || string.IsNullOrEmpty(r.Definition.CutsceneId), 0)
            };
        }

        bool AllTasksDone(Run run)
        {
            foreach (TaskDefinition task in run.Definition.Tasks)
                if (!_completed.Contains(task.TaskId))
                    return false;
            return true;
        }

        void OnEntered(Run run, ChapterPhase phase)
        {
            run.Phase = phase;
            int chapter = run.Definition.Index;
            PhaseChanged?.Invoke(chapter, phase);
            switch (phase)
            {
                case ChapterPhase.Active:
                    ChapterStarted?.Invoke(chapter);
                    break;
                case ChapterPhase.TasksDone:
                    if (run.Definition.HasSwitch)
                        SwitchUnsealed?.Invoke(run.Definition.SwitchNumber);
                    break;
                case ChapterPhase.SwitchRestored:
                    SwitchRestored?.Invoke(run.Definition.SwitchNumber);
                    break;
                case ChapterPhase.Done:
                    if (run.Next != null)
                        run.Next.Unlocked = true;
                    _dirty = true;
                    break;
            }
        }

        // Ticks every chapter's machine until nothing moves. A chapter can pass several
        // phases in one call (all its pickups already collected), and finishing one unlocks
        // the next. A listener that reports back from inside an event (re-entry) just marks
        // the flow dirty, and the outer loop picks the change up.
        void Settle()
        {
            if (_settling)
            {
                _dirty = true;
                return;
            }
            _settling = true;
            try
            {
                do
                {
                    _dirty = false;
                    foreach (Run run in _runs)
                    {
                        for (int guard = 0; guard < 8; guard++)
                        {
                            IState<Run> before = run.Machine.Current;
                            run.Machine.Tick(run);
                            if (run.Machine.Current == before)
                                break;
                        }
                    }
                } while (_dirty);
            }
            finally
            {
                _settling = false;
            }
        }

        int[] RandomOrder(int count)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++)
                order[i] = i + 1;
            for (int i = count - 1; i > 0; i--)   // Fisher-Yates
            {
                int j = _random.Next(i + 1);
                int swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }
            return order;
        }
    }
}
