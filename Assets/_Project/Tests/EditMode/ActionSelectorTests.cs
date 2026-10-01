using System;
using System.Collections.Generic;
using NUnit.Framework;
using ToyFactory.AI.Agents.Saboteur;

namespace ToyFactory.Tests.EditMode
{
    public class ActionSelectorTests
    {
        static readonly ActionKey DoorA = new ActionKey(SaboteurActionKind.CloseDoor, 1);
        static readonly ActionKey DoorB = new ActionKey(SaboteurActionKind.CloseDoor, 2);
        static readonly ActionKey Battery = new ActionKey(SaboteurActionKind.StealBattery, 7);
        static readonly ActionKey Attack = new ActionKey(SaboteurActionKind.AttackPlayer);
        static readonly ActionKey Idle = new ActionKey(SaboteurActionKind.Idle);

        static ActionCandidate C(ActionKey key, float score) => new ActionCandidate(key, score);

        static List<ActionCandidate> List(params ActionCandidate[] items) => new List<ActionCandidate>(items);

        [Test]
        public void HighestScoreWins()
        {
            var selector = new ActionSelector();

            SelectionResult result = selector.Select(List(C(Idle, 0.1f), C(DoorA, 0.6f), C(Battery, 0.4f)), 0f);

            Assert.IsTrue(result.HasSelection);
            Assert.AreEqual(DoorA, result.Key);
            Assert.IsTrue(result.Switched);
        }

        [Test]
        public void NoUsableCandidateGivesNoSelection()
        {
            var selector = new ActionSelector();

            SelectionResult result = selector.Select(List(C(DoorA, 0f)), 0f);

            Assert.IsFalse(result.HasSelection);
            Assert.IsFalse(selector.HasCurrent);
        }

        [Test]
        public void EqualScoresUseActionOrderThenLowerTargetId()
        {
            var selector = new ActionSelector();

            Assert.AreEqual(DoorA, selector.Select(List(C(Battery, 0.5f), C(DoorB, 0.5f), C(DoorA, 0.5f)), 0f).Key);
        }

        [Test]
        public void EqualScoresKeepTheCurrentPair()
        {
            var selector = new ActionSelector(new SelectorSettings(momentum: 0f, commitmentSeconds: 0f));
            selector.Select(List(C(DoorB, 0.5f)), 0f);

            SelectionResult result = selector.Select(List(C(DoorA, 0.5f), C(DoorB, 0.5f)), 1f);

            Assert.AreEqual(DoorB, result.Key);
            Assert.IsFalse(result.Switched);
        }

        [Test]
        public void MomentumHoldsWhenScoresDifferByLessThanTheBonus()
        {
            var selector = new ActionSelector(new SelectorSettings(commitmentSeconds: 0f));
            selector.Select(List(C(DoorA, 0.5f)), 0f);

            SelectionResult result = selector.Select(List(C(DoorA, 0.5f), C(Battery, 0.6f)), 1f);

            Assert.AreEqual(DoorA, result.Key);
            Assert.AreEqual(0.65f, result.RankingScore, 1e-5f);
        }

        [Test]
        public void MomentumDoesNotHoldWhenScoresDifferByMoreThanTheBonus()
        {
            var selector = new ActionSelector(new SelectorSettings(commitmentSeconds: 0f));
            selector.Select(List(C(DoorA, 0.5f)), 0f);

            SelectionResult result = selector.Select(List(C(DoorA, 0.5f), C(Battery, 0.7f)), 1f);

            Assert.AreEqual(Battery, result.Key);
            Assert.IsTrue(result.Switched);
        }

        [Test]
        public void MomentumIsCappedAtOne()
        {
            var selector = new ActionSelector(new SelectorSettings(commitmentSeconds: 0f));
            selector.Select(List(C(DoorA, 0.95f)), 0f);

            Assert.AreEqual(1f, selector.Select(List(C(DoorA, 0.95f)), 1f).RankingScore);
        }

        [Test]
        public void CommitmentKeepsTheCurrentPairAgainstABetterCandidate()
        {
            var selector = new ActionSelector();
            selector.Select(List(C(DoorA, 0.4f)), 0f);

            SelectionResult result = selector.Select(List(C(DoorA, 0.4f), C(Battery, 0.8f)), 1.0f);

            Assert.AreEqual(DoorA, result.Key);
        }

        [Test]
        public void CommitmentEndsAfterTheWindow()
        {
            var selector = new ActionSelector();
            selector.Select(List(C(DoorA, 0.4f)), 0f);

            SelectionResult result = selector.Select(List(C(DoorA, 0.4f), C(Battery, 0.8f)), 1.5f);

            Assert.AreEqual(Battery, result.Key);
        }

        [Test]
        public void ABaseScoreAboveTheEmergencyThresholdInterruptsCommitment()
        {
            var selector = new ActionSelector();
            selector.Select(List(C(DoorA, 0.4f)), 0f);

            SelectionResult result = selector.Select(List(C(DoorA, 0.4f), C(Attack, 0.95f)), 0.5f);

            Assert.AreEqual(Attack, result.Key);
            Assert.IsTrue(result.Switched);
        }

        [Test]
        public void ABaseScoreExactlyAtTheThresholdDoesNotInterrupt()
        {
            var selector = new ActionSelector();
            selector.Select(List(C(DoorA, 0.4f)), 0f);

            Assert.AreEqual(DoorA, selector.Select(List(C(DoorA, 0.4f), C(Attack, 0.9f)), 0.5f).Key);
        }

        [Test]
        public void InvalidCurrentTargetCancelsThePlanDuringCommitment()
        {
            var selector = new ActionSelector();
            selector.Select(List(C(DoorA, 0.8f)), 0f);

            SelectionResult result = selector.Select(List(C(Idle, 0.1f)), 0.2f);

            Assert.AreEqual(Idle, result.Key);
            Assert.IsTrue(result.Switched);
        }

        [Test]
        public void DoorCannotBeReselectedForTenSecondsAfterConfirmedSuccess()
        {
            var selector = new ActionSelector();
            selector.Select(List(C(DoorA, 0.8f), C(Idle, 0.1f)), 0f);
            selector.NotifySuccess(DoorA, 2f);

            Assert.IsTrue(selector.IsOnCooldown(DoorA, 11.9f));
            Assert.AreEqual(Idle, selector.Select(List(C(DoorA, 0.8f), C(Idle, 0.1f)), 11.9f).Key);
            Assert.IsFalse(selector.IsOnCooldown(DoorA, 12f));
            Assert.AreEqual(DoorA, selector.Select(List(C(DoorA, 0.8f), C(Idle, 0.1f)), 12f).Key);
        }

        [Test]
        public void IdleIsNotHeldByCommitment()
        {
            var selector = new ActionSelector();
            selector.Select(List(C(Idle, 0.1f)), 0f);

            SelectionResult result = selector.Select(List(C(Idle, 0.1f), C(DoorA, 0.5f)), 0.1f);

            Assert.AreEqual(DoorA, result.Key);
        }

        [Test]
        public void NoCooldownStartsBeforeSuccessIsConfirmed()
        {
            var selector = new ActionSelector();
            selector.Select(List(C(DoorA, 0.8f)), 0f);

            Assert.IsFalse(selector.IsOnCooldown(DoorA, 0.1f));
        }

        [Test]
        public void CooldownAppliesToThatDoorOnly()
        {
            var selector = new ActionSelector();
            selector.NotifySuccess(DoorA, 0f);

            Assert.AreEqual(DoorB, selector.Select(List(C(DoorA, 0.9f), C(DoorB, 0.5f)), 1f).Key);
        }

        [Test]
        public void ActionsWithoutAnAgreedCooldownHaveNone()
        {
            var selector = new ActionSelector();
            selector.NotifySuccess(Battery, 0f);

            Assert.IsFalse(selector.IsOnCooldown(Battery, 0.1f));
        }

        [Test]
        public void CancelCurrentForgetsThePairButKeepsCooldowns()
        {
            var selector = new ActionSelector();
            selector.Select(List(C(DoorA, 0.8f)), 0f);
            selector.NotifySuccess(DoorA, 0f);

            selector.CancelCurrent();

            Assert.IsFalse(selector.HasCurrent);
            Assert.IsTrue(selector.IsOnCooldown(DoorA, 1f));
        }

        [Test]
        public void TimersFollowTheSuppliedTimeSoAFrozenClockDoesNotAdvanceCommitment()
        {
            var selector = new ActionSelector();
            selector.Select(List(C(DoorA, 0.4f)), 5f);

            // Game time has not moved, as during a cutscene, so commitment still holds.
            Assert.AreEqual(DoorA, selector.Select(List(C(DoorA, 0.4f), C(Battery, 0.8f)), 5f).Key);
        }

        [Test]
        public void InvalidCandidateScoreIsRejected()
        {
            var selector = new ActionSelector();

            Assert.Throws<ArgumentException>(() => selector.Select(List(C(DoorA, float.NaN)), 0f));
            Assert.Throws<ArgumentException>(() => selector.Select(List(C(DoorA, 1.2f)), 0f));
        }

        [Test]
        public void SettingsRejectOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SelectorSettings(momentum: -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SelectorSettings(emergencyThreshold: 1.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SelectorSettings(commitmentSeconds: float.NaN));
        }
    }
}
