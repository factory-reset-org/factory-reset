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
    /// <summary>The text the Saboteur debug overlay shows for a decision.</summary>
    public class UtilityDecisionTextTests
    {
        sealed class FakeSight : IPlayerSight
        {
            public bool CanSeeCell(Vector2Int cell) => true;
        }

        static SaboteurBrain HurtBrain(GridGraph grid)
        {
            var source = new CompositeCandidateSource(new AttackPlayerSource(new FakeSight()), new FleeSource(new FakeSight()));
            var brain = new SaboteurBrain(new SaboteurIdentity(1, SaboteurLetter.A), grid, new AStarSearch(grid), new TargetClaims(),
                new List<Vector3> { grid.CellToWorld(new Vector2Int(10, 10)) }, null, 0, source);
            brain.OnHealthChanged(1, 4);
            return brain;
        }

        static AgentContext FacedByPlayer(GridGraph grid)
        {
            var home = new Vector2Int(30, 30);
            Vector3 position = grid.CellToWorld(home);
            var world = new WorldBlackboard();
            world.SetPlayer(new PlayerSnapshot(true, home, position + new Vector3(3f, 0f, 0f), Vector3.zero, Vector3.left, 5f, true, 1f, 1f, false, 0f, 0f));
            return new AgentContext(home, position, Vector3.forward, 0f, world, default);
        }

        [Test]
        public void NoDecisionYetSaysSo()
        {
            Assert.AreEqual("no decision yet", UtilityDecisionText.Format(new UtilityDecisionTrace()));
        }

        [Test]
        public void NullTraceIsRejected()
        {
            Assert.Throws<System.ArgumentNullException>(() => UtilityDecisionText.Format(null));
        }

        [Test]
        public void EveryRejectionHasAWordExceptTheSelectedOne()
        {
            Assert.AreEqual(string.Empty, UtilityDecisionText.ReasonText(CandidateRejection.None));
            foreach (CandidateRejection rejection in System.Enum.GetValues(typeof(CandidateRejection)))
            {
                if (rejection != CandidateRejection.None)
                    Assert.IsNotEmpty(UtilityDecisionText.ReasonText(rejection), rejection.ToString());
            }
        }

        [Test]
        public void ListsTheSelectedFleeAndTheActionsThatLost()
        {
            var grid = new GridGraph(60, 60, Vector3.zero);
            SaboteurBrain brain = HurtBrain(grid);
            brain.Tick(FacedByPlayer(grid));

            string text = UtilityDecisionText.Format(brain.LastDecision);
            string[] lines = text.Split('\n');

            StringAssert.StartsWith("t 0.0", lines[0]);
            Assert.IsTrue(System.Array.Exists(lines, l => l.StartsWith("> Flee ")), text);
            Assert.IsTrue(System.Array.Exists(lines, l => l.StartsWith("- Idle 0.10 (raw 0.10) outranked")), text);
            Assert.IsTrue(System.Array.Exists(lines, l => l.StartsWith("- AttackPlayer ") && l.EndsWith("outranked")), text);
        }

        [Test]
        public void FleeLineCarriesBaseAndRawScores()
        {
            var grid = new GridGraph(60, 60, Vector3.zero);
            SaboteurBrain brain = HurtBrain(grid);
            brain.Tick(FacedByPlayer(grid));

            string flee = System.Array.Find(UtilityDecisionText.Format(brain.LastDecision).Split('\n'), l => l.StartsWith("> Flee "));

            // Player 3 m away, hurt to 0.25, keycard carrier: capped at 1 after the 1.25 bonus.
            Assert.AreEqual("> Flee 1.00 (raw 1.00)", flee);
        }

        [Test]
        public void TraceBeyondItsCapacityIsFlaggedInTheText()
        {
            var trace = new UtilityDecisionTrace(1, 4);
            var idle = new ActionKey(SaboteurActionKind.Idle);
            var flee = new ActionKey(SaboteurActionKind.Flee);
            trace.Begin(2f);
            trace.AddConstant(idle, new ActionScore(0.1f, 0.1f));
            trace.AddConstant(flee, new ActionScore(0.5f, 0.5f));

            new ActionSelector().Select(new List<ActionCandidate> { new ActionCandidate(idle, 0.1f), new ActionCandidate(flee, 0.5f) }, 2f, trace);

            StringAssert.Contains("more candidates than the trace holds", UtilityDecisionText.Format(trace));
        }
    }
}
