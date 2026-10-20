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
    public class UtilityDecisionTraceTests
    {
        static readonly ActionKey DoorA = new ActionKey(SaboteurActionKind.CloseDoor, 1);
        static readonly ActionKey Battery = new ActionKey(SaboteurActionKind.StealBattery, 7);
        static readonly ActionKey Attack = new ActionKey(SaboteurActionKind.AttackPlayer);
        static readonly ActionKey Idle = new ActionKey(SaboteurActionKind.Idle);

        readonly List<ActionCandidate> _candidates = new List<ActionCandidate>();
        readonly UtilityDecisionTrace _trace = new UtilityDecisionTrace();

        [SetUp]
        public void SetUp() => _candidates.Clear();

        // Scores one action through UtilityAction and records it the way the brain does.
        ActionScore AddScored(ActionKey key, UtilityAction action, params float[] inputs)
        {
            var scores = new float[inputs.Length];
            ActionScore score = action.Evaluate(inputs, scores);
            _candidates.Add(new ActionCandidate(key, score.BaseScore));
            _trace.AddScored(key, action, inputs, scores, score);
            return score;
        }

        void AddConstant(ActionKey key, float score)
        {
            _candidates.Add(new ActionCandidate(key, score));
            _trace.AddConstant(key, new ActionScore(score, score));
        }

        static UtilityAction TwoInputs(SaboteurActionKind kind) =>
            new UtilityAction(kind, Consideration.Direct("distance"), Consideration.Direct("detour"));

        [Test]
        public void RecordsRawBaseAndConsiderationValuesExactlyAsUtilityActionComputedThem()
        {
            UtilityAction action = TwoInputs(SaboteurActionKind.CloseDoor);
            var selector = new ActionSelector();
            _trace.Begin(2f);

            ActionScore expected = AddScored(DoorA, action, 0.9f, 0.7f);
            selector.Select(_candidates, 2f, _trace);

            CandidateTrace c = _trace.GetCandidate(0);
            Assert.AreEqual(expected.Raw, c.RawScore);
            Assert.AreEqual(expected.BaseScore, c.BaseScore);
            Assert.AreEqual(2, c.ConsiderationCount);
            ConsiderationTrace first = _trace.GetConsideration(0, 0);
            ConsiderationTrace second = _trace.GetConsideration(0, 1);
            Assert.AreEqual("distance", first.Name);
            Assert.AreEqual(0.9f, first.Input);
            Assert.AreEqual(0.9f, first.Score);
            Assert.AreEqual("detour", second.Name);
            Assert.AreEqual(0.7f, second.Input);
            Assert.AreEqual(0.7f, second.Score);
        }

        [Test]
        public void RecordsTheSelectedPairAndItsRankingFromTheSelectorResult()
        {
            var selector = new ActionSelector();
            _trace.Begin(1f);
            AddConstant(Idle, 0.1f);
            AddScored(DoorA, TwoInputs(SaboteurActionKind.CloseDoor), 1f, 0.8f);

            SelectionResult result = selector.Select(_candidates, 1f, _trace);

            Assert.IsTrue(_trace.HasDecision);
            Assert.IsTrue(_trace.HasSelection);
            Assert.AreEqual(result.Key, _trace.Selected);
            Assert.AreEqual(result.Switched, _trace.Switched);
            Assert.AreEqual(1f, _trace.Time);
            Assert.AreEqual(CandidateRejection.None, _trace.GetCandidate(1).Rejection);
            Assert.AreEqual(result.BaseScore, _trace.GetCandidate(1).BaseScore);
            Assert.AreEqual(result.RankingScore, _trace.GetCandidate(1).RankingScore);
        }

        [Test]
        public void ALowerCandidateIsOutranked()
        {
            var selector = new ActionSelector();
            _trace.Begin(0f);
            AddConstant(Idle, 0.1f);
            AddConstant(Battery, 0.4f);
            AddConstant(DoorA, 0.6f);

            selector.Select(_candidates, 0f, _trace);

            Assert.AreEqual(CandidateRejection.Outranked, _trace.GetCandidate(0).Rejection);
            Assert.AreEqual(CandidateRejection.Outranked, _trace.GetCandidate(1).Rejection);
            Assert.AreEqual(CandidateRejection.None, _trace.GetCandidate(2).Rejection);
        }

        [Test]
        public void AZeroConsiderationIsRecordedAsAVeto()
        {
            var selector = new ActionSelector();
            _trace.Begin(0f);
            AddConstant(Idle, 0.1f);
            AddScored(DoorA, TwoInputs(SaboteurActionKind.CloseDoor), 0f, 0.9f);

            selector.Select(_candidates, 0f, _trace);

            CandidateTrace vetoed = _trace.GetCandidate(1);
            Assert.AreEqual(CandidateRejection.Vetoed, vetoed.Rejection);
            Assert.AreEqual(0f, vetoed.BaseScore);
            Assert.AreEqual(0f, _trace.GetConsideration(1, 0).Score);
            Assert.AreEqual(Idle, _trace.Selected);
        }

        [Test]
        public void ACoolingDownPairIsRecordedAsOnCooldown()
        {
            var selector = new ActionSelector(new SelectorSettings(commitmentSeconds: 0f));
            selector.NotifySuccess(DoorA, 0f);
            _trace.Begin(1f);
            AddConstant(Idle, 0.1f);
            AddConstant(DoorA, 0.8f);

            selector.Select(_candidates, 1f, _trace);

            Assert.AreEqual(CandidateRejection.OnCooldown, _trace.GetCandidate(1).Rejection);
            Assert.AreEqual(Idle, _trace.Selected);
        }

        [Test]
        public void ACommitmentHoldsAWeakerPairAndAnEmergencyBreaksIt()
        {
            var selector = new ActionSelector(new SelectorSettings(commitmentSeconds: 5f));
            selector.Select(new List<ActionCandidate> { new ActionCandidate(DoorA, 0.5f) }, 0f);

            _trace.Begin(1f);
            AddConstant(DoorA, 0.5f);
            AddConstant(Battery, 0.7f);
            selector.Select(_candidates, 1f, _trace);

            Assert.IsTrue(_trace.CommitmentActive);
            Assert.AreEqual(DoorA, _trace.Selected);
            Assert.IsTrue(_trace.GetCandidate(0).WasCurrent);
            Assert.AreEqual(CandidateRejection.CommitmentHeld, _trace.GetCandidate(1).Rejection);

            _candidates.Clear();
            _trace.Begin(2f);
            AddConstant(DoorA, 0.5f);
            AddConstant(Attack, 0.95f);
            selector.Select(_candidates, 2f, _trace);

            Assert.AreEqual(Attack, _trace.Selected);
            Assert.IsTrue(_trace.Switched);
            Assert.AreEqual(CandidateRejection.Outranked, _trace.GetCandidate(0).Rejection);
        }

        [Test]
        public void TheCurrentPairRecordsItsMomentumInTheRanking()
        {
            var selector = new ActionSelector(new SelectorSettings(momentum: 0.15f, commitmentSeconds: 0f));
            selector.Select(new List<ActionCandidate> { new ActionCandidate(DoorA, 0.5f) }, 0f);

            _trace.Begin(1f);
            AddConstant(DoorA, 0.5f);
            AddConstant(Battery, 0.6f);
            selector.Select(_candidates, 1f, _trace);

            Assert.AreEqual(0.5f, _trace.GetCandidate(0).BaseScore);
            Assert.AreEqual(0.65f, _trace.GetCandidate(0).RankingScore, 1e-6f);
            Assert.AreEqual(0.6f, _trace.GetCandidate(1).RankingScore);
            Assert.AreEqual(DoorA, _trace.Selected);
        }

        [Test]
        public void NoUsableCandidateRecordsNoSelection()
        {
            var selector = new ActionSelector();
            _trace.Begin(0f);
            AddScored(DoorA, TwoInputs(SaboteurActionKind.CloseDoor), 0f, 0f);

            selector.Select(_candidates, 0f, _trace);

            Assert.IsTrue(_trace.HasDecision);
            Assert.IsFalse(_trace.HasSelection);
            Assert.AreEqual(CandidateRejection.Vetoed, _trace.GetCandidate(0).Rejection);
        }

        [Test]
        public void BeginClearsThePreviousDecision()
        {
            var selector = new ActionSelector();
            _trace.Begin(0f);
            AddConstant(Idle, 0.1f);
            selector.Select(_candidates, 0f, _trace);

            _trace.Begin(1f);

            Assert.IsFalse(_trace.HasDecision);
            Assert.AreEqual(0, _trace.CandidateCount);
            Assert.AreEqual(1f, _trace.Time);
        }

        [Test]
        public void OverflowKeepsWhatFitsAndSetsTruncated()
        {
            var small = new UtilityDecisionTrace(maxCandidates: 1, maxConsiderations: 1);
            UtilityAction action = TwoInputs(SaboteurActionKind.CloseDoor);
            small.Begin(0f);

            var inputs = new[] { 0.5f, 0.5f };
            var scores = new float[2];
            ActionScore score = action.Evaluate(inputs, scores);
            small.AddScored(DoorA, action, inputs, scores, score);
            small.AddConstant(Idle, new ActionScore(0.1f, 0.1f));

            Assert.IsTrue(small.Truncated);
            Assert.AreEqual(1, small.CandidateCount);
            Assert.AreEqual(1, small.GetCandidate(0).ConsiderationCount);
        }

        [Test]
        public void RecordingAndSelectingDoNotAllocate()
        {
            var selector = new ActionSelector();
            UtilityAction action = TwoInputs(SaboteurActionKind.CloseDoor);
            var inputs = new[] { 0.9f, 0.7f };
            var scores = new float[2];

            void Pass(float now)
            {
                _candidates.Clear();
                _trace.Begin(now);
                _candidates.Add(new ActionCandidate(Idle, 0.1f));
                _trace.AddConstant(Idle, new ActionScore(0.1f, 0.1f));
                ActionScore s = action.Evaluate(inputs, scores);
                _candidates.Add(new ActionCandidate(DoorA, s.BaseScore));
                _trace.AddScored(DoorA, action, inputs, scores, s);
                selector.Select(_candidates, now, _trace);
            }

            for (int i = 0; i < 10; i++)
                Pass(i);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 10; i < 1010; i++)
                Pass(i);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(0, after - before);
        }

        [Test]
        public void TheBrainRecordsEachDecisionWithIdleAsTheOnlyCandidate()
        {
            var grid = new GridGraph(10, 10, Vector3.zero);
            var brain = new SaboteurBrain(new SaboteurIdentity(1, SaboteurLetter.A), grid, new AStarSearch(grid),
                new TargetClaims(), new List<Vector3> { grid.CellToWorld(new Vector2Int(5, 5)) });
            var cell = new Vector2Int(0, 0);

            brain.Tick(new AgentContext(cell, grid.CellToWorld(cell), Vector3.forward, 3f, new WorldBlackboard(), default));

            UtilityDecisionTrace decision = brain.LastDecision;
            Assert.IsTrue(decision.HasDecision);
            Assert.AreEqual(3f, decision.Time);
            Assert.AreEqual(1, decision.CandidateCount);
            Assert.AreEqual(Idle, decision.Selected);
            Assert.AreEqual(SaboteurBrain.IdleScore, decision.GetCandidate(0).BaseScore);
            Assert.AreEqual(CandidateRejection.None, decision.GetCandidate(0).Rejection);
            Assert.AreEqual(brain.CurrentAction, decision.Selected);
        }
    }
}
