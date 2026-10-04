using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.Tests.EditMode
{
    public class CoverEvaluatorTests
    {
        const float Tolerance = 1e-4f;

        class FakeVisibility : ICoverVisibility
        {
            readonly Func<Vector2Int, float, bool> _blocked;

            public FakeVisibility(Func<Vector2Int, float, bool> blocked) => _blocked = blocked;

            public bool IsBlocked(Vector2Int cell, float height) => _blocked(cell, height);
        }

        static GridGraph OpenGrid(int width, int height)
        {
            return new GridGraph(width, height, Vector3.zero);
        }

        static GridGraph GridWithWall(int width, int height, Vector2Int wall)
        {
            GridGraph grid = OpenGrid(width, height);
            grid.SetWalkable(wall, false);
            return grid;
        }

        static bool TryFind(List<CoverCandidate> results, Vector2Int cell, out CoverCandidate found)
        {
            foreach (CoverCandidate candidate in results)
            {
                if (candidate.Cell == cell)
                {
                    found = candidate;
                    return true;
                }
            }

            found = default;
            return false;
        }

        [Test]
        public void CellBehindAnObstacleIsFullCoverWhenBothHeightsAreBlocked()
        {
            GridGraph grid = GridWithWall(7, 7, new Vector2Int(3, 3));
            var evaluator = new CoverEvaluator(grid, new FakeVisibility((cell, _) => cell.x >= 4));
            var results = new List<CoverCandidate>();

            evaluator.FindCandidates(new Vector2Int(0, 3), results);

            Assert.IsTrue(TryFind(results, new Vector2Int(4, 3), out CoverCandidate cover));
            Assert.AreEqual(1f, cover.Protection, Tolerance);
        }

        [Test]
        public void CellIsHalfCoverWhenOnlyTheLowHeightIsBlocked()
        {
            GridGraph grid = GridWithWall(7, 7, new Vector2Int(3, 3));
            var evaluator = new CoverEvaluator(grid,
                new FakeVisibility((cell, height) => cell.x >= 4 && height < 1f));
            var results = new List<CoverCandidate>();

            evaluator.FindCandidates(new Vector2Int(0, 3), results);

            Assert.IsTrue(TryFind(results, new Vector2Int(4, 3), out CoverCandidate cover));
            Assert.AreEqual(0.5f, cover.Protection, Tolerance);
        }

        [Test]
        public void CellIsDiscardedWhenOnlyTheChestHeightIsBlocked()
        {
            GridGraph grid = GridWithWall(7, 7, new Vector2Int(3, 3));
            var evaluator = new CoverEvaluator(grid,
                new FakeVisibility((cell, height) => cell.x >= 4 && height > 1f));
            var results = new List<CoverCandidate>();

            evaluator.FindCandidates(new Vector2Int(0, 3), results);

            Assert.IsFalse(TryFind(results, new Vector2Int(4, 3), out _));
        }

        [Test]
        public void OpenGridWithNoObstacleHasNoCandidates()
        {
            var evaluator = new CoverEvaluator(OpenGrid(7, 7), new FakeVisibility((_, __) => true));
            var results = new List<CoverCandidate>();

            evaluator.FindCandidates(new Vector2Int(0, 3), results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void ObstacleBeyondFifteenMetresProducesNoCandidates()
        {
            GridGraph grid = GridWithWall(60, 3, new Vector2Int(40, 1));
            var evaluator = new CoverEvaluator(grid, new FakeVisibility((_, __) => true));
            var results = new List<CoverCandidate>();

            evaluator.FindCandidates(new Vector2Int(0, 1), results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void CanPeekWhenANeighbourSeesTheChest()
        {
            GridGraph grid = GridWithWall(7, 7, new Vector2Int(3, 3));
            var openNeighbour = new Vector2Int(4, 2);
            var evaluator = new CoverEvaluator(grid,
                new FakeVisibility((cell, _) => cell.x >= 4 && cell != openNeighbour));
            var results = new List<CoverCandidate>();

            evaluator.FindCandidates(new Vector2Int(0, 3), results);

            Assert.IsTrue(TryFind(results, new Vector2Int(4, 3), out CoverCandidate cover));
            Assert.IsTrue(cover.CanPeek);
        }

        [Test]
        public void CannotPeekWhenEveryNeighbourIsHiddenAtChestHeight()
        {
            GridGraph grid = GridWithWall(7, 7, new Vector2Int(3, 3));
            var evaluator = new CoverEvaluator(grid, new FakeVisibility((_, __) => true));
            var results = new List<CoverCandidate>();

            evaluator.FindCandidates(new Vector2Int(0, 3), results);

            Assert.IsTrue(TryFind(results, new Vector2Int(4, 3), out CoverCandidate cover));
            Assert.IsFalse(cover.CanPeek);
        }

        [Test]
        public void ScoreIsOneForFullCoverAtDesiredRangeWithNoTravelCostAndPeekable()
        {
            var evaluator = new CoverEvaluator(OpenGrid(7, 7), new FakeVisibility((_, __) => false));
            var candidate = new CoverCandidate(new Vector2Int(4, 3), 1f, true);

            float score = evaluator.Score(candidate, new Vector2Int(0, 3), desiredRange: 2f, pathCost: 0f, maxCost: 10f);

            Assert.AreEqual(1f, score, Tolerance);
        }

        [Test]
        public void FullCoverScoresHigherThanHalfCoverAtEqualRange()
        {
            var evaluator = new CoverEvaluator(OpenGrid(7, 7), new FakeVisibility((_, __) => false));
            var full = new CoverCandidate(new Vector2Int(4, 3), 1f, true);
            var half = new CoverCandidate(new Vector2Int(4, 3), 0.5f, true);

            float fullScore = evaluator.Score(full, new Vector2Int(0, 3), 2f, 0f, 10f);
            float halfScore = evaluator.Score(half, new Vector2Int(0, 3), 2f, 0f, 10f);

            Assert.AreEqual(0.2f, fullScore - halfScore, Tolerance);
        }

        [Test]
        public void HigherPathCostLowersTheScore()
        {
            var evaluator = new CoverEvaluator(OpenGrid(7, 7), new FakeVisibility((_, __) => false));
            var candidate = new CoverCandidate(new Vector2Int(4, 3), 1f, true);

            float cheap = evaluator.Score(candidate, new Vector2Int(0, 3), 2f, 0f, 10f);
            float expensive = evaluator.Score(candidate, new Vector2Int(0, 3), 2f, 5f, 10f);

            Assert.AreEqual(0.1f, cheap - expensive, Tolerance);
        }

        [Test]
        public void NullGridIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CoverEvaluator(null, new FakeVisibility((_, __) => false)));
        }

        [Test]
        public void NonPositiveDesiredRangeIsRejected()
        {
            var evaluator = new CoverEvaluator(OpenGrid(7, 7), new FakeVisibility((_, __) => false));
            var candidate = new CoverCandidate(new Vector2Int(4, 3), 1f, true);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                evaluator.Score(candidate, new Vector2Int(0, 3), 0f, 0f, 10f));
        }
    }
}
