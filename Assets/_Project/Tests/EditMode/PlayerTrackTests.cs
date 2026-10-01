using System;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;

namespace ToyFactory.Tests.EditMode
{
    public class PlayerTrackTests
    {
        static Vector2Int Cell(int x) => new Vector2Int(x, 0);

        // Records one sample every 0.5 s (the 2 Hz prediction rate), cell x = sample number.
        static PlayerTrack TrackAtTwoHertz(int samples, int capacity = PlayerTrack.DefaultCapacity)
        {
            var track = new PlayerTrack(capacity);
            for (int i = 0; i < samples; i++)
                track.Record(i * 0.5f, Cell(i));
            return track;
        }

        [Test]
        public void FewerThanTwoSamplesGivesNoPast()
        {
            var track = new PlayerTrack();
            Assert.IsFalse(track.TryGetPast(0f, out _));

            track.Record(0f, Cell(0));
            Assert.IsFalse(track.TryGetPast(10f, out _));
            Assert.IsTrue(track.TryGetCurrent(out Vector2Int current));
            Assert.AreEqual(Cell(0), current);
        }

        [Test]
        public void PastIsTheNewestSampleAtLeastFiveSecondsOld()
        {
            // Samples at 0, 0.5, ..., 9.5 s. At t = 9.5 the cutoff is 4.5 s, i.e. sample 9.
            PlayerTrack track = TrackAtTwoHertz(20);

            Assert.IsTrue(track.TryGetPast(9.5f, out Vector2Int past));
            Assert.AreEqual(Cell(9), past);
        }

        [Test]
        public void PastBetweenSamplesUsesTheOlderOne()
        {
            PlayerTrack track = TrackAtTwoHertz(20);

            // Cutoff 4.8 s: sample 9 (4.5 s) is old enough, sample 10 (5.0 s) is not.
            Assert.IsTrue(track.TryGetPast(9.8f, out Vector2Int past));
            Assert.AreEqual(Cell(9), past);
        }

        [Test]
        public void ShortHistoryFallsBackToTheOldestSample()
        {
            // Only 2 s of history so far: nothing is 5 s old yet.
            PlayerTrack track = TrackAtTwoHertz(5);

            Assert.IsTrue(track.TryGetPast(2f, out Vector2Int past));
            Assert.AreEqual(Cell(0), past);
        }

        [Test]
        public void FullBufferOverwritesTheOldestSamples()
        {
            PlayerTrack track = TrackAtTwoHertz(6, capacity: 4);

            Assert.AreEqual(4, track.Count);
            Assert.IsTrue(track.TryGetCurrent(out Vector2Int current));
            Assert.AreEqual(Cell(5), current);

            // Samples 0 and 1 are gone, so the oldest left is sample 2.
            Assert.IsTrue(track.TryGetPast(2.5f, out Vector2Int past));
            Assert.AreEqual(Cell(2), past);
        }

        [Test]
        public void TimeGoingBackwardsClearsTheHistory()
        {
            PlayerTrack track = TrackAtTwoHertz(10);

            track.Record(0f, Cell(99));

            Assert.AreEqual(1, track.Count);
            Assert.IsFalse(track.TryGetPast(0f, out _));
        }

        [Test]
        public void ClearForgetsEverything()
        {
            PlayerTrack track = TrackAtTwoHertz(10);

            track.Clear();

            Assert.AreEqual(0, track.Count);
            Assert.IsFalse(track.TryGetCurrent(out _));
            Assert.IsFalse(track.TryGetPast(10f, out _));
        }

        [Test]
        public void InvalidArgumentsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerTrack(capacity: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerTrack(window: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerTrack(window: float.NaN));
        }

        [Test]
        public void RecordingAndReadingAllocatesZeroBytes()
        {
            var track = new PlayerTrack(capacity: 16);
            for (int i = 0; i < 3; i++)
            {
                track.Record(i, Cell(i));
                track.TryGetPast(i, out _);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 3; i < 200; i++)
            {
                track.Record(i * 0.5f, Cell(i));
                track.TryGetPast(i * 0.5f, out _);
                track.TryGetCurrent(out _);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated);
        }
    }
}
