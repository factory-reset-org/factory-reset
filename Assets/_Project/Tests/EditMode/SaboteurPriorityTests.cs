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
    /// <summary>
    /// Sabotage before combat: a door, trap or battery worth doing is scored in its priority band (doors, then
    /// traps, then batteries) and holds the attack back, while a sabotage too weak to be worth it competes as
    /// before and a badly hurt Saboteur can still break off to flee. The sources are fixed lists, so only the
    /// brain's selection is under test.
    /// </summary>
    public class SaboteurPriorityTests
    {
        sealed class FakeSource : ICandidateSource
        {
            public readonly List<ActionCandidate> Items = new List<ActionCandidate>();

            public FakeSource(params ActionCandidate[] items) { Items.AddRange(items); }

            public void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates) =>
                candidates.AddRange(Items);
        }

        static readonly ActionKey Attack = new ActionKey(SaboteurActionKind.AttackPlayer);
        static readonly ActionKey Flee = new ActionKey(SaboteurActionKind.Flee);
        static readonly ActionKey Door1 = new ActionKey(SaboteurActionKind.CloseDoor, 1);
        static readonly ActionKey Trap11 = new ActionKey(SaboteurActionKind.ArmTrap, 11);
        static readonly ActionKey Battery21 = new ActionKey(SaboteurActionKind.StealBattery, 21);

        // Exactly representable, so decision slots line up with whole frames.
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

        SaboteurBrain Brain(ICandidateSource source, SaboteurLetter letter = SaboteurLetter.A) =>
            new SaboteurBrain(new SaboteurIdentity((int)letter + 1, letter), _grid, new AStarSearch(_grid), _claims,
                new List<Vector3>(), null, 0, source);

        static void Tick(SaboteurBrain brain, float time) =>
            brain.Tick(new AgentContext(new Vector2Int(5, 5), Vector3.zero, Vector3.forward, time, new WorldBlackboard(), default));

        // The pair the latest selection pass chose, read from the trace: the brain may drop a plan it cannot carry
        // out in this empty world, but the choice is what these tests are about.
        static ActionKey Chosen(SaboteurBrain brain) => brain.LastDecision.Selected;

        static CandidateTrace Traced(SaboteurBrain brain, ActionKey key)
        {
            for (int i = 0; i < brain.LastDecision.CandidateCount; i++)
            {
                if (brain.LastDecision.GetCandidate(i).Key.Equals(key))
                    return brain.LastDecision.GetCandidate(i);
            }

            Assert.Fail($"{key.Kind} was not a candidate.");
            return default;
        }

        // ---- The bands

        [Test]
        public void EachSabotageKindHasItsOwnBandAboveAHeldBackAttack()
        {
            float bestHeldBackAttack = 1f * SaboteurBrain.AttackDeferFactor + new SelectorSettings().Momentum;

            Assert.AreEqual(SaboteurBrain.DoorPriorityFloor + SaboteurBrain.PriorityBandWidth,
                SaboteurBrain.PriorityScore(SaboteurActionKind.CloseDoor, 1f), 1e-6f);
            Assert.Greater(SaboteurBrain.PriorityScore(SaboteurActionKind.CloseDoor, SaboteurBrain.SabotageMinScore),
                SaboteurBrain.PriorityScore(SaboteurActionKind.ArmTrap, 1f) - 1e-6f, "A door comes before a trap.");
            Assert.Greater(SaboteurBrain.PriorityScore(SaboteurActionKind.ArmTrap, SaboteurBrain.SabotageMinScore),
                SaboteurBrain.PriorityScore(SaboteurActionKind.StealBattery, 1f) - 1e-6f, "A trap comes before a battery.");
            Assert.Greater(SaboteurBrain.PriorityScore(SaboteurActionKind.StealBattery, SaboteurBrain.SabotageMinScore),
                bestHeldBackAttack, "Even the weakest battery worth taking beats an attack under way.");
        }

        [Test]
        public void TheTopOfTheDoorBandWithMomentumStaysUnderTheEmergencyThreshold()
        {
            var settings = new SelectorSettings();
            float doorUnderWay = SaboteurBrain.PriorityScore(SaboteurActionKind.CloseDoor, 1f) + settings.Momentum;

            Assert.Less(doorUnderWay, settings.EmergencyThreshold, "A badly hurt Saboteur can still break off to flee.");
        }

        [Test]
        public void TheBandKeepsTheOrderOfTargetsOfOneKind()
        {
            Assert.Greater(SaboteurBrain.PriorityScore(SaboteurActionKind.CloseDoor, 0.9f),
                SaboteurBrain.PriorityScore(SaboteurActionKind.CloseDoor, 0.4f));
            Assert.AreEqual(0.3f, SaboteurBrain.PriorityScore(SaboteurActionKind.AttackPlayer, 0.3f), "Not a sabotage: unchanged.");
            Assert.AreEqual(0f, SaboteurBrain.PriorityScore(SaboteurActionKind.CloseDoor, 0f), "A veto stays a veto.");
        }

        // ---- The brain's choice

        [Test]
        public void ADoorWorthClosingComesBeforeAnAttackAtMeleeRange()
        {
            SaboteurBrain brain = Brain(new FakeSource(C(Attack, 0.88f), C(Door1, 0.3f)));

            Tick(brain, 0f);

            Assert.AreEqual(Door1, Chosen(brain));
            Assert.AreEqual(0.88f * SaboteurBrain.AttackDeferFactor, Traced(brain, Attack).BaseScore, 1e-6f, "The attack is held back.");
            Assert.AreEqual(SaboteurBrain.PriorityScore(SaboteurActionKind.CloseDoor, 0.3f), Traced(brain, Door1).BaseScore, 1e-6f);
        }

        [Test]
        public void ATrapOrABatteryAlsoComesBeforeTheAttack()
        {
            SaboteurBrain trap = Brain(new FakeSource(C(Attack, 0.88f), C(Trap11, 0.3f)));
            SaboteurBrain battery = Brain(new FakeSource(C(Attack, 0.88f), C(Battery21, 0.3f)), SaboteurLetter.B);

            Tick(trap, 0f);
            Tick(battery, 0f);
            Tick(battery, SquadCoordinator.StaggerStep);

            Assert.AreEqual(Trap11, Chosen(trap));
            Assert.AreEqual(Battery21, Chosen(battery));
        }

        [Test]
        public void ADoorComesBeforeATrapAndATrapBeforeABattery()
        {
            SaboteurBrain all = Brain(new FakeSource(C(Battery21, 1f), C(Trap11, 1f), C(Door1, 0.2f)));
            SaboteurBrain noDoor = Brain(new FakeSource(C(Battery21, 1f), C(Trap11, 0.2f)), SaboteurLetter.B);

            Tick(all, 0f);
            Tick(noDoor, 0f);
            Tick(noDoor, SquadCoordinator.StaggerStep);

            Assert.AreEqual(Door1, Chosen(all), "A weak door still beats the best trap and battery.");
            Assert.AreEqual(Trap11, Chosen(noDoor), "A weak trap still beats the best battery.");
        }

        [Test]
        public void ASabotageTooWeakToBeWorthItNeitherHoldsTheAttackBackNorGetsABand()
        {
            float weak = SaboteurBrain.SabotageMinScore - 0.01f;
            SaboteurBrain brain = Brain(new FakeSource(C(Attack, 0.6f), C(Door1, weak)));

            Tick(brain, 0f);

            Assert.AreEqual(Attack, Chosen(brain));
            Assert.AreEqual(0.6f, Traced(brain, Attack).BaseScore, 1e-6f);
            Assert.AreEqual(weak, Traced(brain, Door1).BaseScore, 1e-6f);
        }

        [Test]
        public void WithNoSabotageOnOfferTheAttackIsNotHeldBack()
        {
            SaboteurBrain brain = Brain(new FakeSource(C(Attack, 0.88f)));

            Tick(brain, 0f);

            Assert.AreEqual(Attack, Chosen(brain));
            Assert.AreEqual(0.88f, Traced(brain, Attack).BaseScore, 1e-6f);
        }

        [Test]
        public void AnAttackUnderWayGivesWayToADoorOnceItsCommitmentIsOver()
        {
            var source = new FakeSource(C(Attack, 0.88f));
            SaboteurBrain brain = Brain(source);
            Tick(brain, 0f);
            Assert.AreEqual(Attack, Chosen(brain));

            source.Items.Add(C(Door1, 0.3f));
            float switchedAt = -1f;
            for (float t = Frame; t <= 3f && switchedAt < 0f; t += Frame)
            {
                Tick(brain, t);
                if (Chosen(brain).Equals(Door1))
                    switchedAt = t;
            }

            Assert.Greater(switchedAt, 0f, "The door was taken.");
            Assert.LessOrEqual(switchedAt, new SelectorSettings().CommitmentSeconds + SaboteurBrain.DecisionInterval + Frame,
                "No later than the first decision after the attack's commitment.");
        }

        [Test]
        public void ABadlyHurtSaboteurStillBreaksOffADoorToFlee()
        {
            var source = new FakeSource(C(Door1, 1f));
            SaboteurBrain brain = Brain(source);
            Tick(brain, 0f);
            Assert.AreEqual(Door1, Chosen(brain));

            source.Items.Add(C(Flee, 0.95f));
            Tick(brain, SaboteurBrain.DecisionInterval);

            Assert.AreEqual(Flee, Chosen(brain), "An emergency flight beats a door under way, even inside its commitment.");
        }

        [Test]
        public void ASecondSaboteurWhoseDoorIsClaimedAttacksInstead()
        {
            // The squad still fights: the "not claimed" veto leaves B no sabotage, so its attack is not held back.
            SaboteurBrain a = Brain(new FakeSource(C(Door1, 0.5f), C(Attack, 0.5f)));
            SaboteurBrain b = Brain(new FakeSource(C(Door1, 0.5f), C(Attack, 0.5f)), SaboteurLetter.B);

            Tick(a, 0f);
            Tick(b, 0f);
            Tick(b, SquadCoordinator.StaggerStep);

            Assert.AreEqual(Door1, Chosen(a));
            Assert.AreEqual(Attack, Chosen(b));
            Assert.AreEqual(0.5f, Traced(b, Attack).BaseScore, 1e-6f);
        }
    }
}
