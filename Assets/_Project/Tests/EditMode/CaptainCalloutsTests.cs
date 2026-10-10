using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The Captain's call-outs: one flash and one line per new commitment, never a line it
    /// cannot be seen saying, rate-limited, a feint answered with its own line, and the line
    /// picked by the kind of goal.
    /// </summary>
    public class CaptainCalloutsTests
    {
        static readonly CandidateGoal TaskA = new CandidateGoal(1, new Vector2Int(5, 5), GoalCategory.Task);
        static readonly CandidateGoal TaskB = new CandidateGoal(2, new Vector2Int(9, 5), GoalCategory.Task);
        static readonly CandidateGoal Console = new CandidateGoal(300, new Vector2Int(20, 20), GoalCategory.Console);
        static readonly CandidateGoal Switch = new CandidateGoal(201, new Vector2Int(30, 2), GoalCategory.Switch);

        static Callout Commit(CaptainCallouts callouts, float time, CandidateGoal goal, bool visible = true,
            bool guarding = false, bool finalChapter = false) =>
            callouts.Update(time, true, goal, guarding, finalChapter, visible);

        static Callout Idle(CaptainCallouts callouts, float time) =>
            callouts.Update(time, false, default, false, false, true);

        [Test]
        public void ANewCommitmentFlashesTheGoalAndSaysOneLineOnce()
        {
            var callouts = new CaptainCallouts();
            Assert.IsTrue(Idle(callouts, 0f).IsNone, "Watching: nothing.");

            Callout first = Commit(callouts, 1f, TaskA);
            Assert.IsTrue(first.Mark);
            Assert.IsNotNull(first.Line);

            for (float t = 1.1f; t < 30f; t += 0.5f)
                Assert.IsTrue(Commit(callouts, t, TaskA).IsNone, $"Still the same commitment at t = {t}: quiet.");
        }

        [Test]
        public void OutOfSightItOnlyFlashesTheGoal()
        {
            var callouts = new CaptainCallouts();
            Callout hidden = Commit(callouts, 1f, TaskA, visible: false);
            Assert.IsTrue(hidden.Mark, "The marker still tells the player.");
            Assert.IsNull(hidden.Line, "No line nobody sees it say.");

            Assert.IsTrue(Commit(callouts, 2f, TaskA, visible: true).IsNone, "The skipped line is not said late.");
        }

        [Test]
        public void LinesAreAtLeastEightSecondsApartAndMarksTwo()
        {
            var callouts = new CaptainCallouts();
            Commit(callouts, 0f, TaskA);

            Callout quick = Commit(callouts, 1f, TaskB);
            Assert.IsFalse(quick.Mark, "Marks at most every 2 s.");
            Assert.IsNull(quick.Line);

            Callout soon = Commit(callouts, 3f, Console);
            Assert.IsTrue(soon.Mark);
            Assert.IsNull(soon.Line, "Lines at most every 8 s.");

            Callout later = Commit(callouts, 8.5f, Switch);
            Assert.IsNotNull(later.Line);
        }

        [Test]
        public void TheSameGoalIsCalledOutAgainOnlyAfterTwentySeconds()
        {
            var callouts = new CaptainCallouts();
            Commit(callouts, 0f, TaskA);
            Idle(callouts, 2f);   // a fight, say

            Callout again = Commit(callouts, 10f, TaskA);
            Assert.IsTrue(again.Mark, "Re-committing still flashes the goal.");
            Assert.IsNull(again.Line, "But it does not repeat itself about it.");

            Idle(callouts, 12f);
            Assert.IsNotNull(Commit(callouts, 21f, TaskA).Line);
        }

        [Test]
        public void SwitchingGoalsIsAnsweredAsAFeint()
        {
            var callouts = new CaptainCallouts();
            Commit(callouts, 0f, TaskA);

            Callout switched = Commit(callouts, 9f, TaskB);
            CollectionAssert.Contains(new[] { "Changing your mind will not help.", "A feint? I saw it." }, switched.Line);

            // A gap of more than 6 s with no commitment is a fresh start, not a feint.
            Idle(callouts, 10f);
            Callout fresh = Commit(callouts, 19f, Console);
            StringAssert.Contains("console", fresh.Line);
        }

        [Test]
        public void TheLineFitsTheGoal()
        {
            Assert.That(Commit(new CaptainCallouts(), 0f, Console).Line, Does.Contain("console"));
            Assert.That(Commit(new CaptainCallouts(), 0f, Switch).Line, Does.Contain("switch"));
            Assert.That(Commit(new CaptainCallouts(), 0f, TaskA, finalChapter: true).Line, Does.Contain("core"),
                "In Chapter 4 the tasks are the cores.");
            string guard = Commit(new CaptainCallouts(), 0f, Console, guarding: true).Line;
            CollectionAssert.Contains(new[] { "Hide if you like. You must come here.", "I do not need to find you. I only wait." }, guard);
        }

        [Test]
        public void ALineNeverFollowsItself()
        {
            var callouts = new CaptainCallouts();
            var said = new List<string>();
            float t = 0f;
            for (int i = 0; i < 4; i++)
            {
                said.Add(Commit(callouts, t, Console).Line);
                Idle(callouts, t + 1f);
                t += CaptainCallouts.RepeatAfter + 1f;
            }
            for (int i = 1; i < said.Count; i++)
                Assert.AreNotEqual(said[i - 1], said[i]);
        }

        [Test]
        public void EveryLineHasItsComicBubble()
        {
            Assert.AreEqual("Say_TheConsoleOfCourse", CaptainCallouts.BubbleName("The console. Of course."));
            Assert.AreEqual("Say_ThatTask047IAmAlreadyThere", CaptainCallouts.BubbleName("That task, 047? I am already there."));
            foreach (string line in CaptainCallouts.AllLines())
            {
                string path = $"Assets/_Project/Textures/FX/CaptainSays/{CaptainCallouts.BubbleName(line)}.png";
                Assert.IsTrue(System.IO.File.Exists(path), $"No bubble for \"{line}\": {path}");
            }
        }

        [Test]
        public void EveryLineFitsAboveItsHead()
        {
            foreach (string line in CaptainCallouts.AllLines())
                Assert.LessOrEqual(line.Length, 42, line);
            Assert.AreEqual(CaptainCallouts.AllLines().Count(), CaptainCallouts.AllLines().Distinct().Count(), "No line twice.");
        }
    }
}
