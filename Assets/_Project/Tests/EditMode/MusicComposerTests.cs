using NUnit.Framework;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The music made in code: each loop is exactly eight bars and joins without a click,
    /// nothing clips or is silent, every piece is as loud as the others and sits under the
    /// sound effects, the jingles end in silence, the same track is written the same way every
    /// time, and each room and each part of a run has its own track.
    /// </summary>
    public class MusicComposerTests
    {
        static readonly MusicTrack[] Loops =
        {
            MusicTrack.Title, MusicTrack.AssemblyFloor, MusicTrack.PaintingRoom, MusicTrack.StorageArea, MusicTrack.ControlRoom,
        };
        static readonly MusicTrack[] Jingles = { MusicTrack.WinJingle, MusicTrack.LoseJingle };

        static float Rms(float[] data)
        {
            double sum = 0.0;
            foreach (float s in data)
                sum += s * s;
            return (float)System.Math.Sqrt(sum / data.Length);
        }

        [Test]
        public void EachLoopIsExactlyEightBarsOfItsTempo()
        {
            foreach (MusicTrack track in Loops)
            {
                float[] data = MusicComposer.Compose(track);
                float seconds = 8 * 4 * 60f / MusicComposer.Tempo(track);
                Assert.AreEqual(Mathf.RoundToInt(seconds * MusicComposer.SampleRate), data.Length, track.ToString());
                Assert.IsTrue(MusicComposer.Loops(track));
            }
        }

        [Test]
        public void NothingClipsAndEveryLoopIsAsLoudAsTheOthers()
        {
            foreach (MusicTrack track in Loops)
            {
                float[] data = MusicComposer.Compose(track);
                float peak = 0f;
                foreach (float s in data)
                    peak = Mathf.Max(peak, Mathf.Abs(s));
                Assert.LessOrEqual(peak, 0.8001f, $"{track} peaks under 0.8.");
                Assert.AreEqual(MusicComposer.TargetRms, Rms(data), 0.03f, $"{track} is at the shared loudness.");
            }
            foreach (MusicTrack track in Jingles)
                Assert.Greater(Rms(MusicComposer.Compose(track)), 0.03f, $"{track} is audible.");
        }

        [Test]
        public void TheMusicSitsUnderTheSoundEffects()
        {
            // The effects peak at about 0.03 to 0.07 after GameSfx's master volume; the music
            // during play stays at about a quarter of that.
            foreach (MusicTrack track in Loops)
                Assert.LessOrEqual(MusicDirector.PlayVolume * Rms(MusicComposer.Compose(track)), 0.015f, track.ToString());
        }

        [Test]
        public void TheLoopsJoinWithoutAClick()
        {
            // A note that runs past the end wraps round to the start, so the jump across the
            // seam is no bigger than the jumps inside the track.
            foreach (MusicTrack track in Loops)
            {
                float[] data = MusicComposer.Compose(track);
                float largestInside = 0f;
                for (int i = 1; i < data.Length; i++)
                    largestInside = Mathf.Max(largestInside, Mathf.Abs(data[i] - data[i - 1]));
                float seam = Mathf.Abs(data[0] - data[data.Length - 1]);
                Assert.LessOrEqual(seam, largestInside, track.ToString());
            }
        }

        [Test]
        public void TheJinglesDoNotLoopAndEndInSilence()
        {
            foreach (MusicTrack track in Jingles)
            {
                Assert.IsFalse(MusicComposer.Loops(track));
                float[] data = MusicComposer.Compose(track);
                int tail = MusicComposer.SampleRate / 20;
                float sum = 0f;
                for (int i = data.Length - tail; i < data.Length; i++)
                    sum += data[i] * data[i];
                Assert.Less(Mathf.Sqrt(sum / tail), 0.01f, $"{track} has died away by its end.");
            }
        }

        [Test]
        public void TheSameTrackIsWrittenTheSameWayEveryTime()
        {
            CollectionAssert.AreEqual(MusicComposer.Compose(MusicTrack.ControlRoom), MusicComposer.Compose(MusicTrack.ControlRoom));
            Assert.AreEqual(0, MusicComposer.Compose(MusicTrack.None).Length);
        }

        [Test]
        public void EachRoomHasItsOwnTrack()
        {
            Assert.AreEqual(MusicTrack.AssemblyFloor, MusicDirector.RoomTrack(0), "Before the first chapter starts.");
            Assert.AreEqual(MusicTrack.AssemblyFloor, MusicDirector.RoomTrack(1));
            Assert.AreEqual(MusicTrack.PaintingRoom, MusicDirector.RoomTrack(2));
            Assert.AreEqual(MusicTrack.StorageArea, MusicDirector.RoomTrack(3));
            Assert.AreEqual(MusicTrack.ControlRoom, MusicDirector.RoomTrack(4));
        }

        [Test]
        public void TheTrackFitsThePartOfTheRun()
        {
            Assert.AreEqual(MusicTrack.Title, MusicDirector.Choose(GameState.Title, 0, false, false));
            Assert.AreEqual(MusicTrack.AssemblyFloor, MusicDirector.Choose(GameState.Playing, 1, false, false));
            Assert.AreEqual(MusicTrack.PaintingRoom, MusicDirector.Choose(GameState.Cutscene, 2, false, false), "A cutscene keeps the room's music.");
            Assert.AreEqual(MusicTrack.ControlRoom, MusicDirector.Choose(GameState.Paused, 4, false, false), "So does the pause menu.");
            Assert.AreEqual(MusicTrack.WinJingle, MusicDirector.Choose(GameState.Results, 4, true, false));
            Assert.AreEqual(MusicTrack.LoseJingle, MusicDirector.Choose(GameState.Results, 2, false, false));
            Assert.AreEqual(MusicTrack.Title, MusicDirector.Choose(GameState.Results, 4, true, true), "Then the theme again.");
        }

        [Test]
        public void TheMusicDipsUnderCutscenesAndWhilePaused()
        {
            float playing = MusicDirector.VolumeFor(GameState.Playing, 0.08f, 0.5f, 0.5f, 2f);
            Assert.AreEqual(0.08f, playing, 1e-5f);
            Assert.Less(MusicDirector.VolumeFor(GameState.Cutscene, 0.08f, 0.5f, 0.5f, 2f), playing);
            Assert.Less(MusicDirector.VolumeFor(GameState.Paused, 0.08f, 0.5f, 0.5f, 2f), playing);
            Assert.Greater(MusicDirector.VolumeFor(GameState.Title, 0.08f, 0.5f, 0.5f, 2f), playing, "Louder where nothing else plays.");
        }
    }
}
