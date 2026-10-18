using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The music follows the run: the theme on the title, each room's piece as its chapter
    /// starts, quieter under a cutscene, and the fanfare on the Results screen of a won run.
    /// </summary>
    public sealed class MusicDirectorTests
    {
        sealed class FakeClock : IGameClock
        {
            public GameState State { get; set; } = GameState.Title;
            public float GameTime => Time.time;
            public void AddListener(IGameStateListener listener) { }
            public void RemoveListener(IGameStateListener listener) { }
            public void RequestState(GameState state) => State = state;
        }

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            GameClock.Publish(null);
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        // The tracks are composed on a background thread, so a change can take a moment.
        static IEnumerator Until(MusicDirector music, MusicTrack track)
        {
            float until = Time.realtimeSinceStartup + 10f;
            while (music.Playing != track && Time.realtimeSinceStartup < until)
                yield return null;
        }

        [UnityTest]
        public IEnumerator TheMusicFollowsTheRun()
        {
            var clock = new FakeClock();
            GameClock.Publish(clock);
            var go = new GameObject("Music");
            _created.Add(go);
            MusicDirector music = go.AddComponent<MusicDirector>();

            yield return Until(music, MusicTrack.Title);
            Assert.AreEqual(MusicTrack.Title, music.Playing, "The theme on the title screen.");

            clock.State = GameState.Playing;
            ChapterEvents.RaiseChapterStarted(1);
            yield return Until(music, MusicTrack.AssemblyFloor);
            Assert.AreEqual(MusicTrack.AssemblyFloor, music.Playing, "The Assembly Floor in Chapter 1.");
            float playVolume = music.TargetVolume;

            ChapterEvents.RaiseChapterStarted(2);
            yield return Until(music, MusicTrack.PaintingRoom);
            Assert.AreEqual(MusicTrack.PaintingRoom, music.Playing, "The Painting Room in Chapter 2.");

            clock.State = GameState.Cutscene;
            yield return null;
            Assert.Less(music.TargetVolume, playVolume, "Quieter under the cutscene's dialogue.");
            ChapterEvents.RaiseChapterStarted(3);
            yield return Until(music, MusicTrack.StorageArea);
            Assert.AreEqual(MusicTrack.StorageArea, music.Playing, "The Storage Vault in Chapter 3.");

            clock.State = GameState.Playing;
            ChapterEvents.RaiseChapterStarted(4);
            yield return Until(music, MusicTrack.ControlRoom);
            Assert.AreEqual(MusicTrack.ControlRoom, music.Playing, "The Control Room in Chapter 4.");

            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            clock.State = GameState.Results;
            yield return Until(music, MusicTrack.WinJingle);
            Assert.AreEqual(MusicTrack.WinJingle, music.Playing, "The fanfare for a won run.");

            clock.State = GameState.Title;
            yield return Until(music, MusicTrack.Title);
            Assert.AreEqual(MusicTrack.Title, music.Playing, "A new run starts on the music box.");
        }
    }
}
