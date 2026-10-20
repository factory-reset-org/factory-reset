using NUnit.Framework;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core.Blackboard;

namespace ToyFactory.Tests.EditMode
{
    public class SquadClaimTests
    {
        const int IdA = 1;
        const int IdB = 2;
        const int IdC = 3;
        const int IdD = 4;

        static readonly ActionKey Door = new ActionKey(SaboteurActionKind.CloseDoor, 10);
        static readonly ActionKey OtherDoor = new ActionKey(SaboteurActionKind.CloseDoor, 11);
        static readonly ActionKey Battery = new ActionKey(SaboteurActionKind.StealBattery, 20);
        static readonly ActionKey Attack = new ActionKey(SaboteurActionKind.AttackPlayer);
        static readonly ActionKey Idle = new ActionKey(SaboteurActionKind.Idle);

        TargetClaims _claims;
        SquadCoordinator _squad;

        [SetUp]
        public void SetUp()
        {
            _claims = new TargetClaims();
            _squad = new SquadCoordinator(_claims);
            _squad.Register(new SaboteurIdentity(IdA, SaboteurLetter.A));
            _squad.Register(new SaboteurIdentity(IdB, SaboteurLetter.B));
            _squad.Register(new SaboteurIdentity(IdC, SaboteurLetter.C));
            _squad.Register(new SaboteurIdentity(IdD, SaboteurLetter.D));
        }

        [Test]
        public void TwoInstancesCannotHoldTheSameClaim()
        {
            Assert.IsTrue(_squad.TryCommit(IdA, Door, 0.6f));

            Assert.AreEqual(0f, _squad.NotClaimedFactor(IdB, Door));
            Assert.AreEqual(1f, _squad.NotClaimedFactor(IdA, Door));
            Assert.IsFalse(_squad.TryCommit(IdB, Door, 0.5f));
            Assert.AreEqual(IdA, _claims.ClaimedBy(Door.TargetId));
        }

        [Test]
        public void UnclaimedTargetsAndUnclaimableActionsScoreNormally()
        {
            Assert.IsTrue(_squad.TryCommit(IdA, Door, 0.6f));

            Assert.AreEqual(1f, _squad.NotClaimedFactor(IdB, OtherDoor));
            Assert.AreEqual(1f, _squad.NotClaimedFactor(IdB, Battery));
            Assert.AreEqual(1f, _squad.NotClaimedFactor(IdB, Attack));
        }

        [Test]
        public void EqualScoresGoToTheLowerId()
        {
            Assert.IsTrue(_squad.TryCommit(IdB, Door, 0.6f));
            Assert.IsTrue(_squad.TryCommit(IdA, Door, 0.6f));
            Assert.AreEqual(IdA, _claims.ClaimedBy(Door.TargetId));

            Assert.IsFalse(_squad.TryCommit(IdC, Door, 0.6f));
            Assert.AreEqual(IdA, _claims.ClaimedBy(Door.TargetId));
        }

        [Test]
        public void OutscoredHolderIsDetectedOnItsNextTick()
        {
            Assert.IsTrue(_squad.TryCommit(IdA, Door, 0.5f));
            Assert.IsFalse(_squad.CheckOutscored(IdA));

            Assert.IsTrue(_squad.TryCommit(IdB, Door, 0.8f));

            Assert.IsTrue(_squad.CheckOutscored(IdA));
            Assert.AreEqual(IdB, _claims.ClaimedBy(Door.TargetId));
            Assert.IsFalse(_squad.CheckOutscored(IdA), "The plan is dropped once, not reported every tick.");
            Assert.IsFalse(_squad.CheckOutscored(IdB));
        }

        [Test]
        public void SwitchingPlanReleasesTheOldClaim()
        {
            Assert.IsTrue(_squad.TryCommit(IdA, Door, 0.6f));
            Assert.IsTrue(_squad.TryCommit(IdA, Battery, 0.7f));

            Assert.IsNull(_claims.ClaimedBy(Door.TargetId));
            Assert.AreEqual(IdA, _claims.ClaimedBy(Battery.TargetId));
        }

        [Test]
        public void EndingAPlanReleasesItsClaim()
        {
            // Action end, invalidation and stun all end the plan the same way.
            Assert.IsTrue(_squad.TryCommit(IdA, Door, 0.6f));

            _squad.EndPlan(IdA);

            Assert.IsNull(_claims.ClaimedBy(Door.TargetId));
            Assert.AreEqual(1f, _squad.NotClaimedFactor(IdB, Door));
            Assert.IsFalse(_squad.CheckOutscored(IdA));
        }

        [Test]
        public void DestructionReleasesAllClaims()
        {
            Assert.IsTrue(_squad.TryCommit(IdA, Door, 0.6f));
            Assert.IsTrue(_squad.TryCommit(IdB, Battery, 0.6f));
            // A claim held outside the current plan is released too.
            Assert.IsTrue(_claims.TryClaim(OtherDoor.TargetId, IdA, 0.4f));

            _squad.OnDestroyed(IdA);

            Assert.IsTrue(_squad.IsDestroyed(IdA));
            Assert.IsNull(_claims.ClaimedBy(Door.TargetId));
            Assert.IsNull(_claims.ClaimedBy(OtherDoor.TargetId));
            Assert.AreEqual(IdB, _claims.ClaimedBy(Battery.TargetId));
            Assert.IsFalse(_squad.TryCommit(IdA, Door, 0.9f));
            Assert.IsNull(_claims.ClaimedBy(Door.TargetId));
        }

        [Test]
        public void ThirdAttackerIsSaturated()
        {
            Assert.IsTrue(_squad.TryCommit(IdA, Attack, 0.6f));
            Assert.AreEqual(1f, _squad.AttackSaturation(IdB), "One other attacker.");
            Assert.IsTrue(_squad.TryCommit(IdB, Attack, 0.6f));

            Assert.AreEqual(SquadCoordinator.SaturatedAttackMultiplier, _squad.AttackSaturation(IdC));
            Assert.AreEqual(0.45f, _squad.AttackSaturation(IdC));
            Assert.AreEqual(1f, _squad.AttackSaturation(IdA), "An attacker does not count itself.");
        }

        [Test]
        public void DestroyedOrStoppedAttackersNoLongerSaturate()
        {
            Assert.IsTrue(_squad.TryCommit(IdA, Attack, 0.6f));
            Assert.IsTrue(_squad.TryCommit(IdB, Attack, 0.6f));

            _squad.OnDestroyed(IdA);
            Assert.AreEqual(1f, _squad.AttackSaturation(IdC));

            Assert.IsTrue(_squad.TryCommit(IdD, Attack, 0.6f));
            Assert.AreEqual(0.45f, _squad.AttackSaturation(IdC));
            Assert.IsTrue(_squad.TryCommit(IdD, Idle, 0.1f));
            Assert.AreEqual(1f, _squad.AttackSaturation(IdC));
        }

        [Test]
        public void FourDecisionOffsetsAre62Point5MsApart()
        {
            Assert.AreEqual(0f, SquadCoordinator.DecisionOffset(SaboteurLetter.A) * 1000f);
            Assert.AreEqual(62.5f, SquadCoordinator.DecisionOffset(SaboteurLetter.B) * 1000f);
            Assert.AreEqual(125f, SquadCoordinator.DecisionOffset(SaboteurLetter.C) * 1000f);
            Assert.AreEqual(187.5f, SquadCoordinator.DecisionOffset(SaboteurLetter.D) * 1000f);

            for (int i = 1; i < 4; i++)
            {
                float gap = SquadCoordinator.DecisionOffset((SaboteurLetter)i)
                    - SquadCoordinator.DecisionOffset((SaboteurLetter)(i - 1));
                Assert.AreEqual(62.5f, gap * 1000f);
            }

            // All four fit inside one 4 Hz period, so the pattern repeats without overlap.
            Assert.AreEqual(SaboteurBrain.DecisionInterval,
                SquadCoordinator.DecisionOffset(SaboteurLetter.D) + SquadCoordinator.StaggerStep);
        }

        [Test]
        public void UnknownOrDuplicateMembersAreRejected()
        {
            Assert.Throws<System.ArgumentException>(() => _squad.NotClaimedFactor(99, Door));
            Assert.Throws<System.ArgumentException>(() => _squad.Register(new SaboteurIdentity(IdA, SaboteurLetter.A)));
            Assert.Throws<System.ArgumentException>(() => _squad.Register(default));
        }
    }
}
