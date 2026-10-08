using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// Checks the Timelines the builder wrote: each shot starts its lines without stopping the
    /// camera, ends just before the next, lasts as long as its lines, and fires its signals.
    /// Rebuild the Timelines (Factory Reset/Cutscenes/Build Timelines) if these fail after a
    /// change to the shot plan or the dialogue.
    /// </summary>
    public sealed class CutsceneTimelineAssetTests
    {
        static TimelineAsset Load(string id) =>
            AssetDatabase.LoadAssetAtPath<TimelineAsset>($"Assets/_Project/Data/Cutscenes/{id}.playable");

        static DialogueScript Script(string id) =>
            AssetDatabase.LoadAssetAtPath<DialogueScript>($"Assets/_Project/Data/Dialogue/{id}.asset");

        static IEnumerable<string> Ids => CutsceneDefinition.JourneyDefaults().Select(d => d.Id);

        [Test]
        public void EveryShotStartsItsLinesThenEndsBeforeTheNextShot([ValueSource(nameof(Ids))] string id)
        {
            TimelineAsset timeline = Load(id);
            DialogueScript script = Script(id);
            Assert.IsNotNull(timeline, $"No Timeline for {id}: run Factory Reset/Cutscenes/Build Timelines.");

            List<IMarker> markers = TimelineMarkers.Between(timeline, -1, double.MaxValue)
                .Where(m => m is DialogueMarker || m is ShotEndMarker).ToList();
            Assert.AreEqual(script.ShotCount * 2, markers.Count, "One dialogue marker and one shot end per shot.");

            double expectedStart = 0;
            for (int shot = 0; shot < script.ShotCount; shot++)
            {
                var start = markers[shot * 2] as DialogueMarker;
                var end = markers[shot * 2 + 1] as ShotEndMarker;
                Assert.IsNotNull(start, $"Shot {shot + 1} starts with its dialogue marker.");
                Assert.IsNotNull(end, $"Shot {shot + 1} ends with a shot end.");
                Assert.AreEqual(shot, start.Shot);
                Assert.IsFalse(start.WaitForLines, "The camera keeps moving while the lines are said.");
                Assert.AreEqual(expectedStart, start.time, 1e-3, $"Shot {shot + 1} starts where the last one ended.");

                double length = DialogueRunner.ShotSeconds(script.LinesOf(shot)) + CutsceneShotPlan.ShotPadding;
                Assert.AreEqual(start.time + length, end.time, 0.06, $"Shot {shot + 1} lasts as long as its lines.");
                expectedStart = start.time + length;
            }
            Assert.AreEqual(expectedStart, timeline.duration, 1e-3);
        }

        [Test]
        public void SignalMarkersMatchTheCutsceneDefinition([ValueSource(nameof(Ids))] string id)
        {
            CutsceneDefinition definition = CutsceneDefinition.JourneyDefaults().First(d => d.Id == id);
            IEnumerable<string> marked = TimelineMarkers.Between(Load(id), -1, double.MaxValue)
                .OfType<CriticalSignalMarker>().Select(m => m.SignalId);
            CollectionAssert.AreEquivalent(definition.CriticalSignals, marked);
        }

        [Test]
        public void EveryShotHasTwoCameraClipsThatBlendIntoEachOther([ValueSource(nameof(Ids))] string id)
        {
            TimelineAsset timeline = Load(id);
            TrackAsset camera = timeline.GetOutputTracks().Single(t => t.name == "Camera");
            List<TimelineClip> clips = camera.GetClips().OrderBy(c => c.start).ToList();
            Assert.AreEqual(Script(id).ShotCount * 2, clips.Count);

            for (int i = 0; i < clips.Count; i += 2)
            {
                TimelineClip from = clips[i], to = clips[i + 1];
                Assert.Greater(from.end, to.start, "The two poses of a shot overlap.");
                Assert.AreEqual(from.end - to.start, to.blendInDuration, 1e-3, "The overlap is one long blend.");
            }
        }
    }
}
