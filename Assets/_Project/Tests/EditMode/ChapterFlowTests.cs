using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests.EditMode
{
    public class ChapterFlowTests
    {
        // Finds positions from a dictionary: anchors by anchor id, props by task id.
        sealed class FakeLocator : IObjectiveLocator
        {
            public readonly Dictionary<string, Vector3> Known = new Dictionary<string, Vector3>();

            public bool TryLocate(string taskId, string anchorId, out Vector3 position)
            {
                if (!string.IsNullOrEmpty(anchorId) && Known.TryGetValue(anchorId, out position))
                    return true;
                return Known.TryGetValue(taskId, out position);
            }
        }

        static readonly string[] Chapter1Tasks = { "ch1.lever", "ch1.plate", "ch1.fuse.1", "ch1.fuse.2", "ch1.fuse.3" };
        static readonly string[] Chapter4Tasks = { "ch4.core.1", "ch4.core.2", "ch4.core.3", ChapterEvents.ConsoleTaskId };

        readonly List<Object> _assets = new List<Object>();
        ChapterDefinition[] _chapters;
        ChapterFlow _flow;
        FakeLocator _locator;
        List<string> _log;
        List<ObjectiveTargetInfo> _objectives;
        int _objectiveChanges;

        [SetUp]
        public void SetUp()
        {
            _chapters = BuildChapters();
            _flow = new ChapterFlow(_chapters, seed: 7);
            _log = new List<string>();
            _objectiveChanges = 0;
            _flow.ChapterStarted += n => _log.Add("start " + n);
            _flow.TaskCompleted += id => _log.Add("task " + id);
            _flow.SwitchUnsealed += n => _log.Add("unseal " + n);
            _flow.SwitchRestored += n => _log.Add("restored " + n);
            _flow.ObjectivesChanged += () => _objectiveChanges++;

            _locator = new FakeLocator();
            string[] anchors =
            {
                "ch1.lever", "ch1.plate", "ch1.fuse.1", "ch1.fuse.2", "ch1.fuse.3", "ch2.terminal",
                "ch3.relay.1", "ch3.relay.2", "ch3.relay.3", "ch4.core.1", "ch4.core.2", "ch4.core.3",
                "ch4.console", "switch.1", "switch.2", "switch.3"
            };
            for (int i = 0; i < anchors.Length; i++)
                _locator.Known[anchors[i]] = new Vector3(i, 0f, 0f);
            _locator.Known["ch2.targets"] = new Vector3(50f, 0f, 0f);   // the prop itself: no anchor
            _objectives = new List<ObjectiveTargetInfo>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object asset in _assets)
                Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        // The real journey's shape (see Data/Chapters).
        ChapterDefinition[] BuildChapters()
        {
            TaskDefinition T(string id, int objective, TaskType type, string anchor, bool pickup = false,
                bool spawned = false, int count = 1)
            {
                TaskDefinition task = TaskDefinition.Create(id, objective, id, type, anchor, pickup, spawned, count);
                _assets.Add(task);
                return task;
            }

            ChapterDefinition C(int index, int switchNumber, string cutscene, params TaskDefinition[] tasks)
            {
                ChapterDefinition chapter = ChapterDefinition.Create(index, "Chapter " + index, "", "Area", tasks, switchNumber, cutscene);
                _assets.Add(chapter);
                return chapter;
            }

            return new[]
            {
                C(1, 1, "ch2",
                    T("ch1.lever", 1, TaskType.Interact, "ch1.lever"),
                    T("ch1.plate", 2, TaskType.BoxOnPlate, "ch1.plate"),
                    T("ch1.fuse.1", 3, TaskType.Collect, "ch1.fuse.1", pickup: true),
                    T("ch1.fuse.2", 4, TaskType.Collect, "ch1.fuse.2", pickup: true),
                    T("ch1.fuse.3", 5, TaskType.Collect, "ch1.fuse.3", pickup: true)),
                C(2, 2, "ch3",
                    T("ch2.targets", 6, TaskType.TimedHits, "", count: 4),
                    T("ch2.terminal", 7, TaskType.Hold, "ch2.terminal")),
                C(3, 3, "ch4",
                    T("ch3.keycard", 8, TaskType.Collect, "", pickup: true, spawned: true),
                    T("ch3.relays", 9, TaskType.Sequence, "ch3.relay", count: 3)),
                C(4, 0, "",
                    T("ch4.core.1", 10, TaskType.Destroy, "ch4.core.1"),
                    T("ch4.core.2", 11, TaskType.Destroy, "ch4.core.2"),
                    T("ch4.core.3", 12, TaskType.Destroy, "ch4.core.3"),
                    T(ChapterEvents.ConsoleTaskId, 13, TaskType.HoldAt, "ch4.console"))
            };
        }

        void CompleteAll(params string[] ids)
        {
            foreach (string id in ids)
                Assert.IsTrue(_flow.CompleteTask(id), id);
        }

        void FinishChapter1()
        {
            CompleteAll(Chapter1Tasks);
            CompleteAll("switch.1");
            Assert.IsTrue(_flow.CutsceneEnded("ch2"));
        }

        void FinishChapter2()
        {
            CompleteAll("ch2.targets", "ch2.terminal", "switch.2");
            Assert.IsTrue(_flow.CutsceneEnded("ch3"));
        }

        List<int> ObjectiveIds()
        {
            _flow.BuildObjectives(_objectives, _locator);
            var ids = new List<int>();
            foreach (ObjectiveTargetInfo target in _objectives)
                ids.Add(target.Id);
            return ids;
        }

        ObjectiveTargetInfo Objective(int id)
        {
            _flow.BuildObjectives(_objectives, _locator);
            foreach (ObjectiveTargetInfo target in _objectives)
                if (target.Id == id)
                    return target;
            Assert.Fail($"Objective {id} is not live.");
            return default;
        }

        // ---- Starting -------------------------------------------------------------------

        [Test]
        public void BeginStartsChapterOneOnce()
        {
            Assert.AreEqual(0, _flow.CurrentChapter);

            Assert.IsTrue(_flow.Begin());
            Assert.IsFalse(_flow.Begin());

            CollectionAssert.AreEqual(new[] { "start 1" }, _log);
            Assert.AreEqual(1, _flow.CurrentChapter);
            Assert.AreEqual(ChapterPhase.Active, _flow.GetPhase(1));
            Assert.AreEqual(ChapterPhase.Locked, _flow.GetPhase(2));
        }

        [Test]
        public void NothingCountsBeforeBegin()
        {
            Assert.IsFalse(_flow.CompleteTask("ch1.lever"));
            Assert.IsFalse(_flow.CompleteTask("ch1.fuse.1"));
            CollectionAssert.IsEmpty(ObjectiveIds());
        }

        // ---- Tasks and the switch lock --------------------------------------------------

        [Test]
        public void TasksCountInAnyOrderAndUnsealTheSwitchOnceAllAreDone()
        {
            _flow.Begin();
            CompleteAll("ch1.fuse.3", "ch1.plate", "ch1.fuse.1", "ch1.lever");
            Assert.AreEqual(ChapterPhase.Active, _flow.GetPhase(1));
            CollectionAssert.DoesNotContain(_log, "unseal 1");

            CompleteAll("ch1.fuse.2");

            Assert.AreEqual(ChapterPhase.TasksDone, _flow.GetPhase(1));
            Assert.AreEqual("unseal 1", _log[_log.Count - 1]);
            Assert.AreEqual(1, _log.FindAll(entry => entry == "unseal 1").Count);
        }

        [Test]
        public void ASealedSwitchCannotBeRestored()
        {
            _flow.Begin();
            CompleteAll("ch1.lever", "ch1.plate");

            Assert.IsFalse(_flow.CompleteTask("switch.1"));
            Assert.IsFalse(_flow.CompleteTask("switch.2"));
            Assert.AreEqual(ChapterPhase.Active, _flow.GetPhase(1));
        }

        [Test]
        public void ATaskCompletesOnlyOnce()
        {
            _flow.Begin();

            Assert.IsTrue(_flow.CompleteTask("ch1.lever"));
            Assert.IsFalse(_flow.CompleteTask("ch1.lever"));

            Assert.AreEqual(1, _log.FindAll(entry => entry == "task ch1.lever").Count);
        }

        [Test]
        public void UnknownTaskIdsAreIgnored()
        {
            _flow.Begin();

            Assert.IsFalse(_flow.CompleteTask("battery.1"));
            Assert.IsFalse(_flow.CompleteTask("switch.9"));
            Assert.IsFalse(_flow.CompleteTask("switch.x"));
            Assert.IsFalse(_flow.CompleteTask(null));
        }

        [Test]
        public void EarlyPickupsCountAndAreNotAskedForAgain()
        {
            _flow.Begin();

            Assert.IsTrue(_flow.CompleteTask("ch3.keycard"), "Saboteur A's keycard can be picked up in any chapter.");
            CollectionAssert.Contains(_log, "task ch3.keycard");

            FinishChapter1();
            FinishChapter2();
            _locator.Known["ch3.keycard"] = Vector3.one;

            Assert.AreEqual(ChapterPhase.Active, _flow.GetPhase(3));
            CollectionAssert.DoesNotContain(ObjectiveIds(), 108);
            CompleteAll("ch3.relays");
            Assert.AreEqual(ChapterPhase.TasksDone, _flow.GetPhase(3), "Only the relays were left.");
        }

        [Test]
        public void LaterChapterTasksAreInert()
        {
            _flow.Begin();

            Assert.IsFalse(_flow.CompleteTask("ch2.terminal"));
            Assert.IsFalse(_flow.CompleteTask("ch4.core.1"));
            Assert.IsFalse(_flow.CompleteTask(ChapterEvents.ConsoleTaskId));

            Assert.IsFalse(_flow.IsTaskComplete("ch2.terminal"));
            FinishChapter1();
            Assert.IsTrue(_flow.CompleteTask("ch2.terminal"), "It counts once its chapter is active.");
        }

        // ---- Switch, cutscene, next chapter ---------------------------------------------

        [Test]
        public void RestoringTheSwitchMovesToTransition()
        {
            _flow.Begin();
            CompleteAll(Chapter1Tasks);

            Assert.IsTrue(_flow.CompleteTask("switch.1"));

            Assert.AreEqual("restored 1", _log[_log.Count - 1]);
            Assert.AreEqual(ChapterPhase.Transition, _flow.GetPhase(1));
            Assert.AreEqual(ChapterPhase.Locked, _flow.GetPhase(2));
            Assert.IsFalse(_flow.CompleteTask("switch.1"), "A switch is restored once.");
        }

        [Test]
        public void TheNextChapterStartsOnlyWhenItsCutsceneEnds()
        {
            _flow.Begin();
            Assert.IsFalse(_flow.CutsceneEnded("ch2"), "Ending before the switch is restored does nothing.");
            CompleteAll(Chapter1Tasks);
            CompleteAll("switch.1");

            Assert.IsFalse(_flow.CutsceneEnded("ch3"));
            Assert.IsFalse(_flow.CutsceneEnded("intro"));
            Assert.AreEqual(ChapterPhase.Locked, _flow.GetPhase(2));

            Assert.IsTrue(_flow.CutsceneEnded("ch2"));

            Assert.AreEqual(ChapterPhase.Done, _flow.GetPhase(1));
            Assert.AreEqual(ChapterPhase.Active, _flow.GetPhase(2));
            Assert.AreEqual(2, _flow.CurrentChapter);
            Assert.AreEqual("start 2", _log[_log.Count - 1]);
        }

        [Test]
        public void ChapterFourFinishesTheJourneyWithoutASwitch()
        {
            _flow.Begin();
            FinishChapter1();
            FinishChapter2();
            _locator.Known["ch3.keycard"] = Vector3.one;
            CompleteAll("ch3.keycard", "ch3.relays", "switch.3");
            Assert.IsTrue(_flow.CutsceneEnded("ch4"));
            Assert.AreEqual(ChapterPhase.Active, _flow.GetPhase(4));

            CompleteAll(Chapter4Tasks);

            Assert.AreEqual(ChapterPhase.Done, _flow.GetPhase(4));
            Assert.IsTrue(_flow.IsFinished);
            CollectionAssert.Contains(_log, "task " + ChapterEvents.ConsoleTaskId);
            CollectionAssert.DoesNotContain(_log, "unseal 0");
            CollectionAssert.IsEmpty(ObjectiveIds());
        }

        [Test]
        public void AListenerReportingBackFromInsideAnEventIsHandled()
        {
            _flow.SwitchUnsealed += n => _flow.CompleteTask("switch." + n);   // the switch flips at once
            _flow.Begin();

            CompleteAll(Chapter1Tasks);

            Assert.AreEqual(ChapterPhase.Transition, _flow.GetPhase(1));
            CollectionAssert.Contains(_log, "restored 1");
        }

        // ---- Objectives -----------------------------------------------------------------

        [Test]
        public void ObjectivesAreTheActiveTasksEverySwitchAndTheConsole()
        {
            _flow.Begin();

            CollectionAssert.AreEquivalent(new[] { 101, 102, 103, 104, 105, 201, 202, 203, 300 }, ObjectiveIds());
            Assert.AreEqual(ObjectiveKind.Task, Objective(101).Kind);
            Assert.AreEqual(ObjectiveKind.Switch, Objective(202).Kind);
            Assert.AreEqual(ObjectiveKind.Console, Objective(300).Kind);
            Assert.AreEqual(_locator.Known["ch1.lever"], Objective(101).Position);
            Assert.AreEqual(_locator.Known["ch4.console"], Objective(300).Position);
        }

        [Test]
        public void ACompletedTaskLeavesTheList()
        {
            _flow.Begin();

            CompleteAll("ch1.lever");

            CollectionAssert.DoesNotContain(ObjectiveIds(), 101);
            CollectionAssert.Contains(ObjectiveIds(), 102);
        }

        [Test]
        public void ARestoredSwitchLeavesTheList()
        {
            _flow.Begin();
            CompleteAll(Chapter1Tasks);
            CollectionAssert.Contains(ObjectiveIds(), 201);

            CompleteAll("switch.1");

            CollectionAssert.AreEquivalent(new[] { 202, 203, 300 }, ObjectiveIds(), "No tasks during the cutscene.");
        }

        [Test]
        public void ATaskWithNoAnchorIsShownAtItsProp()
        {
            _flow.Begin();
            FinishChapter1();

            Assert.AreEqual(new Vector3(50f, 0f, 0f), Objective(106).Position);
        }

        [Test]
        public void TheKeycardIsListedOnlyOnceItHasBeenDropped()
        {
            _flow.Begin();
            FinishChapter1();
            FinishChapter2();
            CollectionAssert.DoesNotContain(ObjectiveIds(), 108);

            _locator.Known["ch3.keycard"] = new Vector3(3f, 0f, 9f);

            Assert.AreEqual(new Vector3(3f, 0f, 9f), Objective(108).Position);
        }

        [Test]
        public void TheRelayObjectiveFollowsTheChosenOrder()
        {
            IReadOnlyList<int> chosen = null;
            _flow.SequenceChosen += (id, order) => chosen = order;
            _flow.Begin();
            Assert.IsTrue(_flow.TryGetSequenceOrder("ch3.relays", out IReadOnlyList<int> order));
            Assert.AreSame(order, chosen);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, order);
            FinishChapter1();
            FinishChapter2();

            Assert.AreEqual(_locator.Known["ch3.relay." + order[0]], Objective(109).Position);
            Assert.IsTrue(_flow.ReportProgress("ch3.relays", 1f / 3f));
            Assert.AreEqual(_locator.Known["ch3.relay." + order[1]], Objective(109).Position);
            Assert.IsTrue(_flow.ReportProgress("ch3.relays", 0f));   // wrong relay: start again
            Assert.AreEqual(_locator.Known["ch3.relay." + order[0]], Objective(109).Position);
            Assert.IsFalse(_flow.ReportProgress("ch3.relays", 0f), "No change, no update.");
        }

        [Test]
        public void TheSameSeedGivesTheSameRelayOrder()
        {
            var again = new ChapterFlow(_chapters, seed: 7);
            _flow.Begin();
            again.Begin();
            _flow.TryGetSequenceOrder("ch3.relays", out IReadOnlyList<int> first);
            again.TryGetSequenceOrder("ch3.relays", out IReadOnlyList<int> second);

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void ObjectivesChangeOnEveryAcceptedInputAndOnlyThen()
        {
            _flow.Begin();
            int afterBegin = _objectiveChanges;
            Assert.AreEqual(1, afterBegin);

            _flow.CompleteTask("ch2.terminal");    // inert
            _flow.CompleteTask("switch.1");        // sealed
            Assert.AreEqual(afterBegin, _objectiveChanges);

            _flow.CompleteTask("ch1.lever");
            Assert.AreEqual(afterBegin + 1, _objectiveChanges);
        }

        // ---- Data validation ------------------------------------------------------------

        [Test]
        public void InvalidChapterDataIsRejected()
        {
            TaskDefinition a = TaskDefinition.Create("dup", 1, "a", TaskType.Interact, "");
            TaskDefinition b = TaskDefinition.Create("dup", 2, "b", TaskType.Interact, "");
            TaskDefinition reserved = TaskDefinition.Create("switch.1", 3, "c", TaskType.Interact, "");
            ChapterDefinition duplicateTasks = ChapterDefinition.Create(1, "", "", "", new[] { a, b }, 1, "");
            ChapterDefinition reservedTask = ChapterDefinition.Create(1, "", "", "", new[] { reserved }, 1, "");
            ChapterDefinition gap = ChapterDefinition.Create(2, "", "", "", new TaskDefinition[0], 0, "");
            _assets.AddRange(new Object[] { a, b, reserved, duplicateTasks, reservedTask, gap });

            Assert.Throws<ArgumentException>(() => new ChapterFlow(new[] { duplicateTasks }, 1));
            Assert.Throws<ArgumentException>(() => new ChapterFlow(new[] { reservedTask }, 1));
            Assert.Throws<ArgumentException>(() => new ChapterFlow(new[] { gap }, 1));
            Assert.Throws<ArgumentException>(() => new ChapterFlow(new ChapterDefinition[0], 1));
        }
    }
}
