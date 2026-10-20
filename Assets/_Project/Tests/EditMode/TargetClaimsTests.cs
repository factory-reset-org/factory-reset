using NUnit.Framework;
using ToyFactory.AI.Core.Blackboard;

namespace ToyFactory.Tests.EditMode
{
    public class TargetClaimsTests
    {
        const int TargetA = 1;
        const int TargetB = 2;
        const int AgentX = 10;
        const int AgentY = 20;

        [Test]
        public void UnclaimedTargetCanBeClaimed()
        {
            var claims = new TargetClaims();

            bool claimed = claims.TryClaim(TargetA, AgentX, 0.5f);

            Assert.IsTrue(claimed);
            Assert.AreEqual(AgentX, claims.ClaimedBy(TargetA));
        }

        [Test]
        public void LowerScoreCannotTakeAnExistingClaim()
        {
            var claims = new TargetClaims();
            claims.TryClaim(TargetA, AgentX, 0.8f);

            bool claimed = claims.TryClaim(TargetA, AgentY, 0.5f);

            Assert.IsFalse(claimed);
            Assert.AreEqual(AgentX, claims.ClaimedBy(TargetA));
        }

        [Test]
        public void HigherScoreTakesOverAnExistingClaim()
        {
            var claims = new TargetClaims();
            claims.TryClaim(TargetA, AgentX, 0.5f);

            bool claimed = claims.TryClaim(TargetA, AgentY, 0.8f);

            Assert.IsTrue(claimed);
            Assert.AreEqual(AgentY, claims.ClaimedBy(TargetA));
        }

        [Test]
        public void TieGoesToTheLowerAgentIdWhenChallengerIsLower()
        {
            var claims = new TargetClaims();
            claims.TryClaim(TargetA, AgentY, 0.5f);

            bool claimed = claims.TryClaim(TargetA, AgentX, 0.5f);

            Assert.IsTrue(claimed);
            Assert.AreEqual(AgentX, claims.ClaimedBy(TargetA));
        }

        [Test]
        public void TieKeepsTheExistingHolderWhenItIsAlreadyLower()
        {
            var claims = new TargetClaims();
            claims.TryClaim(TargetA, AgentX, 0.5f);

            bool claimed = claims.TryClaim(TargetA, AgentY, 0.5f);

            Assert.IsFalse(claimed);
            Assert.AreEqual(AgentX, claims.ClaimedBy(TargetA));
        }

        [Test]
        public void SameAgentCanRefreshItsOwnClaimWithAnyScore()
        {
            var claims = new TargetClaims();
            claims.TryClaim(TargetA, AgentX, 0.9f);

            bool claimed = claims.TryClaim(TargetA, AgentX, 0.1f);

            Assert.IsTrue(claimed);
            Assert.AreEqual(AgentX, claims.ClaimedBy(TargetA));
        }

        [Test]
        public void ReleaseRemovesTheClaimSoAnotherAgentCanTakeIt()
        {
            var claims = new TargetClaims();
            claims.TryClaim(TargetA, AgentX, 0.9f);

            claims.Release(AgentX);

            Assert.IsNull(claims.ClaimedBy(TargetA));
            Assert.IsTrue(claims.TryClaim(TargetA, AgentY, 0.1f));
        }

        [Test]
        public void ReleaseOnlyAffectsThatAgentsClaims()
        {
            var claims = new TargetClaims();
            claims.TryClaim(TargetA, AgentX, 0.9f);
            claims.TryClaim(TargetB, AgentY, 0.9f);

            claims.Release(AgentX);

            Assert.IsNull(claims.ClaimedBy(TargetA));
            Assert.AreEqual(AgentY, claims.ClaimedBy(TargetB));
        }

        [Test]
        public void ClaimedByReturnsNullForAnUnclaimedTarget()
        {
            var claims = new TargetClaims();

            Assert.IsNull(claims.ClaimedBy(TargetA));
        }
    }
}
