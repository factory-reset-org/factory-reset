using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.Tests.EditMode
{
    public class CachedCoverVisibilityTests
    {
        // Counts how often it is asked, and answers from a set the test can change.
        sealed class CountingVisibility : ICoverVisibility
        {
            public readonly HashSet<Vector2Int> Blocked = new HashSet<Vector2Int>();
            public int Asked;

            public bool IsBlocked(Vector2Int cell, float height)
            {
                Asked++;
                return Blocked.Contains(cell);
            }
        }

        static readonly Vector2Int Cell = new Vector2Int(3, 4);

        CountingVisibility _inner;
        CachedCoverVisibility _cached;

        [SetUp]
        public void SetUp()
        {
            _inner = new CountingVisibility();
            _cached = new CachedCoverVisibility(_inner, new GridGraph(10, 10, Vector3.zero));
        }

        [Test]
        public void TheSameQuestionIsOnlyPassedOnOnce()
        {
            _inner.Blocked.Add(Cell);

            for (int i = 0; i < 5; i++)
                Assert.IsTrue(_cached.IsBlocked(Cell, CoverEvaluator.ChestHeight));

            Assert.AreEqual(1, _inner.Asked);
            Assert.AreEqual(5, _cached.Queries);
            Assert.AreEqual(1, _cached.InnerQueries);
        }

        [Test]
        public void TheTwoHeightsAreRememberedSeparately()
        {
            _cached.IsBlocked(Cell, CoverEvaluator.LowCoverHeight);
            _cached.IsBlocked(Cell, CoverEvaluator.ChestHeight);
            _cached.IsBlocked(Cell, CoverEvaluator.LowCoverHeight);
            _cached.IsBlocked(Cell, CoverEvaluator.ChestHeight);

            Assert.AreEqual(2, _inner.Asked);
        }

        [Test]
        public void AnAnswerIsKeptUntilInvalidated()
        {
            Assert.IsFalse(_cached.IsBlocked(Cell, CoverEvaluator.ChestHeight));

            _inner.Blocked.Add(Cell);
            Assert.IsFalse(_cached.IsBlocked(Cell, CoverEvaluator.ChestHeight), "Still the remembered answer.");

            _cached.Invalidate();
            Assert.IsTrue(_cached.IsBlocked(Cell, CoverEvaluator.ChestHeight));
            Assert.AreEqual(2, _inner.Asked);
        }

        [Test]
        public void OtherHeightsAndCellsOffTheGridAreAlwaysPassedOn()
        {
            _cached.IsBlocked(Cell, 0.9f);
            _cached.IsBlocked(Cell, 0.9f);
            _cached.IsBlocked(new Vector2Int(-1, 0), CoverEvaluator.ChestHeight);
            _cached.IsBlocked(new Vector2Int(-1, 0), CoverEvaluator.ChestHeight);

            Assert.AreEqual(4, _inner.Asked);
        }

        [Test]
        public void ACoverSearchAsksAboutEachCellOnlyOnce()
        {
            // The evaluator asks about a cell for its own protection and again for each
            // neighbour's peek check. Through the cache every (cell, height) is asked once.
            var grid = new GridGraph(30, 11, Vector3.zero);
            for (int y = 3; y <= 7; y++)
                grid.SetWalkable(new Vector2Int(15, y), false);
            var inner = new CountingVisibility();
            for (int x = 16; x <= 19; x++)
                for (int y = 3; y <= 7; y++)
                    inner.Blocked.Add(new Vector2Int(x, y));

            var direct = new CoverEvaluator(grid, inner);
            var results = new List<CoverCandidate>();
            direct.FindCandidates(new Vector2Int(5, 5), results);
            int withoutCache = inner.Asked;

            inner.Asked = 0;
            var cached = new CachedCoverVisibility(inner, grid);
            var throughCache = new CoverEvaluator(grid, cached);
            var cachedResults = new List<CoverCandidate>();
            throughCache.FindCandidates(new Vector2Int(5, 5), cachedResults);

            Assert.AreEqual(results.Count, cachedResults.Count, "Same candidates either way.");
            Assert.Less(inner.Asked, withoutCache);
            Assert.AreEqual(cached.InnerQueries, inner.Asked);

            // Asking again changes nothing: every answer is remembered.
            throughCache.FindCandidates(new Vector2Int(5, 5), cachedResults);
            Assert.AreEqual(cached.InnerQueries, inner.Asked);
            Assert.AreEqual(results.Count, cachedResults.Count);
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            var grid = new GridGraph(4, 4, Vector3.zero);
            Assert.Throws<ArgumentNullException>(() => new CachedCoverVisibility(null, grid));
            Assert.Throws<ArgumentNullException>(() => new CachedCoverVisibility(_inner, null));
        }
    }
}
