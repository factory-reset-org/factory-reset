using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The camera and the lines move together: a shot end holds the Timeline while a line is
    /// on screen, and clicking through a shot's lines moves the Timeline to the shot's end.
    /// Also the Unit 047 stand-in, which the follow shots film.
    /// </summary>
    public sealed class CutscenePacingTests
    {
        readonly List<Object> _created = new List<Object>();
        readonly List<string> _log = new List<string>();
        Action<string> _onSignal;
        Action<DialogueLineView> _onShown;

        sealed class FakePlayer : IPlayerState
        {
            public Vector3 Position { get; set; }
            public Vector3 Velocity => Vector3.zero;
            public Vector3 Forward { get; set; } = Vector3.forward;
            public float SprintSpeed => 8.6f;
            public bool IsAlive => true;
            public float HealthFraction => 1f;
            public float AmmoFraction => 1f;
            public bool IsReloading => false;
            public float OverchargeTimeLeft => 0f;
            public float LastShotTime => -1f;
            public void TakeDamage(float amount, int sourceAgentId) { }
        }

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            CutsceneEvents.OnCriticalSignal += _onSignal = id => _log.Add("signal:" + id);
            DialogueEvents.OnLineShown += _onShown = line => { if (line.VisibleCharacters == 0) _log.Add("line:" + line.Text); };
        }

        [TearDown]
        public void TearDown()
        {
            CutsceneEvents.OnCriticalSignal -= _onSignal;
            DialogueEvents.OnLineShown -= _onShown;
            PlayerState.Publish(null);
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

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        CutsceneDirector Director(CutsceneDefinition cutscene)
        {
            var go = Track(new GameObject("Cutscene Director"));
            go.SetActive(false);
            CutsceneDirector director = go.AddComponent<CutsceneDirector>();
            SetField(director, "cutscenes", new[] { cutscene });
            SetField(director, "startDelay", 0f);
            SetField(director, "playIntroOnStart", false);
            go.SetActive(true);
            return director;
        }

        TimelineAsset Timeline(double seconds)
        {
            var timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = seconds;
            timeline.CreateMarkerTrack();
            return timeline;
        }

        DialogueScript Script(params DialogueLine[][] shots) => Track(DialogueScript.Create(shots));

        static DialogueLine Pip(string text) => new DialogueLine(DialogueSpeaker.Pip, text);

        [UnityTest]
        public IEnumerator ClickingThroughAShotMovesTheTimelineToItsEndAndFiresWhatItSkipped()
        {
            // Shot 1 runs 0-6 s with a signal at 3 s; shot 2 starts at 6.05 s.
            TimelineAsset timeline = Timeline(10);
            timeline.markerTrack.CreateMarker<DialogueMarker>(0).Configure(0, false);
            timeline.markerTrack.CreateMarker<CriticalSignalMarker>(3).Configure(CutsceneSignals.CaptainWake);
            timeline.markerTrack.CreateMarker<ShotEndMarker>(6);
            timeline.markerTrack.CreateMarker<DialogueMarker>(6.05).Configure(1, false);
            timeline.markerTrack.CreateMarker<ShotEndMarker>(9.9);

            CutsceneDirector director = Director(new CutsceneDefinition("ch3", CutsceneTrigger.SwitchRestored, 2,
                new[] { CutsceneSignals.CaptainWake }, timeline: timeline, dialogue: Script(new[] { Pip("One.") }, new[] { Pip("Two.") })));
            PlayableDirector playable = director.GetComponent<PlayableDirector>();

            ChapterEvents.RaiseSwitchRestored(2);
            yield return Seconds(0.3f);
            CollectionAssert.AreEqual(new[] { "line:One." }, _log);
            Assert.Less(playable.time, 3.0, "Still early in the shot.");

            director.Dialogue.Advance();   // the line is typed, so this ends it
            yield return null;

            Assert.GreaterOrEqual(playable.time, 6.0 - 1e-6, "Moved to the shot's end.");
            Assert.Less(playable.time, 7.0);
            CollectionAssert.Contains(_log, "signal:" + CutsceneSignals.CaptainWake, "The skipped signal still fired.");

            yield return Seconds(0.3f);
            CollectionAssert.Contains(_log, "line:Two.", "The next shot carries on from there.");
            Assert.AreEqual(1, _log.FindAll(e => e.StartsWith("signal:")).Count, "Fired once.");
            director.Skip();
        }

        [UnityTest]
        public IEnumerator AShotEndHoldsTheTimelineWhileItsLineIsOnScreen()
        {
            TimelineAsset timeline = Timeline(1);
            timeline.markerTrack.CreateMarker<DialogueMarker>(0).Configure(0, false);
            timeline.markerTrack.CreateMarker<ShotEndMarker>(0.2);

            CutsceneDirector director = Director(new CutsceneDefinition("ch2", CutsceneTrigger.SwitchRestored, 1,
                timeline: timeline, dialogue: Script(new[] { Pip("A line long enough to stay on screen for a while.") })));
            PlayableDirector playable = director.GetComponent<PlayableDirector>();

            ChapterEvents.RaiseSwitchRestored(1);
            yield return Seconds(0.8f);   // the line takes about 3.8 s
            Assert.IsTrue(director.IsPlaying);
            Assert.AreEqual(0.2, playable.time, 0.05, "Held at the shot end.");
            yield return Seconds(0.3f);
            Assert.AreEqual(0.2, playable.time, 0.05, "Still held while the line is on screen.");

            director.Dialogue.Advance();   // finish typing
            director.Dialogue.Advance();   // end the line
            yield return Seconds(1.5f);
            Assert.IsFalse(director.IsPlaying, "Carried on to the end once the line was said.");
        }

        [UnityTest]
        public IEnumerator TheShotStaysOnScreenWhileTheTimelineHolds()
        {
            var cameraObject = Track(new GameObject("Main Camera") { tag = "MainCamera" });
            Camera camera = cameraObject.AddComponent<Camera>();

            var shotCamera = Track(new GameObject("Shot Camera"));
            shotCamera.transform.position = new Vector3(20f, 4f, -10f);
            CinemachineCamera vcam = shotCamera.AddComponent<CinemachineCamera>();

            TimelineAsset timeline = Timeline(1);
            var cameraTrack = timeline.CreateTrack<CinemachineTrack>(null, "Camera");
            TimelineClip clip = cameraTrack.CreateClip<CinemachineShot>();
            clip.start = 0;
            clip.duration = 1;
            ((CinemachineShot)clip.asset).VirtualCamera.exposedName = "shot";
            timeline.markerTrack.CreateMarker<DialogueMarker>(0).Configure(0, false);
            timeline.markerTrack.CreateMarker<ShotEndMarker>(0.3);

            CutsceneDirector director = Director(new CutsceneDefinition("ch2", CutsceneTrigger.SwitchRestored, 1,
                timeline: timeline, dialogue: Script(new[] { Pip("A line long enough to stay on screen for a while.") })));
            director.GetComponent<PlayableDirector>().SetReferenceValue("shot", vcam);

            ChapterEvents.RaiseSwitchRestored(1);
            yield return Seconds(1.2f);   // held at 0.3 s for most of this

            Assert.AreEqual(0.3, director.GetComponent<PlayableDirector>().time, 0.05, "Held at the shot end.");
            Assert.Less(Vector3.Distance(shotCamera.transform.position, camera.transform.position), 0.01f,
                "The shot camera is still the one on screen, not the gameplay view.");
            director.Skip();
        }

        [UnityTest]
        public IEnumerator Unit047StandsWhereThePlayerIsForTheLengthOfACutscene()
        {
            PlayerState.Publish(new FakePlayer { Position = new Vector3(12f, 0f, 5f), Forward = Vector3.right });

            var root = Track(new GameObject("Unit 047 (cutscene)"));
            root.SetActive(false);
            GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.transform.SetParent(root.transform, false);
            CutsceneActor actor = root.AddComponent<CutsceneActor>();
            SetField(actor, "model", model);
            root.SetActive(true);
            yield return null;

            Assert.IsFalse(actor.IsShowing, "Hidden during play.");
            Assert.IsFalse(model.GetComponent<Collider>().enabled, "Only ever seen, never touched.");

            CutsceneEvents.RaiseCutsceneStarted("ch2");
            Assert.IsTrue(actor.IsShowing);
            Assert.AreEqual(new Vector3(12f, 0f, 5f), root.transform.position);
            Assert.Less(Vector3.Angle(Vector3.right, root.transform.forward), 0.01f, "Facing the way the player faces.");

            CutsceneEvents.RaiseCutsceneEnded("ch2");
            Assert.IsFalse(actor.IsShowing);
        }
    }
}
