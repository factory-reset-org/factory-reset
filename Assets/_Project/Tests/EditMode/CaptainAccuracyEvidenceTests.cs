using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;
using Random = System.Random;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// IS evidence: how well the Captain predicts and intercepts in the real level. The level
    /// is a snapshot of the game's grid and goals (<c>Data/CaptainLevelSnapshot.txt</c>, taken in
    /// Play mode from Bootstrap), so the runs are deterministic and take seconds. Seeded
    /// scripted players walk to a goal three ways: straight there, by a detour, or with a feint
    /// (towards another goal first, then turning). The prediction is fed exactly as the brain
    /// feeds it (the player's cell at 2 Hz, the cell 5 s ago) and compared with two simpler
    /// predictors. The intercept runs put the real brain against a plain chaser.
    /// The tables go to the test output for Docs/AIPerformanceLog.md.
    /// </summary>
    [Category("Evidence")]
    public class CaptainAccuracyEvidenceTests
    {
        const string SnapshotPath = "Assets/_Project/Tests/EditMode/Data/CaptainLevelSnapshot.txt";
        const float WalkSpeed = 4f;       // PlayerController.walkSpeed
        const float SprintSpeed = 7f;     // PlayerController.sprintSpeed
        const float Sample = CaptainBrain.DecisionInterval;
        const float ArrivedRadius = 1f;
        const float MinTripMetres = 12f;
        const int TripsPerRoute = 60;
        const int InterceptTrips = 40;
        const float Dt = 0.05f;

        enum Route { Direct, Detour, Feint }

        // ---- The level ---------------------------------------------------------------

        sealed class Level
        {
            public GridGraph Grid;
            public readonly Dictionary<int, List<ObjectiveTarget>> Targets = new Dictionary<int, List<ObjectiveTarget>>();
            public Vector3 Cell00;
        }

        static Level LoadLevel()
        {
            string[] lines = File.ReadAllLines(SnapshotPath);
            var level = new Level();
            int width = 0, height = 0, gridStart = -1;
            for (int i = 0; i < lines.Length && gridStart < 0; i++)
            {
                string[] parts = lines[i].Trim().Split(' ');
                switch (parts[0])
                {
                    case "size":
                        width = int.Parse(parts[1]);
                        height = int.Parse(parts[2]);
                        break;
                    case "cell00":
                        level.Cell00 = new Vector3(Parse(parts[1]), 0f, Parse(parts[2]));
                        break;
                    case "targets":
                        var targets = new List<ObjectiveTarget>();
                        for (int p = 2; p < parts.Length; p++)
                        {
                            string[] t = parts[p].Split(':');
                            targets.Add(new ObjectiveTarget(int.Parse(t[0]), new Vector2Int(int.Parse(t[2]), int.Parse(t[3])),
                                (ObjectiveTargetKind)Enum.Parse(typeof(ObjectiveTargetKind), t[1])));
                        }
                        level.Targets[int.Parse(parts[1])] = targets;
                        break;
                    case "grid":
                        gridStart = i + 1;
                        break;
                }
            }

            float half = GridGraph.CellSize / 2f;
            level.Grid = new GridGraph(width, height, new Vector3(level.Cell00.x - half, 0f, level.Cell00.z - half));
            using (GridGraph.Batch batch = level.Grid.BeginBatch())
            {
                for (int y = 0; y < height; y++)
                {
                    string row = lines[gridStart + y];
                    for (int x = 0; x < width; x++)
                    {
                        var cell = new Vector2Int(x, y);
                        switch (row[x])
                        {
                            case '#': batch.SetWalkable(cell, false); break;
                            case 'b': batch.AddBlocker(cell); break;
                            case 'd': batch.SetDoorway(cell, true); break;
                            case 'x': batch.SetDoor(cell, 1, true); break;
                        }
                    }
                }
                batch.Commit();
            }
            return level;
        }

        static float Parse(string text) => float.Parse(text, CultureInfo.InvariantCulture);

        static GoalCategory CategoryOf(ObjectiveTargetKind kind)
        {
            switch (kind)
            {
                case ObjectiveTargetKind.Switch: return GoalCategory.Switch;
                case ObjectiveTargetKind.Console: return GoalCategory.Console;
                case ObjectiveTargetKind.Battery: return GoalCategory.Battery;
                default: return GoalCategory.Task;
            }
        }

        // ---- Scripted players --------------------------------------------------------

        sealed class Trip
        {
            public int Goal;                 // index of the true goal
            public List<Vector3> Points;     // the walk, cell centres
            public float[] Along;            // distance along the walk at each point
            public float Length => Along[Along.Length - 1];
            public float TurnAt = -1f;       // feint: distance at which the player turns to the true goal
            public int Decoy = -1;

            public Vector3 At(float distance)
            {
                if (distance >= Length)
                    return Points[Points.Count - 1];
                int i = Array.BinarySearch(Along, distance);
                if (i >= 0)
                    return Points[i];
                i = ~i;
                float t = (distance - Along[i - 1]) / (Along[i] - Along[i - 1]);
                return Vector3.Lerp(Points[i - 1], Points[i], t);
            }
        }

        static float Metres(List<Vector2Int> cells)
        {
            float cost = 0f;
            for (int i = 1; i < cells.Count; i++)
                cost += BaseCostModel.Instance.StepCost(cells[i - 1], cells[i]);
            return cost * GridGraph.CellSize;
        }

        static List<Vector2Int> Path(AStarSearch astar, Vector2Int from, Vector2Int to)
        {
            PathResult result = astar.FindPath(from, to, BaseCostModel.Instance);
            return result.Found ? new List<Vector2Int>(result.Cells) : null;
        }

        static Trip MakeTrip(GridGraph grid, List<Vector2Int> cells, int goal)
        {
            var trip = new Trip { Goal = goal, Points = new List<Vector3>(cells.Count) };
            trip.Along = new float[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                trip.Points.Add(grid.CellToWorld(cells[i]));
                trip.Along[i] = i == 0 ? 0f : trip.Along[i - 1] + Vector3.Distance(trip.Points[i - 1], trip.Points[i]);
            }
            return trip;
        }

        static Vector2Int RandomCell(GridGraph grid, Random rng)
        {
            while (true)
            {
                var cell = new Vector2Int(rng.Next(grid.Width), rng.Next(grid.Height));
                if (grid.IsTraversable(cell))
                    return cell;
            }
        }

        static Trip NewTrip(GridGraph grid, AStarSearch astar, Vector2Int[] goals, Route route, Random rng,
            Func<Vector2Int, bool> startAllowed = null, int[] trueGoals = null)
        {
            while (true)
            {
                Vector2Int start = RandomCell(grid, rng);
                if (startAllowed != null && !startAllowed(start))
                    continue;
                int goal = trueGoals != null ? trueGoals[rng.Next(trueGoals.Length)] : rng.Next(goals.Length);
                List<Vector2Int> direct = Path(astar, start, goals[goal]);
                if (direct == null || Metres(direct) < MinTripMetres)
                    continue;

                switch (route)
                {
                    case Route.Direct:
                        return MakeTrip(grid, direct, goal);

                    case Route.Detour:
                    {
                        // Off the shortest route by way of a cell near its middle: exploring, or
                        // going round something.
                        Vector2Int middle = direct[direct.Count / 2];
                        var via = new Vector2Int(middle.x + rng.Next(-12, 13), middle.y + rng.Next(-12, 13));
                        if (!grid.IsTraversable(via))
                            continue;
                        List<Vector2Int> first = Path(astar, start, via);
                        List<Vector2Int> second = Path(astar, via, goals[goal]);
                        if (first == null || second == null)
                            continue;
                        float ratio = (Metres(first) + Metres(second)) / Metres(direct);
                        if (ratio < 1.15f || ratio > 1.7f)
                            continue;
                        first.AddRange(second.Skip(1));
                        return MakeTrip(grid, first, goal);
                    }

                    default:
                    {
                        // Heads for another goal for half of that walk, then turns to the true goal.
                        int decoy = rng.Next(goals.Length - 1);
                        if (decoy >= goal)
                            decoy++;
                        List<Vector2Int> towards = Path(astar, start, goals[decoy]);
                        if (towards == null || Metres(towards) < MinTripMetres)
                            continue;
                        List<Vector2Int> bait = towards.GetRange(0, towards.Count / 2);
                        List<Vector2Int> rest = Path(astar, bait[bait.Count - 1], goals[goal]);
                        if (rest == null || Metres(rest) < MinTripMetres / 2f)
                            continue;
                        float turnAt = 0f;
                        Trip trip = MakeTrip(grid, bait.Concat(rest.Skip(1)).ToList(), goal);
                        turnAt = trip.Along[bait.Count - 1];
                        trip.TurnAt = turnAt;
                        trip.Decoy = decoy;
                        return trip;
                    }
                }
            }
        }

        // ---- Part 1: prediction ------------------------------------------------------

        sealed class PredictionStats
        {
            public readonly int[] Samples = new int[4];
            public readonly int[] Model = new int[4];
            public readonly int[] Nearest = new int[4];
            public readonly int[] Prior = new int[4];
            public int Trips;
            public int ConfidentlyRight;
            public readonly List<float> TimeToRight = new List<float>();
            public readonly List<float> ShareLeft = new List<float>();
            public int ConfidentlyWrong;
            public int FeintTaken;           // feint: the decoy was predicted before the turn
            public int Switched;             // feint: the true goal predicted again before arrival
            public readonly List<float> SwitchDelay = new List<float>();
        }

        // Two ways to measure. Movement only: every goal counts as a task (equal priors) and the
        // player picks any goal, so only the movement can tell them apart. In game: the real
        // priors, and the player heads where the game lets them (the chapter's tasks; the switch
        // and the console are sealed until those are done).
        static PredictionStats RunPredictions(Level level, int chapter, Route route, int seed, bool inGame)
        {
            GridGraph grid = level.Grid;
            List<ObjectiveTarget> targets = level.Targets[chapter];
            var goals = targets.Select(t => new CandidateGoal(t.Id, t.Cell, inGame ? CategoryOf(t.Kind) : GoalCategory.Task)).ToList();
            int[] trueGoals = inGame
                ? Enumerable.Range(0, targets.Count).Where(i => targets[i].Kind == ObjectiveTargetKind.Task).ToArray()
                : null;
            Vector2Int[] goalCells = targets.Select(t =>
            {
                Assert.IsTrue(grid.TryFindNearestTraversable(t.Cell, GoalInference.SnapRadius, out Vector2Int c), $"Goal {t.Id} has no walkable cell near it.");
                return c;
            }).ToArray();
            bool final = chapter == WorldBlackboard.FinalChapter;
            var priors = new float[goals.Count];
            GoalPriors.Compute(goals, final, 1f, priors);
            int priorBest = Array.IndexOf(priors, priors.Max());

            var astar = new AStarSearch(grid);
            var inference = new GoalInference(grid);
            var rng = new Random(seed);
            var stats = new PredictionStats();

            for (int n = 0; n < TripsPerRoute; n++)
            {
                Trip trip = NewTrip(grid, astar, goalCells, route, rng, trueGoals: trueGoals);
                var track = new PlayerTrack();
                float arrival = trip.Length / WalkSpeed;
                bool right = false, wrong = false, tookBait = false, switched = false;
                float turnTime = trip.TurnAt >= 0f ? trip.TurnAt / WalkSpeed : -1f;

                for (float t = 0f; t <= arrival + 1e-3f; t += Sample)
                {
                    Vector3 position = trip.At(t * WalkSpeed);
                    if (Vector3.Distance(position, grid.CellToWorld(goalCells[trip.Goal])) <= ArrivedRadius)
                        break;   // at the goal: the brain leaves a reached goal out
                    Vector2Int cell = grid.WorldToCell(position);
                    track.Record(t, cell);
                    if (!track.TryGetPast(t, out Vector2Int past) ||
                        !inference.Update(goals, past, cell, final, 1f))
                        continue;

                    int best = inference.MostLikelyIndex;
                    bool sure = inference.Confidence >= CaptainBrain.ConfidenceThreshold;
                    int quarter = Mathf.Min(3, (int)(t / arrival * 4f));
                    stats.Samples[quarter]++;
                    if (best == trip.Goal) stats.Model[quarter]++;
                    if (Nearest(grid, inference, goals, cell) == trip.Goal) stats.Nearest[quarter]++;
                    if (priorBest == trip.Goal) stats.Prior[quarter]++;

                    bool afterTurn = turnTime < 0f || t >= turnTime;
                    if (!afterTurn && best == trip.Decoy && sure)
                        tookBait = true;
                    if (afterTurn && sure && best == trip.Goal && !right)
                    {
                        right = true;
                        stats.TimeToRight.Add(t);
                        stats.ShareLeft.Add(1f - t / arrival);
                    }
                    if (afterTurn && sure && best != trip.Goal && (turnTime < 0f || t >= turnTime + 2f))
                        wrong = true;   // feint: 2 s after the turn the old goal no longer counts as reading the player
                    if (turnTime >= 0f && afterTurn && best == trip.Goal && !switched)
                    {
                        switched = true;
                        stats.SwitchDelay.Add(t - turnTime);
                    }
                }
                stats.Trips++;
                if (right) stats.ConfidentlyRight++;
                if (wrong) stats.ConfidentlyWrong++;
                if (tookBait) stats.FeintTaken++;
                if (switched) stats.Switched++;
            }
            return stats;
        }

        // "The goal closest to the player now": the obvious guess without the movement.
        static int Nearest(GridGraph grid, GoalInference inference, List<CandidateGoal> goals, Vector2Int cell)
        {
            if (!grid.TryFindNearestTraversable(cell, GoalInference.SnapRadius, out Vector2Int x))
                return -1;
            int best = -1;
            float bestCost = float.PositiveInfinity;
            for (int i = 0; i < goals.Count; i++)
                if (inference.TryGetGoalField(goals[i].Id, out DijkstraField field) && field.Cost(x) < bestCost)
                {
                    bestCost = field.Cost(x);
                    best = i;
                }
            return best;
        }

        // ---- Part 2: intercept -------------------------------------------------------

        sealed class InterceptStats
        {
            public int Trips;
            public int Contacts;
            public readonly List<float> LeadAtContact = new List<float>();
        }

        // A Captain body: walks the route it is given at its speed, faces where it walks, and
        // turns to its look target while it stands (360 degrees a second, like the follower).
        sealed class Body
        {
            List<Vector3> _path = new List<Vector3>();
            int _next;
            float _speed;
            public Vector3 Position;
            public Vector3 Forward = Vector3.forward;

            public void Follow(IReadOnlyList<Vector3> path, float speed)
            {
                if (path == null)
                    return;
                _path = new List<Vector3>(path);
                _next = 0;
                _speed = speed;
            }

            public void Step(Vector3? look)
            {
                while (_next < _path.Count && Flat(_path[_next] - Position).magnitude <= 0.3f)
                    _next++;
                if (_next < _path.Count)
                {
                    Vector3 to = Flat(_path[_next] - Position);
                    Position += to.normalized * Mathf.Min(_speed * Dt, to.magnitude);
                    Forward = to.normalized;
                }
                else if (look.HasValue)
                {
                    Vector3 to = Flat(look.Value - Position);
                    if (to.sqrMagnitude > 1e-4f)
                        Forward = Vector3.RotateTowards(Forward, to.normalized, Mathf.Deg2Rad * 360f * Dt, 0f);
                }
            }

            static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        }

        static InterceptStats RunIntercepts(Level level, int chapter, Route route, float playerSpeed, bool predictor, int seed)
        {
            GridGraph grid = level.Grid;
            List<ObjectiveTarget> targets = level.Targets[chapter];
            Vector2Int[] goalCells = targets.Select(t =>
            {
                grid.TryFindNearestTraversable(t.Cell, GoalInference.SnapRadius, out Vector2Int c);
                return c;
            }).ToArray();
            // The Captain wakes at its spawn in the Control Room, by the console.
            grid.TryFindNearestTraversable(grid.WorldToCell(new Vector3(11.13f, 0f, 32.56f)), 6, out Vector2Int captainStart);

            var astar = new AStarSearch(grid);
            var rng = new Random(seed);
            var stats = new InterceptStats();
            Vector3 captainHome = grid.CellToWorld(captainStart);
            // Trips start out of the Captain's reach: at least 20 m away and out of sight.
            Func<Vector2Int, bool> farEnough = cell =>
            {
                List<Vector2Int> p = Path(astar, captainStart, cell);
                Vector3 world = grid.CellToWorld(cell);
                return p != null && Metres(p) >= 20f && !GridLineCheck.IsSightClear(grid, captainHome, world);
            };

            // The player heads where the game lets them: the chapter's open tasks.
            int[] tasks = Enumerable.Range(0, targets.Count).Where(i => targets[i].Kind == ObjectiveTargetKind.Task).ToArray();
            for (int n = 0; n < InterceptTrips; n++)
            {
                Trip trip = NewTrip(grid, astar, goalCells, route, rng, farEnough, tasks);
                var world = new WorldBlackboard();
                world.SetObjectiveTargets(targets);
                world.SetChapterIndex(chapter);
                var brain = predictor ? new CaptainBrain(grid, new AStarSearch(grid), world, startAwake: true) : null;
                var body = new Body { Position = captainHome };
                float arrival = trip.Length / playerSpeed;
                float nextChase = 0f;
                bool contact = false;

                for (float t = 0f; t <= arrival; t += Dt)
                {
                    Vector3 player = trip.At(t * playerSpeed);
                    Vector3 ahead = trip.At(t * playerSpeed + 0.5f);
                    Vector3 velocity = (ahead - player).sqrMagnitude > 1e-6f ? (ahead - player).normalized * playerSpeed : Vector3.zero;
                    Vector2Int playerCell = grid.WorldToCell(player);
                    world.SetPlayer(new PlayerSnapshot(true, playerCell, player, velocity,
                        velocity.sqrMagnitude > 0f ? velocity.normalized : Vector3.forward,
                        SprintSpeed, true, 1f, 1f, false, 0f, -1f));

                    if (Vector3.Distance(body.Position, player) <= CaptainBrain.EngageRange &&
                        GridLineCheck.IsSightClear(grid, body.Position, player))
                    {
                        contact = true;
                        stats.LeadAtContact.Add(arrival - t);
                        break;
                    }

                    if (predictor)
                    {
                        AgentIntent intent = brain.Tick(new AgentContext(grid.WorldToCell(body.Position), body.Position,
                            body.Forward, t, world, default(SensorSnapshot)));
                        body.Follow(intent.Path, intent.DesiredSpeed);
                        body.Step(intent.LookTarget);
                    }
                    else
                    {
                        // The chaser: every 0.5 s, the shortest route to where the player is now.
                        if (t >= nextChase)
                        {
                            nextChase = t + Sample;
                            List<Vector2Int> cells = Path(astar, grid.WorldToCell(body.Position), playerCell);
                            if (cells != null)
                                body.Follow(cells.Select(grid.CellToWorld).ToList(), CaptainBrain.InterceptSpeed);
                        }
                        body.Step(player);
                    }
                }
                stats.Trips++;
                if (contact) stats.Contacts++;
            }
            return stats;
        }

        // ---- The runs ----------------------------------------------------------------

        // To the test output, the console, and Temp/Evidence (not tracked) for the logs.
        static void Publish(string file, StringBuilder report)
        {
            TestContext.Out.WriteLine(report.ToString());
            Debug.Log(report.ToString());
            Directory.CreateDirectory("Temp/Evidence");
            File.WriteAllText(System.IO.Path.Combine("Temp/Evidence", file), report.ToString());
        }

        static string Pct(int part, int whole) => whole == 0 ? "-" : (100f * part / whole).ToString("0", CultureInfo.InvariantCulture) + "%";

        static float Median(List<float> values)
        {
            if (values.Count == 0)
                return float.NaN;
            List<float> sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2f;
        }

        static string F(float value, string format = "0.0") =>
            float.IsNaN(value) ? "-" : value.ToString(format, CultureInfo.InvariantCulture);

        [Test]
        public void TheSnapshotIsTheGamesLevel()
        {
            Level level = LoadLevel();
            Assert.AreEqual(83, level.Grid.Width);
            Assert.AreEqual(level.Cell00, level.Grid.CellToWorld(Vector2Int.zero), "Cells sit where the game's do.");
            Assert.AreEqual(3, level.Targets[3].Count);
            Assert.AreEqual(4, level.Targets[4].Count);
        }

        [Test]
        public void PredictionFromMovementAloneInTheRealLevel()
        {
            Dictionary<string, PredictionStats> all = PredictionReport(inGame: false, "CaptainPrediction_MovementOnly.md",
                "Movement only: every goal equally likely beforehand, the player heading for any of them.");

            // Guards against a regression, not targets: over all trips the movement beats the
            // nearest-goal guess, and a straight walk is read right most of its second half.
            Assert.Greater(all.Values.Sum(s => s.Model.Sum()), all.Values.Sum(s => s.Nearest.Sum()), "The inference must beat the nearest goal overall.");
            foreach (string key in new[] { "3-Direct", "4-Direct" })
            {
                PredictionStats s = all[key];
                Assert.Greater((float)(s.Model[2] + s.Model[3]) / (s.Samples[2] + s.Samples[3]), 0.7f, key);
            }
        }

        [Test]
        public void PredictionInGameInTheRealLevel()
        {
            Dictionary<string, PredictionStats> all = PredictionReport(inGame: true, "CaptainPrediction_InGame.md",
                "In game: the real priors, and the player heading where the game lets them (the chapter's tasks: in Chapter 3 its open task, in Chapter 4 a core).");

            PredictionStats direct4 = all["4-Direct"];
            Assert.Greater((float)(direct4.Model[2] + direct4.Model[3]) / (direct4.Samples[2] + direct4.Samples[3]), 0.7f,
                "Chapter 4: right most of the second half of a straight walk to a core.");
            Assert.LessOrEqual((float)direct4.ConfidentlyWrong / direct4.Trips, 0.25f,
                "Chapter 4: not sure of the wrong goal (the sealed console) on a straight walk to a core.");
        }

        static Dictionary<string, PredictionStats> PredictionReport(bool inGame, string file, string title)
        {
            Level level = LoadLevel();
            var report = new StringBuilder();
            report.AppendLine(title);
            report.AppendLine($"{TripsPerRoute} seeded trips per row, walking at {WalkSpeed} m/s, predicted at 2 Hz as the brain does.");
            report.AppendLine("Accuracy = share of samples whose most likely goal is the true one, by quarter of the trip.");
            report.AppendLine();
            report.AppendLine("| Chapter | Route | Predictor | 1st quarter | 2nd | 3rd | 4th | All |");
            report.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");

            var all = new Dictionary<string, PredictionStats>();
            int seed = 1;
            foreach (int chapter in new[] { 3, 4 })
                foreach (Route route in new[] { Route.Direct, Route.Detour, Route.Feint })
                {
                    PredictionStats s = RunPredictions(level, chapter, route, seed++, inGame);
                    all[$"{chapter}-{route}"] = s;
                    Row(report, chapter, route, "Captain (Bayesian)", s.Model, s.Samples);
                    Row(report, chapter, route, "Nearest goal", s.Nearest, s.Samples);
                    Row(report, chapter, route, "Prior only", s.Prior, s.Samples);
                }

            report.AppendLine();
            report.AppendLine("| Chapter | Route | Confidently right (P >= 0.5) | Median time to it | Median share of the trip left | Confidently wrong | Feint: took the bait | Feint: switched back | Median switch delay |");
            report.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (KeyValuePair<string, PredictionStats> entry in all)
            {
                PredictionStats s = entry.Value;
                string[] key = entry.Key.Split('-');
                bool feint = key[1] == nameof(Route.Feint);
                report.AppendLine($"| {key[0]} | {key[1]} | {Pct(s.ConfidentlyRight, s.Trips)} | {F(Median(s.TimeToRight))} s | {F(Median(s.ShareLeft) * 100f, "0")}% | {Pct(s.ConfidentlyWrong, s.Trips)} | " +
                    (feint ? $"{Pct(s.FeintTaken, s.Trips)} | {Pct(s.Switched, s.Trips)} | {F(Median(s.SwitchDelay))} s |" : "- | - | - |"));
            }
            Publish(file, report);
            return all;
        }

        static void Row(StringBuilder report, int chapter, Route route, string name, int[] hits, int[] samples) =>
            report.AppendLine($"| {chapter} | {route} | {name} | {Pct(hits[0], samples[0])} | {Pct(hits[1], samples[1])} | " +
                $"{Pct(hits[2], samples[2])} | {Pct(hits[3], samples[3])} | {Pct(hits.Sum(), samples.Sum())} |");

        [Test]
        public void InterceptingBeatsChasingInTheRealLevel()
        {
            Level level = LoadLevel();
            var report = new StringBuilder();
            report.AppendLine($"Chapter 3, the Captain starting at its spawn by the console and the player heading for the open task; {InterceptTrips} seeded trips per row, each starting at least 20 m away and out of its sight.");
            report.AppendLine("Contact = the Captain within 10 m of the player with line of sight, before the player reaches their goal.");
            report.AppendLine();
            report.AppendLine("| Player | Route | Captain | Contact before the goal | Median seconds to spare |");
            report.AppendLine("| --- | --- | --- | --- | --- |");

            int seed = 100;
            var rows = new List<(string name, InterceptStats predictor, InterceptStats chaser)>();
            foreach (float speed in new[] { WalkSpeed, SprintSpeed })
                foreach (Route route in new[] { Route.Direct, Route.Detour })
                {
                    InterceptStats predicted = RunIntercepts(level, 3, route, speed, true, seed);
                    InterceptStats chased = RunIntercepts(level, 3, route, speed, false, seed);
                    seed++;
                    string player = speed == WalkSpeed ? "Walking 4 m/s" : "Sprinting 7 m/s";
                    report.AppendLine($"| {player} | {route} | Predicts and intercepts | {Pct(predicted.Contacts, predicted.Trips)} | {F(Median(predicted.LeadAtContact))} s |");
                    report.AppendLine($"| {player} | {route} | Chases | {Pct(chased.Contacts, chased.Trips)} | {F(Median(chased.LeadAtContact))} s |");
                    rows.Add(($"{player} {route}", predicted, chased));
                }
            Publish("CaptainIntercept.md", report);

            int predictedTotal = rows.Sum(r => r.predictor.Contacts);
            int chasedTotal = rows.Sum(r => r.chaser.Contacts);
            Assert.Greater(predictedTotal, chasedTotal, "Predicting must reach the player more often than chasing.");
        }
    }
}
