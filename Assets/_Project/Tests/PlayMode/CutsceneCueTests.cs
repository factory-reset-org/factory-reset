using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Cue markers on a cutscene Timeline sound the alarm and pop comic words up through the
    /// director's <see cref="CutsceneCuePlayer"/>; a word pops up where it belongs, faces the
    /// camera and fades, and every word is cleared when the cutscene ends.
    /// </summary>
    public sealed class CutsceneCueTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
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

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
        }

        Sprite[] Words()
        {
            var sprites = new Sprite[4];
            for (int i = 0; i < sprites.Length; i++)
                sprites[i] = Track(Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f)));
            return sprites;
        }

        void Camera(Vector3 at)
        {
            var go = Track(new GameObject("Main Camera") { tag = "MainCamera" });
            go.AddComponent<Camera>();
            go.transform.position = at;
        }

        [UnityTest]
        public IEnumerator CueMarkersOnTheTimelineSoundTheAlarmAndPopAWord()
        {
            var timeline = Track(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 0.5;
            timeline.CreateMarkerTrack();
            timeline.markerTrack.CreateMarker<CutsceneCueMarker>(0.1).Configure(CutsceneCuePlan.For("ch4", 0)[0]);       // the alarm
            timeline.markerTrack.CreateMarker<CutsceneCueMarker>(0.2).Configure(CutsceneCuePlan.For("ending", 0)[0]);   // SHUTDOWN

            var go = Track(new GameObject("Cutscene Director"));
            go.SetActive(false);
            CutsceneDirector director = go.AddComponent<CutsceneDirector>();
            SetField(director, "cutscenes", new[] { new CutsceneDefinition("ch4", CutsceneTrigger.SwitchRestored, 3, timeline: timeline) });
            SetField(director, "startDelay", 0f);
            SetField(director, "playIntroOnStart", false);
            CutsceneCuePlayer cues = go.AddComponent<CutsceneCuePlayer>();
            SetField(cues, "words", Words());
            go.SetActive(true);

            ChapterEvents.RaiseSwitchRestored(3);
            yield return null;
            Assert.IsTrue(director.IsPlaying);
            float giveUpAt = Time.realtimeSinceStartup + 3f;
            while (cues.LastWord == null && Time.realtimeSinceStartup < giveUpAt)
                yield return null;
            Assert.AreEqual(1, cues.AlarmsPlayed, "The alarm marker sounded the alarm.");
            Assert.AreEqual(ComicWord.Shutdown, cues.LastWord, "The word marker popped its word.");
            Assert.AreEqual(1, cues.WordsShowing);

            while (director.IsPlaying && Time.realtimeSinceStartup < giveUpAt)
                yield return null;
            yield return null;
            Assert.AreEqual(0, cues.WordsShowing, "Cleared when the cutscene ended.");
        }

        [UnityTest]
        public IEnumerator AWordPopsUpAboveTheStandInFacesTheCameraAndFades()
        {
            Camera(new Vector3(3f, 2f, -6f));
            var go = Track(new GameObject("Cutscene Director"));
            go.SetActive(false);
            var actorGo = new GameObject("Unit 047 (cutscene)");
            actorGo.transform.SetParent(go.transform, false);
            actorGo.transform.SetPositionAndRotation(new Vector3(3f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            var model = new GameObject("Unit047");
            model.transform.SetParent(actorGo.transform, false);
            CutsceneActor actor = actorGo.AddComponent<CutsceneActor>();
            SetField(actor, "model", model);
            CutsceneCuePlayer cues = go.AddComponent<CutsceneCuePlayer>();
            SetField(cues, "words", Words());
            go.SetActive(true);

            cues.Play(CutsceneCueKind.Pop, ComicWord.Defective, true, new Vector3(0f, 2.8f, 0f));
            yield return null;
            Transform word = go.transform.Find("CutsceneWord");
            Assert.IsNotNull(word);
            Assert.AreEqual(3f, word.position.x, 0.01f, "Above the stand-in.");
            Assert.That(word.position.y, Is.InRange(2.8f, 3.2f), "At head height and drifting up.");
            Vector3 fromCamera = word.position - new Vector3(3f, 2f, -6f);
            Assert.Less(Vector3.Angle(word.forward, fromCamera), 1f, "Faces the camera.");

            yield return Seconds(1.9f);
            Assert.AreEqual(0, cues.WordsShowing, "Gone after 1.6 s.");
        }

        [Test]
        public void TheAlarmIsThreeRisingWhoops()
        {
            AudioClip alarm = CutsceneCuePlayer.BuildAlarm();
            try
            {
                Assert.AreEqual(1.2f, alarm.length, 0.01f);
                var data = new float[alarm.samples];
                alarm.GetData(data, 0);
                float peak = 0f;
                foreach (float s in data)
                    peak = Mathf.Max(peak, Mathf.Abs(s));
                Assert.That(peak, Is.InRange(0.3f, 0.51f), "Loud but never clipping.");

                // Each whoop rises: more zero crossings in its last 50 ms than in its first.
                int window = alarm.frequency / 20;
                int whoop = alarm.samples / 3;
                Assert.Greater(Crossings(data, whoop - window, window), Crossings(data, 0, window) + 5);
            }
            finally
            {
                Object.DestroyImmediate(alarm);
            }
        }

        static int Crossings(float[] data, int start, int length)
        {
            int count = 0;
            for (int i = start + 1; i < start + length; i++)
                if ((data[i - 1] < 0f) != (data[i] < 0f))
                    count++;
            return count;
        }
    }
}
