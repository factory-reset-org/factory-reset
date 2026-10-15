using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The Captain's prediction marker: one flash per new commitment (a new goal, or a switch
    /// to another), none while the commitment holds, and at most one every 2 s.
    /// </summary>
    public class CaptainCalloutsTests
    {
        static readonly CandidateGoal TaskA = new CandidateGoal(1, new Vector2Int(5, 5), GoalCategory.Task);
        static readonly CandidateGoal TaskB = new CandidateGoal(2, new Vector2Int(9, 5), GoalCategory.Task);
        static readonly CandidateGoal Console = new CandidateGoal(300, new Vector2Int(20, 20), GoalCategory.Console);

        static bool Commit(CaptainCallouts callouts, float time, CandidateGoal goal) => callouts.Update(time, true, goal);

        static bool Idle(CaptainCallouts callouts, float time) => callouts.Update(time, false, default);

        [Test]
        public void ANewCommitmentFlashesTheGoalOnce()
        {
            var callouts = new CaptainCallouts();
            Assert.IsFalse(Idle(callouts, 0f), "Watching: nothing.");
            Assert.IsTrue(Commit(callouts, 1f, TaskA));
            for (float t = 1.1f; t < 30f; t += 0.5f)
                Assert.IsFalse(Commit(callouts, t, TaskA), $"Still the same commitment at t = {t}: quiet.");
        }

        [Test]
        public void ASwitchToAnotherGoalFlashesItButAtMostEveryTwoSeconds()
        {
            var callouts = new CaptainCallouts();
            Commit(callouts, 0f, TaskA);
            Assert.IsFalse(Commit(callouts, 1f, TaskB), "Marks at most every 2 s.");
            Assert.IsTrue(Commit(callouts, 3f, Console), "A switch flashes the new goal.");
        }

        [Test]
        public void CommittingAgainAfterLettingGoFlashesAgain()
        {
            var callouts = new CaptainCallouts();
            Commit(callouts, 0f, TaskA);
            Idle(callouts, 2f);   // a fight, say
            Assert.IsTrue(Commit(callouts, 10f, TaskA), "Re-committing flashes the goal again.");
        }
    }
}
