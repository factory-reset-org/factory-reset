using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The cutscene cues match Story.md's shot lists, point at real shots, and put every comic
    /// word inside the room its shot films, under the ceiling.
    /// </summary>
    public class CutsceneCuePlanTests
    {
        [Test]
        public void EveryCueBelongsToAShotOfItsCutscene()
        {
            foreach (CutsceneCue cue in CutsceneCuePlan.All)
            {
                var shots = CutsceneShotPlan.For(cue.CutsceneId);
                Assert.IsTrue(shots.Any(s => s.Index == cue.Shot), $"{cue.CutsceneId} has no shot {cue.Shot}.");
                Assert.GreaterOrEqual(cue.AtLine, 0);
            }
        }

        [Test]
        public void TheStorysCuesAreAllThere()
        {
            // Story.md: alarms in the intro (shot 1), ch2 (shot 2), ch3 (shot 1) and ch4 (shot 1);
            // "?!" on the Tracker, "DEFECTIVE!" on 047 with the second line, "SHUTDOWN" at the console.
            Assert.IsTrue(Has("intro", 0, CutsceneCueKind.Alarm));
            Assert.IsTrue(Has("ch2", 1, CutsceneCueKind.Alarm));
            Assert.IsTrue(Has("ch3", 0, CutsceneCueKind.Alarm));
            Assert.IsTrue(Has("ch4", 0, CutsceneCueKind.Alarm));
            Assert.IsTrue(HasWord("intro", 1, ComicWord.Alert));
            Assert.IsTrue(HasWord("intro", 2, ComicWord.Attention));
            CutsceneCue defective = CutsceneCuePlan.For("intro", 4).Single(c => c.Word == ComicWord.Defective && c.Kind == CutsceneCueKind.Pop);
            Assert.IsTrue(defective.OnActor, "Above Unit 047, wherever the player stands.");
            Assert.AreEqual(1, defective.AtLine, "With \"Product status: DEFECTIVE.\"");
            Assert.IsTrue(HasWord("ending", 0, ComicWord.Shutdown));
        }

        [Test]
        public void WordsPopUpInsideTheRoomTheirShotFilmsUnderTheCeiling()
        {
            foreach (CutsceneCue cue in CutsceneCuePlan.All.Where(c => c.Kind == CutsceneCueKind.Pop && !c.OnActor))
            {
                CutsceneShot shot = CutsceneShotPlan.For(cue.CutsceneId).Single(s => s.Index == cue.Shot);
                Rect room = CutsceneShotPlan.Interior(shot.Room);
                Assert.IsTrue(room.Contains(new Vector2(cue.Point.x, cue.Point.z)), $"{cue.Word} is outside the {shot.Room} room.");
                Assert.Less(cue.Point.y + 0.5f, CutsceneShotPlan.CeilingHeight, $"{cue.Word} would touch the ceiling.");
            }
        }

        static bool Has(string id, int shot, CutsceneCueKind kind) =>
            CutsceneCuePlan.For(id, shot).Any(c => c.Kind == kind);

        static bool HasWord(string id, int shot, ComicWord word) =>
            CutsceneCuePlan.For(id, shot).Any(c => c.Kind == CutsceneCueKind.Pop && c.Word == word);
    }
}
