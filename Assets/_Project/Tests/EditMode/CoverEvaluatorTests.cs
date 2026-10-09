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

        // ---- FindBest: the same answer as scoring everything, for fewer sight tests ----

        // A 60 x 11 room. The player stands in the west; a wall at x = 15 casts a shadow over
        // the cells just east of it, and a second wall at x = 27 casts none.
        static readonly Vector2Int RoomPlayer = new Vector2Int(5, 5);
        static readonly Vector2Int RoomAgent = new Vector2Int(22, 5);

        static GridGraph TwoWallRoom()
        {
            GridGraph grid = OpenGrid(60, 11);
            for (int y = 3; y <= 7; y++)
            {
                grid.SetWalkable(new Vector2Int(15, y), false);
                grid.SetWalkable(new Vector2Int(27, y), false);
            }
            return grid;
        }

        static bool InShadow(Vector2Int cell) => cell.x > 15 && cell.x <= 19 && cell.y >= 3 && cell.y <= 7;

        // What the brain used to do: score every candidate and keep the best.
        static List<float> BestScoresTheSlowWay(CoverEvaluator evaluator, float desiredRange, int count,
            Func<CoverCandidate, bool> allowed = null)
        {
            var all = new List<CoverCandidate>();
            evaluator.FindCandidates(RoomPlayer, all);
            var scores = new List<float>();
            foreach (CoverCandidate candidate in all)
            {
                if (allowed != null && !allowed(candidate))
                    continue;
                float estimate = ToyFactory.AI.Core.Search.BaseCostModel.OctileDistance(RoomAgent, candidate.Cell);
                scores.Add(evaluator.Score(candidate, RoomPlayer, desiredRange, estimate, 60f));
            }
            scores.Sort((a, b) => b.CompareTo(a));
            if (scores.Count > count)
                scores.RemoveRange(count, scores.Count - count);
            return scores;
        }

        [TestCase(10f)]
        [TestCase(7f)]
        [TestCase(4f)]
        public void FindBestGivesTheSameTopScoresAsScoringEveryCandidate(float desiredRange)
        {
            var evaluator = new CoverEvaluator(TwoWallRoom(), new FakeVisibility((cell, _) => InShadow(cell)));
            var best = new List<RankedCover>();

            evaluator.FindBest(RoomPlayer, RoomAgent, desiredRange, 60f, false, null, 3, best);

            List<float> expected = BestScoresTheSlowWay(evaluator, desiredRange, 3);
            Assert.AreEqual(expected.Count, best.Count);
            for (int i = 0; i < expected.Count; i++)
                Assert.AreEqual(expected[i], best[i].Score, Tolerance, "rank " + i);
        }

        [Test]
        public void FindBestStopsBeforeTestingCellsThatCannotWin()
        {
            // Both walls have cells next to them, 16 each. With the ideal range at the first
            // wall, the cells at the second cannot outscore real cover there, so they are never tested.
            var evaluator = new CoverEvaluator(TwoWallRoom(), new FakeVisibility((cell, _) => InShadow(cell)));
            var best = new List<RankedCover>();

            evaluator.FindBest(RoomPlayer, RoomAgent, 5.5f, 60f, false, null, 1, best);

            Assert.AreEqual(1, best.Count);
            Assert.AreEqual(BestScoresTheSlowWay(evaluator, 5.5f, 1)[0], best[0].Score, Tolerance);
            Assert.Less(evaluator.CellsTested, 17, "Only cells at the first wall needed a sight test.");
            Assert.Greater(evaluator.CellsTested, 0);
        }

        [Test]
        public void FindBestWithFullCoverOnlyLeavesOutHalfCover()
        {
            // Half cover everywhere in the shadow: blocked low, visible at chest height.
            var evaluator = new CoverEvaluator(TwoWallRoom(),
                new FakeVisibility((cell, height) => InShadow(cell) && height < 1f));
            var best = new List<RankedCover>();

            evaluator.FindBest(RoomPlayer, RoomAgent, 10f, 60f, true, null, 3, best);
            Assert.AreEqual(0, best.Count);

            evaluator.FindBest(RoomPlayer, RoomAgent, 10f, 60f, false, null, 3, best);
            Assert.AreEqual(3, best.Count);
            Assert.AreEqual(0.5f, best[0].Candidate.Protection, Tolerance);
        }

        [Test]
        public void FindBestLeavesOutCellsThatAreNotAvailable()
        {
            var evaluator = new CoverEvaluator(TwoWallRoom(), new FakeVisibility((cell, _) => InShadow(cell)));
            var best = new List<RankedCover>();
            evaluator.FindBest(RoomPlayer, RoomAgent, 10f, 60f, false, null, 1, best);
            Vector2Int taken = best[0].Candidate.Cell;

            evaluator.FindBest(RoomPlayer, RoomAgent, 10f, 60f, false, cell => cell != taken, 3, best);

            Assert.AreEqual(3, best.Count);
            foreach (RankedCover cover in best)
                Assert.AreNotEqual(taken, cover.Candidate.Cell);
            List<float> expected = BestScoresTheSlowWay(evaluator, 10f, 3, c => c.Cell != taken);
            for (int i = 0; i < 3; i++)
                Assert.AreEqual(expected[i], best[i].Score, Tolerance, "rank " + i);
        }

        [Test]
        public void FindBestIsBestFirstAndClearsTheList()
        {
            var evaluator = new CoverEvaluator(TwoWallRoom(), new FakeVisibility((cell, _) => InShadow(cell)));
            var best = new List<RankedCover> { new RankedCover(default, 99f) };

            evaluator.FindBest(RoomPlayer, RoomAgent, 10f, 60f, false, null, 3, best);

            Assert.AreEqual(3, best.Count);
            Assert.GreaterOrEqual(best[0].Score, best[1].Score);
            Assert.GreaterOrEqual(best[1].Score, best[2].Score);
            Assert.Less(best[0].Score, 99f);
        }

        [Test]
        public void TheCellsNextToObstaclesAreOnlyWorkedOutAgainWhenTheGridChanges()
        {
            GridGraph grid = TwoWallRoom();
            var evaluator = new CoverEvaluator(grid, new FakeVisibility((cell, _) => InShadow(cell)));
            var best = new List<RankedCover>();

            evaluator.FindBest(RoomPlayer, RoomAgent, 10f, 60f, false, null, 3, best);
            evaluator.FindBest(new Vector2Int(6, 5), RoomAgent, 10f, 60f, false, null, 3, best);
            Assert.AreEqual(1, evaluator.EdgeRebuilds, "The player moving does not change the grid.");

            // A new obstacle in the open makes the cells round it possible cover.
            var crate = new Vector2Int(18, 9);
            grid.SetWalkable(crate, false);
            evaluator.FindBest(RoomPlayer, RoomAgent, 10f, 60f, false, null, 3, best);

            Assert.AreEqual(2, evaluator.EdgeRebuilds);
            List<float> expected = BestScoresTheSlowWay(evaluator, 10f, 3);
            for (int i = 0; i < expected.Count; i++)
                Assert.AreEqual(expected[i], best[i].Score, Tolerance, "rank " + i);
        }

        [Test]
        public void FindBestRejectsANonPositiveCount()
        {
            var evaluator = new CoverEvaluator(TwoWallRoom(), new FakeVisibility((_, __) => false));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                evaluator.FindBest(RoomPlayer, RoomAgent, 10f, 60f, false, null, 0, new List<RankedCover>()));
        }

        [Test]
        public void TryEvaluateAgreesWithFindCandidates()
        {
            var evaluator = new CoverEvaluator(TwoWallRoom(), new FakeVisibility((cell, _) => InShadow(cell)));
            var all = new List<CoverCandidate>();
            evaluator.FindCandidates(RoomPlayer, all);
            Assert.Greater(all.Count, 0);

            foreach (CoverCandidate expected in all)
            {
                Assert.IsTrue(evaluator.TryEvaluate(expected.Cell, RoomPlayer, out CoverCandidate found));
                Assert.AreEqual(expected.Protection, found.Protection, Tolerance);
                Assert.AreEqual(expected.CanPeek, found.CanPeek);
            }

            // In the open, away from any wall: not cover.
            Assert.IsFalse(evaluator.TryEvaluate(new Vector2Int(22, 5), RoomPlayer, out _));
            // Next to the second wall, which casts no shadow: not cover either.
            Assert.IsFalse(evaluator.TryEvaluate(new Vector2Int(28, 5), RoomPlayer, out _));
        }
    }
}
