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
    public sealed class CutsceneCameraTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        T Track<T>(T thing) where T : Object
        {
            _created.Add(thing);
            return thing;
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        // A player body with a pitch pivot and the main camera under it, like the player prefab.
        Camera PlayerCamera(Vector3 at)
        {
            var body = Track(new GameObject("Player"));
            body.transform.position = at;
            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(body.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            pivot.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(pivot.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0.05f, 0.1f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 74f;
            return camera;
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

        TimelineAsset Timeline(double seconds)
        {
            var timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = seconds;
            return timeline;
        }

        static CutsceneBindingId BindingId(GameObject target, string id)
        {
            target.SetActive(false);
            CutsceneBindingId binding = target.AddComponent<CutsceneBindingId>();
            SetField(binding, "id", id);
            target.SetActive(true);
            return binding;
        }

        [UnityTest]
        public IEnumerator CameraCutsToTheShotThenGoesBackToThePlayer()
        {
            Camera camera = PlayerCamera(new Vector3(2f, 0f, 3f));
            Vector3 localPosition = camera.transform.localPosition;
            Quaternion localRotation = camera.transform.localRotation;
            Vector3 playerView = camera.transform.position;

            var shotCamera = Track(new GameObject("Shot Camera"));
            shotCamera.transform.position = new Vector3(20f, 6f, -10f);
            CinemachineCamera vcam = shotCamera.AddComponent<CinemachineCamera>();
            vcam.Lens.FieldOfView = 40f;

            TimelineAsset timeline = Timeline(0.6);
            var cameraTrack = timeline.CreateTrack<CinemachineTrack>(null, "Camera");
            TimelineClip clip = cameraTrack.CreateClip<CinemachineShot>();
            clip.start = 0;
            clip.duration = 0.6;
            var shot = (CinemachineShot)clip.asset;
            shot.VirtualCamera.exposedName = "shot-1";

            CutsceneDirector director = Director(new CutsceneDefinition("ch2", CutsceneTrigger.SwitchRestored, 1, timeline: timeline));
            director.GetComponent<PlayableDirector>().SetReferenceValue("shot-1", vcam);

            ChapterEvents.RaiseSwitchRestored(1);
            yield return Seconds(0.25f);

            Assert.IsTrue(director.CameraRig.IsActive);
            Assert.IsTrue(camera.GetComponent<CinemachineBrain>().enabled, "The gameplay camera has a brain during the cutscene.");
            Assert.Less(Vector3.Distance(camera.transform.position, shotCamera.transform.position), 0.01f, "The shot camera is live.");
            Assert.AreEqual(40f, camera.fieldOfView, 0.01f);

            yield return Seconds(0.8f);

            Assert.IsFalse(director.IsPlaying);
            Assert.IsFalse(director.CameraRig.IsActive);
            Assert.IsFalse(camera.GetComponent<CinemachineBrain>().enabled, "No brain runs during gameplay.");
            Assert.AreEqual(localPosition, camera.transform.localPosition);
            Assert.AreEqual(localRotation, camera.transform.localRotation);
            Assert.Less(Vector3.Distance(playerView, camera.transform.position), 0.0001f, "Back at the player's eyes.");
            Assert.AreEqual(74f, camera.fieldOfView, 0.01f, "The player's field of view is restored.");
        }

        [UnityTest]
        public IEnumerator SkipGivesTheCameraBackStraightAway()
        {
            Camera camera = PlayerCamera(Vector3.zero);
            Vector3 localPosition = camera.transform.localPosition;
            var shotCamera = Track(new GameObject("Shot Camera"));
            shotCamera.transform.position = new Vector3(5f, 5f, 5f);
            CinemachineCamera vcam = shotCamera.AddComponent<CinemachineCamera>();

            TimelineAsset timeline = Timeline(5.0);
            var cameraTrack = timeline.CreateTrack<CinemachineTrack>(null, "Camera");
            TimelineClip clip = cameraTrack.CreateClip<CinemachineShot>();
            clip.start = 0;
            clip.duration = 5.0;
            ((CinemachineShot)clip.asset).VirtualCamera.exposedName = "shot-1";

            CutsceneDirector director = Director(new CutsceneDefinition("ch2", CutsceneTrigger.SwitchRestored, 1, timeline: timeline));
            director.GetComponent<PlayableDirector>().SetReferenceValue("shot-1", vcam);

            ChapterEvents.RaiseSwitchRestored(1);
            yield return Seconds(0.2f);
            director.Skip();
            yield return null;

            Assert.IsFalse(director.CameraRig.IsActive);
            Assert.AreEqual(localPosition, camera.transform.localPosition);
        }

        [UnityTest]
        public IEnumerator TracksNamedAfterABindingIdDriveThatObject()
        {
            var lamp = Track(new GameObject("Storage Lamp"));
            BindingId(lamp, "TestLamp");

            TimelineAsset timeline = Timeline(0.4);
            var activation = timeline.CreateTrack<ActivationTrack>(null, "TestLamp");
            TimelineClip on = activation.CreateDefaultClip();
            on.start = 0.3;
            on.duration = 0.1;
            timeline.CreateTrack<ActivationTrack>(null, "NoSuchObject");

            CutsceneDirector director = Director(new CutsceneDefinition("ch2", CutsceneTrigger.SwitchRestored, 1, timeline: timeline));
            ChapterEvents.RaiseSwitchRestored(1);
            yield return Seconds(0.1f);

            PlayableDirector playable = director.GetComponent<PlayableDirector>();
            Assert.AreEqual(lamp, playable.GetGenericBinding(activation), "Bound by its binding id, across scenes.");
            Assert.IsFalse(lamp.activeSelf, "Outside its clip, the activation track switches the lamp off: the track drives it.");
            CollectionAssert.AreEqual(new[] { "NoSuchObject" }, director.MissingBindings);
            director.Skip();
        }

        [Test]
        public void BindingForPicksTheComponentTheTrackDrives()
        {
            var target = Track(new GameObject("Door"));
            var child = new GameObject("Model");
            child.transform.SetParent(target.transform, false);
            Animator animator = child.AddComponent<Animator>();

            Assert.AreEqual(target, CutsceneBindings.BindingFor(target, typeof(GameObject)));
            Assert.AreEqual(animator, CutsceneBindings.BindingFor(target, typeof(Animator)), "An animator on a child is found.");
            Assert.IsNull(CutsceneBindings.BindingFor(target, typeof(AudioSource)));
            Assert.IsNull(CutsceneBindings.BindingFor(target, typeof(string)));
        }

        [UnityTest]
        public IEnumerator LetterboxClosesInForACutsceneAndOpensAfter()
        {
            var host = Track(new GameObject("Letterbox"));
            Letterbox letterbox = host.AddComponent<Letterbox>();
            yield return null;
            Assert.IsFalse(letterbox.IsShowing, "Nothing on screen during gameplay.");

            CutsceneEvents.RaiseCutsceneStarted("ch2");
            yield return Seconds(0.8f);
            Assert.AreEqual(1f, letterbox.Amount, 1e-4f);
            Assert.AreEqual(0.11f, letterbox.Coverage, 1e-4f);
            Assert.IsTrue(letterbox.IsShowing);

            CutsceneEvents.RaiseCutsceneEnded("ch2");
            yield return Seconds(0.8f);
            Assert.AreEqual(0f, letterbox.Amount, 1e-4f);
            Assert.IsFalse(letterbox.IsShowing, "The canvas is switched off once the bars are open.");
        }

        [Test]
        public void LetterboxEasesInAndOut()
        {
            Assert.AreEqual(0f, Letterbox.Ease(0f));
            Assert.AreEqual(0.5f, Letterbox.Ease(0.5f), 1e-5f);
            Assert.AreEqual(1f, Letterbox.Ease(1f));
            Assert.Less(Letterbox.Ease(0.1f), 0.1f, "Slow at the start.");
        }
    }
}
