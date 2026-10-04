using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    public sealed class CutsceneDirectorTests
    {
        readonly List<Object> _created = new List<Object>();
        readonly List<string> _log = new List<string>();
        readonly List<int> _chaptersStarted = new List<int>();
        Action<string> _onStarted, _onEnded, _onSignal;
        Action<int> _onChapter;

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            _chaptersStarted.Clear();
            CutsceneEvents.OnCutsceneStarted += _onStarted = id => _log.Add("started:" + id);
            CutsceneEvents.OnCutsceneEnded += _onEnded = id => _log.Add("ended:" + id);
            CutsceneEvents.OnCriticalSignal += _onSignal = id => _log.Add("signal:" + id);
            ChapterEvents.OnChapterStarted += _onChapter = _chaptersStarted.Add;
        }

        [TearDown]
        public void TearDown()
        {
            CutsceneEvents.OnCutsceneStarted -= _onStarted;
            CutsceneEvents.OnCutsceneEnded -= _onEnded;
            CutsceneEvents.OnCriticalSignal -= _onSignal;
            ChapterEvents.OnChapterStarted -= _onChapter;
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        T Track<T>(T asset) where T : Object
        {
            _created.Add(asset);
            return asset;
        }

        // No start delay, so tests do not wait 1.3 s.
        CutsceneDirector CreateDirector(params CutsceneDefinition[] cutscenes)
        {
            var go = Track(new GameObject("Cutscene Director"));
            go.SetActive(false);   // so Awake runs after the fields are set
            CutsceneDirector director = go.AddComponent<CutsceneDirector>();
            SetField(director, "cutscenes", cutscenes);
            SetField(director, "startDelay", 0f);
            go.SetActive(true);
            return director;
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        [UnityTest]
        public IEnumerator RestoredSwitchPlaysItsCutsceneAndTheNextChapterStarts()
        {
            CreateDirector(new CutsceneDefinition("ch2", CutsceneTrigger.SwitchRestored, 1, placeholderSeconds: 0f));

            // Chapter 1: task "a" then switch 1, waiting for cutscene "ch2". Chapter 2: the console.
            TaskDefinition a = Track(TaskDefinition.Create("a", 1, "A", TaskType.Interact, "a"));
            TaskDefinition console = Track(TaskDefinition.Create(ChapterEvents.ConsoleTaskId, 2, "Console", TaskType.HoldAt, "con"));
            ChapterDefinition one = Track(ChapterDefinition.Create(1, "One", "", "", new[] { a }, 1, "ch2"));
            ChapterDefinition two = Track(ChapterDefinition.Create(2, "Two", "", "", new[] { console }, 0, ""));
            foreach (string id in new[] { "a", "switch.1", "con" })
                Track(new GameObject("Anchor " + id)).AddComponent<FakeTaskAnchor>().Id = id;
            FakeTaskProp taskA = Prop("a");
            FakeTaskProp lever = Prop("switch.1");
            Prop(ChapterEvents.ConsoleTaskId);

            var go = Track(new GameObject("Chapters"));
            go.SetActive(false);
            ChapterManager manager = go.AddComponent<ChapterManager>();
            SetField(manager, "chapters", new[] { one, two });
            go.SetActive(true);
            manager.Begin();

            taskA.Complete();
            lever.Complete();
            CollectionAssert.AreEqual(new[] { 1 }, _chaptersStarted, "Chapter 2 waits for the cutscene.");

            yield return Frames(3);

            CollectionAssert.AreEqual(new[] { "started:ch2", "ended:ch2" }, _log);
            CollectionAssert.AreEqual(new[] { 1, 2 }, _chaptersStarted, "The director ended the cutscene the chapter was waiting for.");
        }

        FakeTaskProp Prop(string id)
        {
            FakeTaskProp prop = Track(new GameObject("Prop " + id)).AddComponent<FakeTaskProp>();
            prop.TaskId = id;
            return prop;
        }

        [UnityTest]
        public IEnumerator ConsoleCompletionPlaysTheEnding()
        {
            CreateDirector(new CutsceneDefinition("ending", CutsceneTrigger.ConsoleCompleted, placeholderSeconds: 0f));

            ChapterEvents.RaiseTaskCompleted("a");
            yield return Frames(2);
            Assert.IsEmpty(_log, "Other tasks do not play it.");

            ChapterEvents.RaiseTaskCompleted(ChapterEvents.ConsoleTaskId);
            yield return Frames(3);
            CollectionAssert.AreEqual(new[] { "started:ending", "ended:ending" }, _log);
        }

        [UnityTest]
        public IEnumerator SkipEndsTheCutsceneAndFiresItsCriticalSignals()
        {
            CutsceneDirector director = CreateDirector(new CutsceneDefinition("ch3", CutsceneTrigger.SwitchRestored, 2,
                new[] { CutsceneSignals.CaptainWake, CutsceneSignals.ControlRoomUnlock }, placeholderSeconds: 60f));

            ChapterEvents.RaiseSwitchRestored(2);
            yield return Frames(2);
            Assert.IsTrue(director.IsPlaying);
            Assert.AreEqual("ch3", director.CurrentCutsceneId);

            director.Skip();

            Assert.IsFalse(director.IsPlaying);
            CollectionAssert.AreEqual(new[]
            {
                "started:ch3",
                "signal:" + CutsceneSignals.CaptainWake,
                "signal:" + CutsceneSignals.ControlRoomUnlock,
                "ended:ch3",
            }, _log);
        }

        [UnityTest]
        public IEnumerator TimelineMarkerFiresItsSignalAndTheTimelineEndEndsTheCutscene()
        {
            // A 0.3 s Timeline with a CoreShieldsDown marker at 0.1 s.
            var timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 0.3;
            timeline.CreateMarkerTrack();
            CriticalSignalMarker marker = timeline.markerTrack.CreateMarker<CriticalSignalMarker>(0.1);
            SetField(marker, "signalId", CutsceneSignals.CoreShieldsDown);

            CutsceneDirector director = CreateDirector(new CutsceneDefinition("ch4", CutsceneTrigger.SwitchRestored, 3,
                new[] { CutsceneSignals.CoreShieldsDown }, timeline: timeline));

            ChapterEvents.RaiseSwitchRestored(3);
            yield return Frames(2);
            Assert.IsTrue(director.IsPlaying);
            Assert.AreSame(timeline, director.GetComponent<PlayableDirector>().playableAsset);

            float giveUpAt = Time.realtimeSinceStartup + 3f;
            while (director.IsPlaying && Time.realtimeSinceStartup < giveUpAt)
                yield return null;

            Assert.IsFalse(director.IsPlaying, "The Timeline's end ended the cutscene.");
            CollectionAssert.AreEqual(new[]
            {
                "started:ch4",
                "signal:" + CutsceneSignals.CoreShieldsDown,
                "ended:ch4",
            }, _log, "The marker fired the signal once; the end did not fire it again.");
        }
    }
}
