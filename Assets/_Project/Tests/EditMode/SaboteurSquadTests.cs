using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>Brains that share one <see cref="TargetClaims"/> behave as a squad.</summary>
    public class SaboteurSquadTests
    {
        // Offers a fixed list of candidates and remembers when it was asked.
        sealed class FakeSource : ICandidateSource
        {
            readonly List<ActionCandidate> _items = new List<ActionCandidate>();
            public readonly List<float> DecisionTimes = new List<float>();

            public FakeSource(params ActionCandidate[] items) { _items.AddRange(items); }

            public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates)
            {
                DecisionTimes.Add(ctx.Time);
                candidates.AddRange(_items);
            }
        }

        static readonly ActionKey Door1 = new ActionKey(SaboteurActionKind.CloseDoor, 1);
        static readonly ActionKey Door2 = new ActionKey(SaboteurActionKind.CloseDoor, 2);
        static readonly ActionKey Door3 = new ActionKey(SaboteurActionKind.CloseDoor, 3);
        static readonly ActionKey Attack = new ActionKey(SaboteurActionKind.AttackPlayer);

        // Exactly representable, so the 62.5 ms stagger lines up with whole frames.
        const float Frame = 1f / 64f;

        GridGraph _grid;
        TargetClaims _claims;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(10, 10, Vector3.zero);
            _claims = new TargetClaims();
        }

        static ActionCandidate C(ActionKey key, float score) => new ActionCandidate(key, score);

        SaboteurBrain Brain(SaboteurLetter letter, ICandidateSource source = null, TargetClaims claims = null)
        {
            // Ids 1-4 for letters A-D, so a brain's claim owner id is easy to read in assertions.
            var identity = new SaboteurIdentity((int)letter + 1, letter);
            return new SaboteurBrain(identity, _grid, new AStarSearch(_grid), claims ?? _claims,
                new List<Vector3>(), null, 0, source);
        }

        static void Tick(SaboteurBrain brain, float time) =>
            brain.Tick(new AgentContext(new Vector2Int(5, 5), Vector3.zero, Vector3.forward, time,
                new WorldBlackboard(), default));

        static int AgentId(SaboteurLetter letter) => (int)letter + 1;

        [Test]
        public void TwoBrainsSharingClaimsChooseDifferentTargets()
        {
            SaboteurBrain a = Brain(SaboteurLetter.A, new FakeSource(C(Door1, 0.8f), C(Door2, 0.7f)));
            SaboteurBrain b = Brain(SaboteurLetter.B, new FakeSource(C(Door1, 0.8f), C(Door2, 0.7f)));

            Tick(a, 0f);
            Tick(b, 0f);
            Tick(b, SquadCoordinator.StaggerStep);

            Assert.AreEqual(Door1, a.CurrentAction);
            Assert.AreEqual(Door2, b.CurrentAction);
            Assert.AreEqual(AgentId(SaboteurLetter.A), _claims.ClaimedBy(1));
            Assert.AreEqual(AgentId(SaboteurLetter.B), _claims.ClaimedBy(2));
        }

        [Test]
        public void ThreeBrainsChooseThreeDifferentDoors()
        {
            var sources = new[]
            {
                new FakeSource(C(Door1, 0.8f), C(Door2, 0.7f), C(Door3, 0.6f)),
                new FakeSource(C(Door1, 0.8f), C(Door2, 0.7f), C(Door3, 0.6f)),
                new FakeSource(C(Door1, 0.8f), C(Door2, 0.7f), C(Door3, 0.6f))
            };
            SaboteurBrain a = Brain(SaboteurLetter.A, sources[0]);
            SaboteurBrain b = Brain(SaboteurLetter.B, sources[1]);
            SaboteurBrain c = Brain(SaboteurLetter.C, sources[2]);

            // Ticks run in order within each frame, as the controller does.
            for (int frame = 0; frame <= 8; frame++)
            {
                float time = frame * Frame;
                Tick(a, time);
                Tick(b, time);
                Tick(c, time);
            }

            Assert.AreEqual(Door1, a.CurrentAction);
            Assert.AreEqual(Door2, b.CurrentAction);
            Assert.AreEqual(Door3, c.CurrentAction);
        }

        [Test]
        public void DecisionsAreStaggeredByLetterAndNeverShareATick()
        {
            var sources = new[] { new FakeSource(), new FakeSource(), new FakeSource(), new FakeSource() };
            var brains = new[]
            {
                Brain(SaboteurLetter.A, sources[0]), Brain(SaboteurLetter.B, sources[1]),
                Brain(SaboteurLetter.C, sources[2]), Brain(SaboteurLetter.D, sources[3])
            };

            for (int frame = 0; frame < 64; frame++)
                foreach (SaboteurBrain brain in brains)
                    Tick(brain, frame * Frame);

            // 62.5 ms apart: A on frame 0, B on 4, C on 8, D on 12, then every 16 frames (4 Hz).
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(4, sources[i].DecisionTimes.Count, $"decisions of brain {i}");
                Assert.AreEqual(i * SquadCoordinator.StaggerStep, sources[i].DecisionTimes[0], 1e-6f);
            }

            var seen = new HashSet<float>();
            foreach (FakeSource source in sources)
                foreach (float time in source.DecisionTimes)
                    Assert.IsTrue(seen.Add(time), $"two decisions at {time}");
        }

        [Test]
        public void DecisionsStayOnTheirPhaseWhateverTheTickTimes()
        {
            var source = new FakeSource();
            SaboteurBrain b = Brain(SaboteurLetter.B, source);

            foreach (float time in new[] { 0f, 0.03f, 0.07f, 0.13f, 0.2f, 0.26f, 0.31f, 0.4f })
                Tick(b, time);

            // Slots are 0.0625 and 0.3125; 0.31 falls just before the second one.
            Assert.AreEqual(new[] { 0.07f, 0.4f }, source.DecisionTimes.ToArray());
        }

        [Test]
        public void ADestroyedBrainReleasesItsClaimsAndTheTargetIsFreeAgain()
        {
            SaboteurBrain a = Brain(SaboteurLetter.A, new FakeSource(C(Door1, 0.8f), C(Door2, 0.7f)));
            SaboteurBrain b = Brain(SaboteurLetter.B, new FakeSource(C(Door1, 0.8f), C(Door2, 0.7f)));
            Tick(a, 0f);
            Assert.AreEqual(AgentId(SaboteurLetter.A), _claims.ClaimedBy(1));

            a.OnDestroyed();
            Tick(b, 0f);
            Tick(b, SquadCoordinator.StaggerStep);

            Assert.AreEqual(AgentId(SaboteurLetter.B), _claims.ClaimedBy(1));
            Assert.AreEqual(Door1, b.CurrentAction);
            Assert.IsTrue(SquadCoordinator.For(_claims).IsDestroyed(AgentId(SaboteurLetter.A)));
        }

        [Test]
        public void AStunReleasesTheClaim()
        {
            SaboteurBrain a = Brain(SaboteurLetter.A, new FakeSource(C(Door1, 0.8f)));
            Tick(a, 0f);
            Assert.AreEqual(AgentId(SaboteurLetter.A), _claims.ClaimedBy(1));

            a.OnStunned(2f);

            Assert.IsNull(_claims.ClaimedBy(1));
        }

        [Test]
        public void RecommittingToTheSameTargetKeepsTheClaim()
        {
            SaboteurBrain a = Brain(SaboteurLetter.A, new FakeSource(C(Door1, 0.8f), C(Door2, 0.7f)));

            Tick(a, 0f);
            Tick(a, 0.25f);
            Tick(a, 0.5f);

            Assert.AreEqual(Door1, a.CurrentAction);
            Assert.AreEqual(AgentId(SaboteurLetter.A), _claims.ClaimedBy(1));
            Assert.IsNull(_claims.ClaimedBy(2));
        }

        [Test]
        public void AnInstanceOutscoredForItsTargetDropsThePlanAndChoosesAnother()
        {
            SaboteurBrain a = Brain(SaboteurLetter.A, new FakeSource(C(Door1, 0.8f), C(Door2, 0.7f)));
            Tick(a, 0f);
            Assert.AreEqual(Door1, a.CurrentAction);

            // Someone outside this brain takes door 1 with a higher score.
            Assert.IsTrue(_claims.TryClaim(1, 99, 0.95f));
            Tick(a, 0.25f);

            Assert.AreEqual(Door2, a.CurrentAction);
            Assert.AreEqual(99, _claims.ClaimedBy(1));
            Assert.AreEqual(AgentId(SaboteurLetter.A), _claims.ClaimedBy(2));
        }

        [Test]
        public void ATargetClaimedByAnotherInstanceShowsAsVetoedInTheTrace()
        {
            SaboteurBrain a = Brain(SaboteurLetter.A, new FakeSource(C(Door1, 0.8f)));
            SaboteurBrain b = Brain(SaboteurLetter.B, new FakeSource(C(Door1, 0.8f), C(Door2, 0.7f)));
            Tick(a, 0f);
            Tick(b, 0f);
            Tick(b, SquadCoordinator.StaggerStep);

            UtilityDecisionTrace trace = b.LastDecision;

            // Idle, door 1 (vetoed), door 2.
            Assert.AreEqual(3, trace.CandidateCount);
            CandidateTrace vetoed = trace.GetCandidate(1);
            Assert.AreEqual(Door1, vetoed.Key);
            Assert.AreEqual(0.8f, vetoed.RawScore);
            Assert.AreEqual(0f, vetoed.BaseScore);
            Assert.AreEqual(CandidateRejection.Vetoed, vetoed.Rejection);
            Assert.AreEqual(Door2, trace.Selected);
        }

        [Test]
        public void ThirdAttackerIsSaturatedAndPrefersAnotherAction()
        {
            SaboteurBrain a = Brain(SaboteurLetter.A, new FakeSource(C(Attack, 0.8f)));
            SaboteurBrain b = Brain(SaboteurLetter.B, new FakeSource(C(Attack, 0.8f)));
            SaboteurBrain c = Brain(SaboteurLetter.C, new FakeSource(C(Attack, 0.8f), C(Door1, 0.5f)));

            for (int frame = 0; frame <= 8; frame++)
            {
                float time = frame * Frame;
                Tick(a, time);
                Tick(b, time);
                Tick(c, time);
            }

            // Two others attack, so C's attack is worth 0.8 x 0.45 = 0.36 and the door wins.
            Assert.AreEqual(Attack, a.CurrentAction);
            Assert.AreEqual(Attack, b.CurrentAction);
            Assert.AreEqual(Door1, c.CurrentAction);
            CandidateTrace attack = c.LastDecision.GetCandidate(1);
            Assert.AreEqual(Attack, attack.Key);
            Assert.AreEqual(0.8f, attack.RawScore);
            Assert.AreEqual(0.8f * SquadCoordinator.SaturatedAttackMultiplier, attack.BaseScore, 1e-6f);
        }

        [Test]
        public void SecondAttackerIsNotSaturated()
        {
            SaboteurBrain a = Brain(SaboteurLetter.A, new FakeSource(C(Attack, 0.8f)));
            SaboteurBrain b = Brain(SaboteurLetter.B, new FakeSource(C(Attack, 0.8f)));

            for (int frame = 0; frame <= 8; frame++)
            {
                Tick(a, frame * Frame);
                Tick(b, frame * Frame);
            }

            Assert.AreEqual(Attack, b.CurrentAction);
            Assert.AreEqual(0.8f, b.LastDecision.GetCandidate(1).BaseScore);
        }

        [Test]
        public void BrainsOverOneClaimsInstanceShareOneCoordinatorAndOtherClaimsDoNot()
        {
            var other = new TargetClaims();

            Assert.AreSame(SquadCoordinator.For(_claims), SquadCoordinator.For(_claims));
            Assert.AreNotSame(SquadCoordinator.For(_claims), SquadCoordinator.For(other));
            Assert.Throws<ArgumentNullException>(() => SquadCoordinator.For(null));
        }

        [Test]
        public void SeparateSquadsDoNotShareClaims()
        {
            var otherClaims = new TargetClaims();
            SaboteurBrain a = Brain(SaboteurLetter.A, new FakeSource(C(Door1, 0.8f)));
            SaboteurBrain b = Brain(SaboteurLetter.A, new FakeSource(C(Door1, 0.8f)), otherClaims);

            Tick(a, 0f);
            Tick(b, 0f);

            Assert.AreEqual(Door1, a.CurrentAction);
            Assert.AreEqual(Door1, b.CurrentAction);
        }

        [Test]
        public void TheSameAgentIdTwiceInOneSquadIsRejected()
        {
            Brain(SaboteurLetter.A);

            Assert.Throws<ArgumentException>(() => Brain(SaboteurLetter.A));
        }

        [Test]
        public void ABrainThatFailsValidationDoesNotJoinTheSquad()
        {
            var identity = new SaboteurIdentity(7, SaboteurLetter.A);
            Assert.Throws<ArgumentNullException>(() =>
                new SaboteurBrain(identity, null, new AStarSearch(_grid), _claims, new List<Vector3>()));

            Assert.DoesNotThrow(() =>
                new SaboteurBrain(identity, _grid, new AStarSearch(_grid), _claims, new List<Vector3>()));
        }

        [Test]
        public void WithoutACandidateSourceTheBrainStillOnlyIdles()
        {
            SaboteurBrain a = Brain(SaboteurLetter.A);

            Tick(a, 0f);

            Assert.AreEqual(1, a.LastDecision.CandidateCount);
            Assert.AreEqual(SaboteurActionKind.Idle, a.CurrentAction.Kind);
        }
    }
}
