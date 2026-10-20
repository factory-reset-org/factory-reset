using System;
using System.Collections.Generic;
using ToyFactory.Interfaces;

namespace ToyFactory.UI
{
    /// <summary>The state of one switch lamp on the HUD.</summary>
    public enum LampState
    {
        /// <summary>The chapter's tasks are not done: the switch is sealed.</summary>
        Sealed,

        /// <summary>The tasks are done and the switch can be restored.</summary>
        Ready,

        /// <summary>The switch is restored.</summary>
        Restored
    }

    /// <summary>One line of the objectives panel.</summary>
    public readonly struct HudTaskRow
    {
        /// <summary>The task id, matching <c>ChapterEvents.OnTaskCompleted</c>.</summary>
        public string Id { get; }

        /// <summary>The text shown.</summary>
        public string Text { get; }

        /// <summary>True once the task is done.</summary>
        public bool Done { get; }

        /// <summary>How many parts the task has (targets to hit, relays to press); 1 for a plain task.</summary>
        public int Total { get; }

        /// <summary>How many of the parts are done so far, 0 to <see cref="Total"/>.</summary>
        public int Count { get; }

        /// <summary>True for a task with more than one part, so a count such as "1/3" is shown.</summary>
        public bool HasCount => Total > 1;

        /// <summary>Creates a row for a task with one part.</summary>
        public HudTaskRow(string id, string text, bool done) : this(id, text, done, 1, done ? 1 : 0)
        {
        }

        /// <summary>Creates a row for a task with <paramref name="total"/> parts, <paramref name="count"/> of them done.</summary>
        public HudTaskRow(string id, string text, bool done, int total, int count)
        {
            Id = id;
            Text = text;
            Done = done;
            Total = total < 1 ? 1 : total;
            Count = count < 0 ? 0 : count > Total ? Total : count;
        }

        /// <summary>A copy with the count changed.</summary>
        public HudTaskRow WithCount(int count) => new HudTaskRow(Id, Text, Done, Total, count);

        /// <summary>A copy marked done, with every part counted.</summary>
        public HudTaskRow AsDone() => new HudTaskRow(Id, Text, true, Total, Total);
    }

    /// <summary>
    /// What the objectives panel and the switch lamps show, as plain data: the chapter, its task
    /// rows and the three lamps. The presenter feeds it from the chapter events and draws from it;
    /// <see cref="Version"/> goes up on every change so a view redraws only when something moved.
    /// </summary>
    public sealed class HudModel
    {
        /// <summary>Prefix of the id of the row that asks the player to restore the chapter's switch.</summary>
        public const string SwitchRowPrefix = "switch.";

        readonly List<HudTaskRow> _rows = new List<HudTaskRow>();
        readonly LampState[] _lamps = new LampState[ChapterEvents.SwitchCount];

        /// <summary>The chapter shown, 1 to 4; 0 before the journey starts.</summary>
        public int Chapter { get; private set; }

        /// <summary>The chapter's title.</summary>
        public string Title { get; private set; } = string.Empty;

        /// <summary>The chapter's subtitle.</summary>
        public string Subtitle { get; private set; } = string.Empty;

        /// <summary>The panel's rows: the chapter's tasks, then the switch row if it has one.</summary>
        public IReadOnlyList<HudTaskRow> Rows => _rows;

        /// <summary>Goes up on every change.</summary>
        public int Version { get; private set; }

        /// <summary>The switch the chapter asks the player to restore, or 0 if it has none.</summary>
        public int SwitchNumber { get; private set; }

        /// <summary>How many switches are restored, 0 to 3.</summary>
        public int SwitchesRestored
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _lamps.Length; i++)
                {
                    if (_lamps[i] == LampState.Restored)
                        count++;
                }

                return count;
            }
        }

        /// <summary>How many rows are done.</summary>
        public int RowsDone
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i].Done)
                        count++;
                }

                return count;
            }
        }

        /// <summary>The state of lamp <paramref name="number"/> (1 to 3).</summary>
        public LampState Lamp(int number)
        {
            if (number < 1 || number > _lamps.Length)
                throw new ArgumentOutOfRangeException(nameof(number));
            return _lamps[number - 1];
        }

        /// <summary>
        /// Starts showing a chapter. <paramref name="tasks"/> are its task rows in order;
        /// <paramref name="switchNumber"/> (0 for none) adds the "restore the switch" row.
        /// </summary>
        public void StartChapter(int chapter, string title, string subtitle, IReadOnlyList<HudTaskRow> tasks, int switchNumber)
        {
            if (chapter < 1 || chapter > ChapterEvents.ChapterCount)
                throw new ArgumentOutOfRangeException(nameof(chapter));
            if (tasks == null)
                throw new ArgumentNullException(nameof(tasks));
            if (switchNumber < 0 || switchNumber > ChapterEvents.SwitchCount)
                throw new ArgumentOutOfRangeException(nameof(switchNumber));

            Chapter = chapter;
            CurrentTaskId = null;
            SequenceTaskId = null;
            _order.Clear();
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            SwitchNumber = switchNumber;

            _rows.Clear();
            for (int i = 0; i < tasks.Count; i++)
                _rows.Add(tasks[i]);
            if (switchNumber > 0)
                _rows.Add(new HudTaskRow(SwitchRowPrefix + switchNumber, $"Restore switch {switchNumber}", _lamps[switchNumber - 1] == LampState.Restored));

            Version++;
        }

        /// <summary>Marks a task done. False if the chapter has no such row or it was already done.</summary>
        public bool CompleteTask(string taskId)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Id == taskId && !_rows[i].Done)
                {
                    _rows[i] = _rows[i].AsDone();
                    Version++;
                    return true;
                }
            }

            return false;
        }

        /// <summary>The id of the task the objective arrow points at, or null.</summary>
        public string CurrentTaskId { get; private set; }

        /// <summary>The task whose parts are shown as numbered chips (the relays), or null.</summary>
        public string SequenceTaskId { get; private set; }

        /// <summary>The order the chips must be used in, 1-based item numbers; empty if there is no sequence.</summary>
        public IReadOnlyList<int> SequenceOrder => _order;

        readonly List<int> _order = new List<int>();

        /// <summary>Sets the current task (null for none).</summary>
        public void SetCurrentTask(string taskId)
        {
            if (CurrentTaskId == taskId)
                return;

            CurrentTaskId = taskId;
            Version++;
        }

        /// <summary>Shows the chips for an in-order task. An empty order clears them.</summary>
        public void SetSequence(string taskId, IReadOnlyList<int> order)
        {
            _order.Clear();
            if (order != null)
                _order.AddRange(order);
            SequenceTaskId = _order.Count > 0 ? taskId : null;
            Version++;
        }

        /// <summary>
        /// A prop reported progress in [0, 1]: the row's count becomes the parts that fraction covers.
        /// False if the chapter has no such row, the row is done, or the count did not change.
        /// </summary>
        public bool SetProgress(string taskId, float progress)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Id != taskId)
                    continue;

                if (_rows[i].Done)
                    return false;

                int count = HudMath.PartsDone(progress, _rows[i].Total);
                if (count == _rows[i].Count)
                    return false;

                _rows[i] = _rows[i].WithCount(count);
                Version++;
                return true;
            }

            return false;
        }

        /// <summary>How many chips of the sequence are used so far (the row's count), 0 if there is none.</summary>
        public int SequenceStep
        {
            get
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i].Id == SequenceTaskId)
                        return _rows[i].Done ? _order.Count : _rows[i].Count;
                }

                return 0;
            }
        }

        /// <summary>Sets lamp <paramref name="number"/> (1 to 3). Restoring the chapter's switch also ticks its row.</summary>
        public void SetLamp(int number, LampState state)
        {
            if (number < 1 || number > _lamps.Length)
                throw new ArgumentOutOfRangeException(nameof(number));

            if (_lamps[number - 1] == state)
                return;

            _lamps[number - 1] = state;
            if (state == LampState.Restored)
                CompleteTask(SwitchRowPrefix + number);
            Version++;
        }

        /// <summary>
        /// Whether the HUD is drawn: while playing or paused, but not in a cutscene, on the title
        /// screen or on the results screen.
        /// </summary>
        public static bool IsVisible(GameState state, bool cutsceneActive) =>
            !cutsceneActive && (state == GameState.Playing || state == GameState.Paused);
    }
}
