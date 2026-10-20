using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The music follows the run: the title track on the title, each room's track as its chapter
    /// starts, quieter under a cutscene, and a sting on the Results screen, played once; each
    /// track plays its own clip at its own gain.
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

        // A track change is seen on the next update.
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
            Assert.AreEqual(MusicTrack.Title, music.Playing, "A new run starts on the title track.");
        }

        AudioClip Clip(string name, float seconds)
        {
            AudioClip clip = AudioClip.Create(name, Mathf.RoundToInt(seconds * 44100), 1, 44100, false);
            _created.Add(clip);
            return clip;
        }

        static AudioSource Audible(MusicDirector music) =>
            music.GetComponents<AudioSource>().Where(s => s.isPlaying).OrderByDescending(s => s.volume).FirstOrDefault();

        [UnityTest]
        public IEnumerator EachTrackPlaysItsClipAtItsGainAndTheStingPlaysOnce()
        {
            var clock = new FakeClock { State = GameState.Playing };
            GameClock.Publish(clock);
            var go = new GameObject("Music");
            _created.Add(go);
            MusicDirector music = go.AddComponent<MusicDirector>();
            AudioClip title = Clip("title", 5f), assembly = Clip("assembly", 5f), sting = Clip("sting", 1f);
            music.UseCues(
                new MusicCue(MusicTrack.Title, title, 1f),
                new MusicCue(MusicTrack.AssemblyFloor, assembly, 0.5f),
                new MusicCue(MusicTrack.LoseJingle, sting, 1f));

            ChapterEvents.RaiseChapterStarted(1);
            yield return Until(music, MusicTrack.AssemblyFloor);
            yield return new WaitForSeconds(2f);   // past the crossfade and the volume's ease
            AudioSource playing = Audible(music);
            Assert.AreSame(assembly, playing.clip, "The Assembly Floor's clip.");
            Assert.IsTrue(playing.loop, "A room's track loops.");
            float expected = MusicDirector.PlayVolume * GameAudio.MusicVolume * 0.5f;
            Assert.AreEqual(expected, playing.volume, 0.01f, "At the play volume times its gain.");

            // A lost run: the sting plays once, then the title track.
            clock.State = GameState.Results;
            yield return Until(music, MusicTrack.LoseJingle);
            yield return new WaitForSecondsRealtime(0.4f);   // past the sting's 0.25 s crossfade
            playing = Audible(music);
            Assert.AreSame(sting, playing.clip);
            Assert.IsFalse(playing.loop, "The sting does not loop.");
            yield return Until(music, MusicTrack.Title);
            Assert.AreEqual(MusicTrack.Title, music.Playing, "The title track once the sting has ended.");
            yield return null;
            Assert.AreSame(title, Audible(music).clip);
        }
    }
}
