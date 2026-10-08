using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    public sealed class CutsceneDialogueTests
    {
        readonly List<Object> _created = new List<Object>();
        readonly List<string> _log = new List<string>();
        Action<string> _onStarted, _onEnded;
        Action<DialogueLineView> _onShown;
        Action _onCleared;

        sealed class FakeAgent : IAgentState
        {
            public AgentType Type { get; set; }
            public AgentIdentity Identity { get; set; }
            public Vector3 Position => Vector3.zero;
            public float Speed => 0f;
            public float TurnRate => 0f;
            public bool IsAttacking => false;
            public bool IsDead => true;
        }

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            CutsceneEvents.OnCutsceneStarted += _onStarted = id => _log.Add("started:" + id);
            CutsceneEvents.OnCutsceneEnded += _onEnded = id => _log.Add("ended:" + id);
            DialogueEvents.OnLineShown += _onShown = line => { if (line.VisibleCharacters == 0) _log.Add("line:" + line.Text); };
            DialogueEvents.OnLineCleared += _onCleared = () => _log.Add("cleared");
        }

        [TearDown]
        public void TearDown()
        {
            CutsceneEvents.OnCutsceneStarted -= _onStarted;
            CutsceneEvents.OnCutsceneEnded -= _onEnded;
            DialogueEvents.OnLineShown -= _onShown;
            DialogueEvents.OnLineCleared -= _onCleared;
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        T Track<T>(T thing) where T : Object
        {
            _created.Add(thing);
            return thing;
        }

        CutsceneDirector Director(params CutsceneDefinition[] cutscenes)
        {
            var go = Track(new GameObject("Cutscene Director"));
            go.SetActive(false);
            CutsceneDirector director = go.AddComponent<CutsceneDirector>();
            SetField(director, "cutscenes", cutscenes);
            SetField(director, "startDelay", 0f);
            SetField(director, "playIntroOnStart", false);
            go.SetActive(true);
            return director;
        }

        DialogueScript Script(params DialogueLine[][] shots) => Track(DialogueScript.Create(shots));

        static DialogueLine Pip(string text, DialogueCondition when = DialogueCondition.Always) =>
            new DialogueLine(DialogueSpeaker.Pip, text, when);

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        [UnityTest]
        public IEnumerator CutsceneWithoutATimelineSaysItsLinesThenEnds()
        {
            DialogueScript script = Script(new[] { Pip("Hi.") }, new[] { Pip("Bye.") });
            CutsceneDirector director = Director(new CutsceneDefinition("ch2", CutsceneTrigger.SwitchRestored, 1, dialogue: script));

            ChapterEvents.RaiseSwitchRestored(1);
            yield return Seconds(0.3f);
            Assert.IsTrue(director.IsPlaying);
            CollectionAssert.AreEqual(new[] { "started:ch2", "line:Hi." }, _log, "Shots are said in order.");

            // Each short line types in under 0.1 s, then holds for 1.3 s + 0.025 s per character.
            yield return Seconds(DialogueRunner.HoldSeconds(3) + DialogueRunner.HoldSeconds(4) + 0.5f);

            CollectionAssert.AreEqual(new[] { "started:ch2", "line:Hi.", "line:Bye.", "cleared", "ended:ch2" }, _log,
                "The cutscene ends with its last line, not on a fixed timer.");
        }

        [UnityTest]
        public IEnumerator SkipClearsTheSubtitleAndEndsTheCutscene()
        {
            DialogueScript script = Script(new[] { Pip("A line long enough to still be on screen."), Pip("Never said.") });
            CutsceneDirector director = Director(new CutsceneDefinition("ch2", CutsceneTrigger.SwitchRestored, 1, dialogue: script));

            ChapterEvents.RaiseSwitchRestored(1);
            yield return Seconds(0.2f);
            director.Skip();

            Assert.IsFalse(director.Dialogue.IsBusy);
            CollectionAssert.AreEqual(new[] { "started:ch2", "line:A line long enough to still be on screen.", "cleared", "ended:ch2" }, _log);
        }

        [UnityTest]
        public IEnumerator KeycardLinePicksItsVersionByWhetherSaboteurAIsScrapped()
        {
            DialogueScript script = Script(new[]
            {
                Pip("Scrap it.", DialogueCondition.IfSaboteurAActive),
                Pip("Go grab it.", DialogueCondition.IfSaboteurAScrapped),
            });
            CutsceneDirector director = Director(new CutsceneDefinition("ch3", CutsceneTrigger.SwitchRestored, 2, dialogue: script));

            AgentEvents.RaiseDestroyed(new FakeAgent { Type = AgentType.Saboteur, Identity = new AgentIdentity(AgentType.Saboteur, 2, 0) });
            ChapterEvents.RaiseSwitchRestored(2);
            yield return Seconds(0.2f);

            CollectionAssert.Contains(_log, "line:Go grab it.");
            CollectionAssert.DoesNotContain(_log, "line:Scrap it.");
            director.Skip();
        }

        [UnityTest]
        public IEnumerator TimelineWaitsAtADialogueMarkerUntilItsLinesAreSaid()
        {
            // A 0.2 s Timeline with one waiting dialogue marker at 0.05 s.
            var timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 0.2;
            timeline.CreateMarkerTrack();
            timeline.markerTrack.CreateMarker<DialogueMarker>(0.05).Configure(0, true);

            DialogueScript script = Script(new[] { Pip("Wait for me.") });
            CutsceneDirector director = Director(new CutsceneDefinition("ch2", CutsceneTrigger.SwitchRestored, 1,
                timeline: timeline, dialogue: script));

            ChapterEvents.RaiseSwitchRestored(1);
            yield return Seconds(0.6f);
            Assert.IsTrue(director.IsPlaying, "The 0.2 s Timeline is held while the line is on screen.");
            CollectionAssert.Contains(_log, "line:Wait for me.");

            yield return Seconds(DialogueRunner.HoldSeconds(12) + 0.6f);
            Assert.IsFalse(director.IsPlaying, "Once the line is said, the Timeline carries on to its end.");
            Assert.AreEqual("ended:ch2", _log[_log.Count - 1]);
        }
    }
}
