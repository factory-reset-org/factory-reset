using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests.EditMode
{
    public sealed class TimelineMarkersTests
    {
        TimelineAsset _timeline;

        [SetUp]
        public void SetUp()
        {
            _timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _timeline.CreateMarkerTrack();
            _timeline.markerTrack.CreateMarker<DialogueMarker>(0).Configure(0, false);
            _timeline.markerTrack.CreateMarker<CriticalSignalMarker>(2).Configure("CaptainWake");
            _timeline.markerTrack.CreateMarker<ShotEndMarker>(4);
            _timeline.markerTrack.CreateMarker<DialogueMarker>(4.05).Configure(1, false);
            _timeline.markerTrack.CreateMarker<ShotEndMarker>(9);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_timeline);

        [Test]
        public void BetweenTakesWhatIsAfterTheStartUpToAndIncludingTheEnd()
        {
            var found = TimelineMarkers.Between(_timeline, 0, 4);
            CollectionAssert.AreEqual(new[] { 2.0, 4.0 }, found.Select(m => m.time));
            Assert.IsInstanceOf<CriticalSignalMarker>(found[0]);
            Assert.IsInstanceOf<ShotEndMarker>(found[1]);
        }

        [Test]
        public void NextShotEndIsTheFirstOneLaterThanNow()
        {
            Assert.IsTrue(TimelineMarkers.NextShotEnd(_timeline, 1.5, out double first));
            Assert.AreEqual(4.0, first);
            Assert.IsTrue(TimelineMarkers.NextShotEnd(_timeline, 4.0, out double second), "One exactly at now has already passed.");
            Assert.AreEqual(9.0, second);
            Assert.IsFalse(TimelineMarkers.NextShotEnd(_timeline, 9.0, out _));
        }

        [Test]
        public void ANullTimelineHasNoMarkers()
        {
            Assert.IsEmpty(TimelineMarkers.Between(null, 0, 10));
            Assert.IsFalse(TimelineMarkers.NextShotEnd(null, 0, out _));
        }
    }
}
