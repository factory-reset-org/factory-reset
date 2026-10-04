using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>A stand-in task prop: completes when the test says so.</summary>
    public sealed class FakeTaskProp : MonoBehaviour, ITask
    {
        public string TaskId;
        public string Id => TaskId;
        public event Action<float> OnProgress;
        public event Action OnCompleted;
        public void Complete()
        {
            OnProgress?.Invoke(1f);
            OnCompleted?.Invoke();
        }
    }

    /// <summary>A stand-in task anchor (Runtime's TaskAnchor keeps its id private).</summary>
    public sealed class FakeTaskAnchor : MonoBehaviour, ITaskAnchor
    {
        public string Id;
        public string AnchorId => Id;
        public int Chapter => 0;
        public Vector3 Position => transform.position;
    }

    public sealed class ChapterManagerTests
    {
        readonly List<Object> _created = new List<Object>();
        readonly List<int> _started = new List<int>();
        readonly List<string> _completed = new List<string>();
        readonly List<int> _unsealed = new List<int>();
        readonly List<int> _restored = new List<int>();
        readonly List<ObjectiveTargetInfo> _targets = new List<ObjectiveTargetInfo>();
        Action<int> _onStarted, _onUnsealed, _onRestored;
        Action<string> _onCompleted;
        Action<IReadOnlyList<ObjectiveTargetInfo>> _onTargets;

        [SetUp]
        public void SetUp()
        {
            _started.Clear();
            _completed.Clear();
            _unsealed.Clear();
            _restored.Clear();
            _targets.Clear();
            ChapterEvents.OnChapterStarted += _onStarted = _started.Add;
            ChapterEvents.OnTaskCompleted += _onCompleted = _completed.Add;
            ChapterEvents.OnSwitchUnsealed += _onUnsealed = _unsealed.Add;
            ChapterEvents.OnSwitchRestored += _onRestored = _restored.Add;
            ObjectiveEvents.OnTargetsChanged += _onTargets = targets =>
            {
                _targets.Clear();
                _targets.AddRange(targets);
            };
        }

        [TearDown]
        public void TearDown()
        {
            ChapterEvents.OnChapterStarted -= _onStarted;
            ChapterEvents.OnTaskCompleted -= _onCompleted;
            ChapterEvents.OnSwitchUnsealed -= _onUnsealed;
            ChapterEvents.OnSwitchRestored -= _onRestored;
            ObjectiveEvents.OnTargetsChanged -= _onTargets;
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        // Chapter 1: task "a" at anchor "a", plus pickup "p" that is spawned during play, then
        // switch 1 and cutscene "c". Chapter 2: the console at anchor "con".
        ChapterManager CreateManager()
        {
            TaskDefinition a = Track(TaskDefinition.Create("a", 1, "A", TaskType.Interact, "a"));
            TaskDefinition p = Track(TaskDefinition.Create("p", 2, "P", TaskType.Collect, "", isPickup: true, spawnedAtRuntime: true));
            TaskDefinition console = Track(TaskDefinition.Create(ChapterEvents.ConsoleTaskId, 3, "Console", TaskType.HoldAt, "con"));
            ChapterDefinition one = Track(ChapterDefinition.Create(1, "One", "", "", new[] { a, p }, 1, "c"));
            ChapterDefinition two = Track(ChapterDefinition.Create(2, "Two", "", "", new[] { console }, 0, ""));

            var go = new GameObject("Chapters");
            go.SetActive(false);   // so Awake runs after the data is set
            _created.Add(go);
            var manager = go.AddComponent<ChapterManager>();
            SetField(manager, "chapters", new[] { one, two });
            SetField(manager, "sequenceSeed", 1);
            go.SetActive(true);
            return manager;
        }

        T Track<T>(T asset) where T : Object
        {
            _created.Add(asset);
            return asset;
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        void Anchor(string id, Vector3 position)
        {
            var go = new GameObject("Anchor " + id);
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<FakeTaskAnchor>().Id = id;
        }

        FakeTaskProp Prop(string id, Vector3 position)
        {
            var go = new GameObject("Prop " + id);
            _created.Add(go);
            go.transform.position = position;
            FakeTaskProp prop = go.AddComponent<FakeTaskProp>();
            prop.TaskId = id;
            return prop;
        }

        void BuildLevel()
        {
            Anchor("a", new Vector3(1f, 0f, 0f));
            Anchor("switch.1", new Vector3(2f, 0f, 0f));
            Anchor("con", new Vector3(3f, 0f, 0f));
        }

        ObjectiveTargetInfo Target(int id)
        {
            foreach (ObjectiveTargetInfo target in _targets)
                if (target.Id == id)
                    return target;
            Assert.Fail($"Objective {id} was not published.");
            return default;
        }

        List<int> TargetIds()
        {
            var ids = new List<int>();
            foreach (ObjectiveTargetInfo target in _targets)
                ids.Add(target.Id);
            return ids;
        }

        [Test]
        public void NothingStartsUntilBeginIsCalled()
        {
            BuildLevel();
            Prop("a", Vector3.zero);
            ChapterManager manager = CreateManager();

            Assert.AreSame(manager, ChapterManager.Current);
            CollectionAssert.IsEmpty(_started);
            Assert.IsFalse(manager.Flow.HasBegun);
        }

        [Test]
        public void BeginStartsChapterOneAndPublishesItsObjectivesAtTheAnchors()
        {
            BuildLevel();
            Prop("a", Vector3.zero);
            Prop("switch.1", Vector3.zero);
            Prop(ChapterEvents.ConsoleTaskId, Vector3.zero);
            ChapterManager manager = CreateManager();

            manager.Begin();

            CollectionAssert.AreEqual(new[] { 1 }, _started);
            CollectionAssert.AreEquivalent(new[] { 101, 201, 300 }, TargetIds(), "The keycard-style pickup is not dropped yet.");
            Assert.AreEqual(new Vector3(1f, 0f, 0f), Target(101).Position);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), Target(201).Position);
            Assert.AreEqual(new Vector3(3f, 0f, 0f), Target(300).Position);
        }

        [UnityTest]
        public IEnumerator PropEventsRunTheJourneyThroughToTheNextChapter()
        {
            BuildLevel();
            FakeTaskProp a = Prop("a", Vector3.zero);
            FakeTaskProp lever = Prop("switch.1", Vector3.zero);
            FakeTaskProp console = Prop(ChapterEvents.ConsoleTaskId, Vector3.zero);
            ChapterManager manager = CreateManager();
            manager.Begin();

            a.Complete();
            CollectionAssert.AreEqual(new[] { "a" }, _completed);
            CollectionAssert.DoesNotContain(TargetIds(), 101);

            // A pickup created during play announces itself and is shown where it lies.
            FakeTaskProp pickup = Prop("p", new Vector3(5f, 0f, 0f));
            TaskEvents.RaiseTaskSpawned(pickup);
            Assert.AreEqual(new Vector3(5f, 0f, 0f), Target(102).Position);

            // Moving it re-publishes the same id at the new place.
            pickup.transform.position = new Vector3(7f, 0f, 0f);
            yield return null;
            Assert.AreEqual(new Vector3(7f, 0f, 0f), Target(102).Position);

            pickup.Complete();
            CollectionAssert.AreEqual(new[] { 1 }, _unsealed);

            lever.Complete();
            CollectionAssert.AreEqual(new[] { 1 }, _restored);
            CollectionAssert.AreEqual(new[] { 1 }, _started, "Waits for the cutscene.");

            CutsceneEvents.RaiseCutsceneEnded("c");
            CollectionAssert.AreEqual(new[] { 1, 2 }, _started);

            console.Complete();
            Assert.Contains(ChapterEvents.ConsoleTaskId, _completed);
            Assert.IsTrue(manager.Flow.IsFinished);
            CollectionAssert.IsEmpty(TargetIds());
        }

        [Test]
        public void AMissingPropIsReportedWhenTheJourneyBegins()
        {
            BuildLevel();
            Prop("switch.1", Vector3.zero);
            Prop(ChapterEvents.ConsoleTaskId, Vector3.zero);
            ChapterManager manager = CreateManager();

            LogAssert.Expect(LogType.Error, new Regex("No task prop with id \"a\""));
            manager.Begin();
        }

        [Test]
        public void BeginTwiceIsIgnored()
        {
            BuildLevel();
            Prop("a", Vector3.zero);
            Prop("switch.1", Vector3.zero);
            Prop(ChapterEvents.ConsoleTaskId, Vector3.zero);
            ChapterManager manager = CreateManager();
            manager.Begin();

            LogAssert.Expect(LogType.Warning, new Regex("called twice"));
            manager.Begin();

            CollectionAssert.AreEqual(new[] { 1 }, _started);
        }
    }
}
