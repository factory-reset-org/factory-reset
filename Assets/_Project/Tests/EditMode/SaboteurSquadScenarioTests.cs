using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using Debug = UnityEngine.Debug;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The squad scenario of the IS evidence: four Saboteurs, two doors and a player crossing both, with
    /// the real <see cref="CloseDoorSource"/>, <see cref="DetourCache"/>, <see cref="TargetClaims"/> and A*,
    /// on a grid shaped like <c>Test_SquadClaims.unity</c>. The assertions hold what must always be true
    /// (different doors, no shared claims, equal searches for one and four Saboteurs); the timings are
    /// printed to the console for <c>Docs/AIPerformanceLog.md</c> because they depend on the machine.
    /// </summary>
    /// <remarks>
    /// The grid is 60 x 40 cells (30 x 20 m). Two walls, at x = 20-21 and x = 40-41, each with a door four
    /// cells tall at y = 18-21 and a gap round the end (door 1's at the north, y = 36-39; door 2's at the
    /// south, y = 0-3). The player at (4, 20) walks east to the objective at (56, 20), straight through
    /// both doors, so closing either one lengthens the route without locking the player out.
    /// </remarks>
    public class SaboteurSquadScenarioTests
    {
        sealed class CountingPathfinder : IPathfinder
        {
            readonly IPathfinder _inner;
            public int Calls;

            public CountingPathfinder(IPathfinder inner) { _inner = inner; }

            public PathResult FindPath(Vector2Int start, Vector2Int goal, ICostModel cost)
            {
                Calls++;
                return _inner.FindPath(start, goal, cost);
            }
        }

        const int Width = 60;
        const int Height = 40;
        const int DoorOne = 1;
        const int DoorTwo = 2;
        const float Frame = 1f / 64f;

        static readonly Vector2Int PlayerCell = new Vector2Int(4, 20);
        static readonly Vector2Int ObjectiveCell = new Vector2Int(56, 20);

        // Where each Saboteur stands: A and B each beside a door, C and D far from both.
        static readonly Vector2Int[] StartCells =
        {
            new Vector2Int(14, 20), new Vector2Int(34, 20), new Vector2Int(30, 6), new Vector2Int(30, 34)
        };

        GridGraph _grid;
        CountingPathfinder _pathfinder;      // the detour cache's searches
        CountingPathfinder _walkPathfinder;  // the routes the Saboteurs plan to the door they chose
        WorldBlackboard _world;
        TargetClaims _claims;
        DetourCache _cache;
        SaboteurBrain[] _brains;

        // The scenario's grid. The walls stand a third and two thirds of the way across, the doorways are centred
        // on the middle row, and each wall has a gap at one end (door 1's wall at the north, door 2's at the south).
        static GridGraph BuildLevel(int width = Width, int height = Height)
        {
            var grid = new GridGraph(width, height, Vector3.zero);
            int middle = height / 2;
            using (GridGraph.Batch batch = grid.BeginBatch())
            {
                foreach ((int wallX, int doorId, int gapFrom) in new[] { (width / 3, DoorOne, height - 4), (width * 2 / 3, DoorTwo, 0) })
                {
                    for (int x = wallX; x <= wallX + 1; x++)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            var cell = new Vector2Int(x, y);
                            if (y >= middle - 2 && y <= middle + 1)
                                batch.SetDoor(cell, doorId, false);
                            else if (y < gapFrom || y > gapFrom + 3)
                                batch.SetWalkable(cell, false);
                        }
                    }
                }

                batch.Commit();
            }

            return grid;
        }

        [SetUp]
        public void SetUp()
        {
            ResetWorld();
            _brains = MakeSquad(4);
        }

        void ResetWorld()
        {
            _grid = BuildLevel();
            _pathfinder = new CountingPathfinder(new AStarSearch(_grid));
            _walkPathfinder = new CountingPathfinder(new AStarSearch(_grid));
            _claims = new TargetClaims();
            _cache = new DetourCache(_grid, _pathfinder);
            _world = new WorldBlackboard();
            _world.SetPlayer(new PlayerSnapshot(true, PlayerCell, _grid.CellToWorld(PlayerCell), Vector3.zero, Vector3.right,
                5f, true, 1f, 1f, false, 0f, 0f));
            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, ObjectiveCell, ObjectiveTargetKind.Task) });
        }

        SaboteurBrain[] MakeSquad(int count)
        {
            var source = new CloseDoorSource(_cache);
            var brains = new SaboteurBrain[count];
            for (int i = 0; i < count; i++)
            {
                var letter = (SaboteurLetter)i;
                brains[i] = new SaboteurBrain(new SaboteurIdentity(i + 1, letter), _grid, _walkPathfinder, _claims,
                    new List<Vector3>(), null, 0, source, _cache);
            }

            return brains;
        }

        // Every Saboteur ticks every frame at its own cell, as the controller does, for the given game time.
        void Run(SaboteurBrain[] brains, float from, float seconds)
        {
            int frames = Mathf.RoundToInt(seconds / Frame);
            for (int f = 0; f < frames; f++)
            {
                float time = from + f * Frame;
                for (int i = 0; i < brains.Length; i++)
                {
                    Vector2Int cell = StartCells[i];
                    brains[i].Tick(new AgentContext(cell, _grid.CellToWorld(cell), Vector3.right, time, _world, default));
                }
            }
        }

        static string Describe(SaboteurBrain brain) =>
            $"{brain.Identity.Letter}: {brain.CurrentAction}\n{UtilityDecisionText.Format(brain.LastDecision)}";

        // ---- The scenario

        [Test]
        public void TheLevelOffersBothDoorsAsUsefulDetours()
        {
            _cache.Refresh(_world, 0f);

            Assert.IsTrue(_cache.TryGet(DoorOne, out DoorDetour one));
            Assert.IsTrue(_cache.TryGet(DoorTwo, out DoorDetour two));
            Assert.IsFalse(one.IsLockout);
            Assert.IsFalse(two.IsLockout);
            Assert.Greater(one.Gain, 0f, "Closing door 1 sends the player round the north end.");
            Assert.Greater(two.Gain, 0f, "Closing door 2 sends the player round the south end.");
            Debug.Log($"[squad] door 1: open {one.OpenCost:0.0}, closed {one.ClosedCost:0.0}, gain {one.Gain:P0}; " +
                      $"door 2: open {two.OpenCost:0.0}, closed {two.ClosedCost:0.0}, gain {two.Gain:P0}");
        }

        [Test]
        public void FourSaboteursClaimTwoDifferentDoorsAndTheOtherTwoClaimNone()
        {
            Run(_brains, 0f, 1.5f);

            ActionKey a = _brains[0].CurrentAction;
            ActionKey b = _brains[1].CurrentAction;
            Assert.AreEqual(SaboteurActionKind.CloseDoor, a.Kind, Describe(_brains[0]));
            Assert.AreEqual(SaboteurActionKind.CloseDoor, b.Kind, Describe(_brains[1]));
            Assert.AreNotEqual(a.TargetId, b.TargetId, "Two Saboteurs never go for one door.");
            CollectionAssert.AreEquivalent(new[] { DoorOne, DoorTwo }, new[] { a.TargetId, b.TargetId });

            Assert.AreEqual((int?)_brains[0].Identity.AgentId, _claims.ClaimedBy(a.TargetId));
            Assert.AreEqual((int?)_brains[1].Identity.AgentId, _claims.ClaimedBy(b.TargetId));
            Assert.AreNotEqual(SaboteurActionKind.CloseDoor, _brains[2].CurrentAction.Kind, "No door is left for C.");
            Assert.AreNotEqual(SaboteurActionKind.CloseDoor, _brains[3].CurrentAction.Kind, "No door is left for D.");

            Debug.Log("[squad] after 1.5 s\n" + string.Join("\n", _brains.Select(Describe)));
        }

        [Test]
        public void EachSaboteurTakesTheDoorBesideIt()
        {
            Run(_brains, 0f, 1.5f);

            Assert.AreEqual(DoorOne, _brains[0].CurrentAction.TargetId, "A stands beside door 1.");
            Assert.AreEqual(DoorTwo, _brains[1].CurrentAction.TargetId, "B stands beside door 2.");
        }

        [Test]
        public void ANearerSaboteurThatDecidesLaterStillLosesNothingToAFartherOneThatDecidedFirst()
        {
            // D decides last (offset 3 x 62.5 ms) but stands far from both doors; A and B, nearer, claim first
            // because the ticks are staggered by letter, and D finds both doors taken.
            Run(_brains, 0f, 1.5f);

            Assert.AreNotEqual(SaboteurActionKind.CloseDoor, _brains[3].CurrentAction.Kind);
            Assert.AreEqual(2, new[] { DoorOne, DoorTwo }.Count(d => _claims.ClaimedBy(d).HasValue));
        }

        [Test]
        public void WhenTheClaimHolderIsDestroyedItsDoorGoesToAnotherSaboteur()
        {
            Run(_brains, 0f, 1.5f);
            int doorOfA = _brains[0].CurrentAction.TargetId;
            Assert.AreEqual((int?)_brains[0].Identity.AgentId, _claims.ClaimedBy(doorOfA));

            _brains[0].OnDestroyed();
            Assert.IsFalse(_claims.ClaimedBy(doorOfA).HasValue, "A destroyed Saboteur lets its claim go at once.");

            Run(_brains.Skip(1).ToArray(), 1.5f, 3f);
            // The remaining three share two doors: one of them takes the freed one.
            var closing = _brains.Skip(1).Where(b => b.CurrentAction.Kind == SaboteurActionKind.CloseDoor).ToArray();
            Assert.AreEqual(2, closing.Length, "B keeps door 2 and one of C and D takes door 1.");
            Assert.AreNotEqual(closing[0].CurrentAction.TargetId, closing[1].CurrentAction.TargetId);
            Debug.Log("[squad] after A was destroyed\n" + string.Join("\n", _brains.Skip(1).Select(Describe)));
        }

        [Test]
        public void ADoorTheSquadClosedIsNotOfferedAgainAndItsClaimIsReleased()
        {
            Run(_brains, 0f, 1.5f);
            int door = _brains[0].CurrentAction.TargetId;

            _grid.SetDoor(new Vector2Int(20, 18), DoorOne, true);
            foreach (Vector2Int cell in new[] { new Vector2Int(20, 19), new Vector2Int(20, 20), new Vector2Int(20, 21),
                         new Vector2Int(21, 18), new Vector2Int(21, 19), new Vector2Int(21, 20), new Vector2Int(21, 21) })
                _grid.SetDoor(cell, DoorOne, true);
            Run(_brains, 1.5f, 1.5f);

            Assert.AreEqual(DoorOne, door);
            Assert.AreNotEqual(DoorOne, _brains[2].CurrentAction.TargetId, "A closed door is not a candidate.");
        }

        // ---- Evidence for the log: how many searches, how much time

        [Test]
        public void TheDetourCacheSearchesTheSameForOneAndForFourSaboteurs()
        {
            (int cacheOne, int walkOne) = SearchesInTwentySeconds(1);
            (int cacheFour, int walkFour) = SearchesInTwentySeconds(4);

            Debug.Log($"[squad evidence] A* searches in 20 s of game time (player and objective fixed). Detour cache: one Saboteur {cacheOne}, " +
                      $"four Saboteurs {cacheFour}. Routes the Saboteurs planned to their door: one {walkOne}, four {walkFour}");
            Assert.AreEqual(cacheOne, cacheFour, "The shared cache answers all four with the searches of one.");
            Assert.LessOrEqual(cacheFour, 40 * 3, "At most one refresh every half second: one search for the route plus one per door on it.");
            Assert.AreEqual(1, walkOne, "One Saboteur plans one route, to the door it chose.");
            Assert.AreEqual(2, walkFour, "Two Saboteurs got a door, each plans one route; C and D chose no door.");
        }

        (int cache, int walk) SearchesInTwentySeconds(int count)
        {
            ResetWorld();
            SaboteurBrain[] squad = MakeSquad(count);
            _pathfinder.Calls = 0;
            _walkPathfinder.Calls = 0;
            Run(squad, 0f, 20f);
            return (_pathfinder.Calls, _walkPathfinder.Calls);
        }

        [Test]
        public void CostOfARefreshOnALevelSizedGrid()
        {
            // The real level is about 83 cells a side. Same two-wall layout, scaled to 84 x 84, 40 refreshes (20 s).
            const int Size = 84;
            GridGraph grid = BuildLevel(Size, Size);
            var detourSearches = new CountingPathfinder(new AStarSearch(grid));
            var cache = new DetourCache(grid, detourSearches);
            var world = new WorldBlackboard();
            var player = new Vector2Int(4, Size / 2);
            var objective = new Vector2Int(Size - 4, Size / 2);
            world.SetPlayer(new PlayerSnapshot(true, player, grid.CellToWorld(player), Vector3.zero, Vector3.right, 5f, true, 1f, 1f, false, 0f, 0f));
            world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, objective, ObjectiveTargetKind.Task) });

            cache.Refresh(world, 0f);   // warm-up
            detourSearches.Calls = 0;
            var microseconds = new List<double>();
            for (int i = 1; i <= 40; i++)
            {
                long start = Stopwatch.GetTimestamp();
                cache.Refresh(world, i * DetourCache.RefreshSeconds);
                microseconds.Add((Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency);
            }

            microseconds.Sort();
            Assert.IsTrue(cache.TryGet(DoorOne, out DoorDetour one));
            Assert.IsTrue(cache.TryGet(DoorTwo, out DoorDetour two));
            Debug.Log($"[squad evidence] detour cache refresh on an {Size} x {Size} grid, two doors on the route (open {one.OpenCost:0.0}, " +
                      $"closed {one.ClosedCost:0.0} and {two.ClosedCost:0.0}): {detourSearches.Calls / 40f:0.0} A* searches per refresh, " +
                      $"median {microseconds[20]:0} us, mean {microseconds.Average():0} us, worst {microseconds[39]:0} us");
            Assert.AreEqual(3 * 40, detourSearches.Calls, "One search for the open route and one for each of the two doors on it, every half second.");
        }

        [Test]
        public void SpreadingTheRefreshRemovesTheSpikeOnALevelSizedGrid()
        {
            // The same 84 x 84 grid, but the cache does one search per call and the squad calls it 16 times a
            // second (four Saboteurs at 4 Hz), as in the game. Compare the worst call with the unspread refresh.
            const int Size = 84;
            GridGraph grid = BuildLevel(Size, Size);
            var player = new Vector2Int(4, Size / 2);
            var objective = new Vector2Int(Size - 4, Size / 2);
            var world = new WorldBlackboard();
            world.SetPlayer(new PlayerSnapshot(true, player, grid.CellToWorld(player), Vector3.zero, Vector3.right, 5f, true, 1f, 1f, false, 0f, 0f));
            world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, objective, ObjectiveTargetKind.Task) });

            double Worst(int searchesPerCall, out int refreshes, out int searches)
            {
                var counting = new CountingPathfinder(new AStarSearch(grid));
                var cache = new DetourCache(grid, counting) { SearchesPerCall = searchesPerCall };
                for (int i = 0; i < 8; i++)
                    cache.Refresh(world, i * 0.0625f);   // warm-up
                counting.Calls = 0;
                int before = cache.Refreshes;
                double worst = 0;
                for (int i = 8; i < 8 + 320; i++)   // 20 s at 16 calls a second
                {
                    long start = Stopwatch.GetTimestamp();
                    cache.Refresh(world, i * 0.0625f);
                    worst = System.Math.Max(worst, (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency);
                }

                refreshes = cache.Refreshes - before;
                searches = counting.Calls;
                return worst;
            }

            double whole = Worst(int.MaxValue, out int wholeRefreshes, out int wholeSearches);
            double spread = Worst(1, out int spreadRefreshes, out int spreadSearches);
            Debug.Log($"[squad evidence] worst single call of the detour cache on an {Size} x {Size} grid over 20 s: {whole:0} us with the " +
                      $"whole refresh in one call ({wholeRefreshes} refreshes, {wholeSearches} searches), {spread:0} us spread one search per call " +
                      $"({spreadRefreshes} refreshes, {spreadSearches} searches)");
            Assert.AreEqual(wholeSearches / (float)wholeRefreshes, spreadSearches / (float)spreadRefreshes, 0.01f, "Same searches per refresh.");
            Assert.Less(spread, whole * 0.85, "The worst call is one search, not all of them.");
        }

        [Test]
        public void SearchesStayFlatWhenThePlayerMovesEveryFrame()
        {
            // The worst case for the cache: the player's cell changes all the time, so every half-second refresh recomputes.
            ResetWorld();
            _brains = MakeSquad(4);
            _pathfinder.Calls = 0;
            int frames = Mathf.RoundToInt(20f / Frame);
            for (int f = 0; f < frames; f++)
            {
                Vector2Int cell = new Vector2Int(4 + (f / 8) % 12, 20);
                _world.SetPlayer(new PlayerSnapshot(true, cell, _grid.CellToWorld(cell), Vector3.zero, Vector3.right, 5f, true, 1f, 1f, false, 0f, 0f));
                float time = f * Frame;
                for (int i = 0; i < _brains.Length; i++)
                    _brains[i].Tick(new AgentContext(StartCells[i], _grid.CellToWorld(StartCells[i]), Vector3.right, time, _world, default));
            }

            int searches = _pathfinder.Calls;
            Debug.Log($"[squad evidence] A* searches in 20 s with the player moving every frame, four Saboteurs: {searches} ({searches / 20f:0.0} a second)");
            Assert.LessOrEqual(searches, 20 * 2 * 3 + 3);
        }

        [Test]
        public void CostOfTheSquadsTicks()
        {
            // One warm-up second, then 20 s of game time at 64 frames a second with four Saboteurs.
            Run(_brains, 0f, 1f);
            int frames = Mathf.RoundToInt(20f / Frame);
            var tickMicroseconds = new List<double>(frames * 4);
            long wholeRun;
            var all = Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
            {
                float time = 1f + f * Frame;
                for (int i = 0; i < _brains.Length; i++)
                {
                    Vector2Int cell = StartCells[i];
                    var context = new AgentContext(cell, _grid.CellToWorld(cell), Vector3.right, time, _world, default);
                    long start = Stopwatch.GetTimestamp();
                    _brains[i].Tick(context);
                    tickMicroseconds.Add((Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency);
                }
            }

            all.Stop();
            wholeRun = all.ElapsedTicks;
            tickMicroseconds.Sort();
            double mean = tickMicroseconds.Average();
            double median = tickMicroseconds[tickMicroseconds.Count / 2];
            double p95 = tickMicroseconds[(int)(tickMicroseconds.Count * 0.95)];
            double worst = tickMicroseconds[tickMicroseconds.Count - 1];

            // The few slow ticks are the detour cache's refreshes (every half second, three searches each).
            double[] slow = tickMicroseconds.Where(us => us > 500.0).ToArray();
            double perGameSecond = wholeRun * 1000.0 / Stopwatch.Frequency / 20.0;
            Debug.Log($"[squad evidence] {tickMicroseconds.Count} Saboteur ticks over 20 s: median {median:0.0} us, mean {mean:0.0} us, 95th percentile {p95:0.0} us, worst {worst:0.0} us. " +
                      $"{slow.Length} ticks took over 0.5 ms (the cache's refreshes), averaging {(slow.Length > 0 ? slow.Average() : 0):0} us; " +
                      $"four Saboteurs cost {perGameSecond:0.000} ms per second of game time");

            Assert.Less(median, 200.0, "An ordinary tick, with no refresh, is a few microseconds.");
            Assert.LessOrEqual(slow.Length, 45, "Only the cache's refreshes, 40 in 20 s, are slow.");
        }

        [Test]
        public void AClosedDoorBlocksTheSaboteurFromClaimingItAgainDuringItsCooldown()
        {
            var selector = new ActionSelector();
            var door = new ActionKey(SaboteurActionKind.CloseDoor, DoorOne);
            selector.NotifySuccess(door, 0f);

            Assert.IsTrue(selector.IsOnCooldown(door, 9.9f));
            Assert.IsFalse(selector.IsOnCooldown(door, 10.1f));
        }

        // ---- Anti-dithering

        static int Switches(SelectorSettings settings, int seed, out int decisions)
        {
            // Two doors whose scores wander around each other at 4 Hz for two minutes: the case that would
            // make a Saboteur change its mind every decision without stability.
            var selector = new ActionSelector(settings);
            var random = new System.Random(seed);
            var a = new ActionKey(SaboteurActionKind.CloseDoor, DoorOne);
            var b = new ActionKey(SaboteurActionKind.CloseDoor, DoorTwo);
            var list = new List<ActionCandidate>(2);
            ActionKey previous = default;
            bool hasPrevious = false;
            int switches = 0;
            decisions = 480;
            for (int i = 0; i < decisions; i++)
            {
                list.Clear();
                float jitterA = (float)(random.NextDouble() * 0.2 - 0.1);
                float jitterB = (float)(random.NextDouble() * 0.2 - 0.1);
                list.Add(new ActionCandidate(a, 0.60f + jitterA));
                list.Add(new ActionCandidate(b, 0.58f + jitterB));
                SelectionResult result = selector.Select(list, i * SaboteurBrain.DecisionInterval);
                if (hasPrevious && !result.Key.Equals(previous))
                    switches++;
                previous = result.Key;
                hasPrevious = true;
            }

            return switches;
        }

        [Test]
        public void MomentumAndCommitmentCutTheSwitchesBetweenTwoCloseDoors()
        {
            int total = 0, plainTotal = 0, runs = 20, decisions = 0;
            for (int seed = 1; seed <= runs; seed++)
            {
                total += Switches(new SelectorSettings(), seed, out decisions);
                plainTotal += Switches(new SelectorSettings(momentum: 0f, commitmentSeconds: 0f), seed, out _);
            }

            double withStability = total / (double)runs;
            double without = plainTotal / (double)runs;
            Debug.Log($"[squad evidence] anti-dithering: two doors scored 0.60 and 0.58, each moving by up to 0.1 either way at 4 Hz for 2 minutes ({decisions} decisions), " +
                      $"mean of {runs} seeded runs: {without:0.0} switches with no momentum or commitment, {withStability:0.0} with momentum 0.15 and 1.5 s commitment");
            Assert.Less(withStability, without * 0.5, "The defaults at least halve the switching.");
        }
    }
}
