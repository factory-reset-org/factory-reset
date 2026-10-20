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
    public class SaboteurDropTests
    {
        const int KeycardId = 42;

        GridGraph _grid;
        TargetClaims _claims;
        readonly List<ItemDrop> _drops = new List<ItemDrop>(4);

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(10, 10, Vector3.zero);
            _claims = new TargetClaims();
            _drops.Clear();
        }

        SaboteurBrain Brain(SaboteurLetter letter, params Vector2Int[] patrolCells)
        {
            var points = new List<Vector3>();
            foreach (Vector2Int cell in patrolCells)
                points.Add(_grid.CellToWorld(cell));
            return new SaboteurBrain(new SaboteurIdentity(1, letter), _grid, new AStarSearch(_grid), _claims,
                points, null, KeycardId);
        }

        // The controller passes the cell under the body, which may lie outside the grid.
        static AgentContext At(Vector2Int cell, float time = 0f) =>
            new AgentContext(cell, Vector3.zero, Vector3.forward, time, new WorldBlackboard(), default);

        static void TickAndDestroy(SaboteurBrain brain, Vector2Int cell)
        {
            brain.Tick(At(cell));
            brain.OnDestroyed();
        }

        [Test]
        public void KeycardLandsOnTheCellWhereSaboteurAFell()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            TickAndDestroy(brain, new Vector2Int(3, 3));

            int count = brain.GetDrops(_drops);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, _drops.Count);
            Assert.AreEqual(ItemDropKind.Keycard, _drops[0].Kind);
            Assert.AreEqual(KeycardId, _drops[0].ItemId);
            Assert.AreEqual(new Vector2Int(3, 3), _drops[0].Cell);
        }

        [Test]
        public void ABlockedCellMovesTheKeycardToTheNearestTraversableCell()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            var fell = new Vector2Int(3, 3);
            brain.Tick(At(fell));
            _grid.AddBlocker(fell);

            brain.OnDestroyed();
            brain.GetDrops(_drops);

            Assert.AreNotEqual(fell, _drops[0].Cell);
            Assert.IsTrue(_grid.IsTraversable(_drops[0].Cell));
            Assert.IsTrue(_grid.TryFindNearestTraversable(fell, SaboteurBrain.DropSearchRadius, out Vector2Int expected));
            Assert.AreEqual(expected, _drops[0].Cell);
        }

        [Test]
        public void ABoxNextToTheSaboteurNeverHoldsTheKeycard()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            var fell = new Vector2Int(4, 4);
            brain.Tick(At(fell));
            for (int x = 3; x <= 5; x++)
            for (int y = 3; y <= 5; y++)
                _grid.AddBlocker(new Vector2Int(x, y));

            brain.OnDestroyed();
            brain.GetDrops(_drops);

            Assert.IsTrue(_grid.IsTraversable(_drops[0].Cell));
            Assert.IsTrue(_grid.TryFindNearestTraversable(fell, SaboteurBrain.DropSearchRadius, out Vector2Int expected));
            Assert.AreEqual(expected, _drops[0].Cell);
        }

        [Test]
        public void WithNothingWalkableNearbyTheSearchWidensSoTheKeycardIsNotLost()
        {
            _grid = new GridGraph(30, 30, Vector3.zero);
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            var open = new Vector2Int(28, 28);
            brain.Tick(At(new Vector2Int(2, 2)));
            for (int x = 0; x < 30; x++)
            for (int y = 0; y < 30; y++)
            {
                var cell = new Vector2Int(x, y);
                if (cell != open)
                    _grid.SetWalkable(cell, false);
            }

            brain.OnDestroyed();
            brain.GetDrops(_drops);

            Assert.AreEqual(1, _drops.Count);
            Assert.AreEqual(open, _drops[0].Cell);
        }

        [Test]
        public void WithNoTraversableCellAtAllTheKeycardIsStillReportedOnTheGrid()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            brain.Tick(At(new Vector2Int(3, 3)));
            for (int x = 0; x < 10; x++)
            for (int y = 0; y < 10; y++)
                _grid.SetWalkable(new Vector2Int(x, y), false);

            brain.OnDestroyed();

            Assert.AreEqual(1, brain.GetDrops(_drops));
            Assert.AreEqual(new Vector2Int(3, 3), _drops[0].Cell);
        }

        [Test]
        public void ACellOutsideTheGridIsSnappedOntoIt()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            TickAndDestroy(brain, new Vector2Int(-3, 4));

            brain.GetDrops(_drops);

            Assert.IsTrue(_grid.IsTraversable(_drops[0].Cell));
            Assert.AreEqual(new Vector2Int(0, 4), _drops[0].Cell);
        }

        [Test]
        public void DestroyedBeforeItsFirstTickDropsOnItsFirstPatrolCell()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A, new Vector2Int(6, 2), new Vector2Int(1, 1));
            brain.OnDestroyed();

            brain.GetDrops(_drops);

            Assert.AreEqual(new Vector2Int(6, 2), _drops[0].Cell);
        }

        [Test]
        public void DestroyedBeforeItsFirstTickWithNoPatrolDropsNearTheGridCentre()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            brain.OnDestroyed();

            brain.GetDrops(_drops);

            Assert.AreEqual(new Vector2Int(5, 5), _drops[0].Cell);
        }

        [TestCase(SaboteurLetter.B)]
        [TestCase(SaboteurLetter.C)]
        [TestCase(SaboteurLetter.D)]
        public void SaboteursBToDDropNothing(SaboteurLetter letter)
        {
            SaboteurBrain brain = Brain(letter);
            TickAndDestroy(brain, new Vector2Int(3, 3));

            Assert.AreEqual(0, brain.GetDrops(_drops));
            Assert.AreEqual(0, _drops.Count);
        }

        [Test]
        public void NothingIsDroppedBeforeDestruction()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            brain.Tick(At(new Vector2Int(3, 3)));

            Assert.AreEqual(0, brain.GetDrops(_drops));
            Assert.AreEqual(0, _drops.Count);
        }

        [Test]
        public void DropsAreAppendedToTheBufferWithoutClearingIt()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            TickAndDestroy(brain, new Vector2Int(3, 3));
            _drops.Add(new ItemDrop(ItemDropKind.Battery, 9, new Vector2Int(1, 1)));

            int count = brain.GetDrops(_drops);

            Assert.AreEqual(1, count);
            Assert.AreEqual(2, _drops.Count);
            Assert.AreEqual(ItemDropKind.Battery, _drops[0].Kind);
            Assert.AreEqual(ItemDropKind.Keycard, _drops[1].Kind);
        }

        [Test]
        public void TheDropIsFixedAtDestructionAndRepeatedQueriesAgree()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            TickAndDestroy(brain, new Vector2Int(3, 3));
            brain.GetDrops(_drops);

            _grid.AddBlocker(new Vector2Int(3, 3));
            brain.OnDestroyed();
            var again = new List<ItemDrop>();
            brain.GetDrops(again);

            Assert.AreEqual(1, again.Count);
            Assert.AreEqual(_drops[0].Cell, again[0].Cell);
        }

        [Test]
        public void ANullBufferIsRejected()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);

            Assert.Throws<ArgumentNullException>(() => brain.GetDrops(null));
        }

        [Test]
        public void QueryingFillsTheCallersBufferWithoutAllocating()
        {
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            TickAndDestroy(brain, new Vector2Int(3, 3));
            for (int i = 0; i < 10; i++)
            {
                _drops.Clear();
                brain.GetDrops(_drops);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                _drops.Clear();
                brain.GetDrops(_drops);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(0, after - before);
        }
    }
}
