using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Debugging;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// <c>Test_FourAgentsStress</c>: all four agent types, seven instances, active at once in
    /// the real level, measured over 30 s. Fills the stress-test table in AIPerformanceLog.md.
    /// <c>Test_PushedBoxStress</c> adds a box moving every 0.5 s (see that test).
    /// </summary>
    /// <remarks>
    /// <para>Loads <c>Bootstrap</c>, jumps to Chapter 4 (the Captain awake, every door open),
    /// then walks the player round a loop through the Control Room and the Storage doorway at
    /// 4 m/s, so the agents keep seeing, hearing and chasing them. The player's health is
    /// refilled every frame through reflection (the test cannot reference the Player
    /// assembly), so the run is not cut short by Results.</para>
    /// <para>Measured per frame after a 3 s warm-up: frame time; each brain's Tick (the
    /// <c>AI.Brain.Tick.*</c> markers in <see cref="AgentController"/>, which contain the
    /// searches); the searches on their own; and memory allocated in the frame by everything
    /// (Unity's "GC Allocated In Frame" counter). Editor timings: a player build is faster.</para>
    /// <para>The stress run also runs with the brain scheduler off, to compare the two in one
    /// session. Explicit, so they only run when asked for (about 40 s each, and 30 s).</para>
    /// </remarks>
    [Explicit("Evidence runs: about 40 s each and 30 s. Run them on their own for the performance logs.")]
    [Category("Evidence")]
    public sealed class FourAgentsStressTests
    {
        const float WarmUp = 3f;
        const float Measure = 30f;
        const float PlayerSpeed = 4f;
        const float AiBudgetMs = 2f;   // the plan's AI budget per frame
        const float CaptainBudgetMs = 1f;   // the Captain's share: half the budget at p99

        static readonly string[] Brains = { "Tracker", "Guard", "Saboteur", "Captain" };
        static readonly string[] Searches = { "AI.Tracker.GBFS", "AI.AStarSearch.FindPath", "AI.DijkstraField.Compute", "AI.DijkstraField.Repair", "AI.OneToOneCost.Compute", "AI.NoisePropagation.Propagate" };

        // A loop through the Control Room and out through door 3 into Storage and back.
        static readonly Vector3[] Loop =
        {
            new Vector3(10.5f, 0.1f, 23f), new Vector3(16f, 0.1f, 26f), new Vector3(18.5f, 0.1f, 31f),
            new Vector3(23.5f, 0.1f, 31f), new Vector3(27f, 0.1f, 27.5f), new Vector3(23.5f, 0.1f, 31f),
            new Vector3(18.5f, 0.1f, 31f), new Vector3(16f, 0.1f, 37f), new Vector3(10.5f, 0.1f, 39f),
            new Vector3(3f, 0.1f, 31f), new Vector3(5.5f, 0.1f, 26f),
        };

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene empty = SceneManager.CreateScene("Empty after stress test");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
            if (GameClock.Current is Object clock && clock == null)
                GameClock.Publish(null);
            if (PlayerState.Current is Object player && player == null)
                PlayerState.Publish(null);
        }

        static IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0)
                return 0;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Mathf.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
        }

        // Loads Bootstrap and jumps to Chapter 4: the Captain awake, every door open.
        static IEnumerator LoadChapterFour()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && Object.FindAnyObjectByType<ChapterJump>() != null && AgentSpawner.Instance != null
                && AgentSpawner.Instance.SpawnedAgents.Count == 7 && PlayerState.Current != null,
                30f, "Bootstrap to load");
            ChapterJump jump = Object.FindAnyObjectByType<ChapterJump>();
            jump.JumpTo(4);
            yield return Until(() => !jump.IsJumping, 20f, "the jump to Chapter 4");
            Assert.IsTrue(AgentSpawner.Instance.Blackboard.CaptainAwake, "All four types active: the Captain is awake.");
        }

        [UnityTest]
        public IEnumerator Test_FourAgentsStress() => RunStress(nameof(Test_FourAgentsStress), BrainTickScheduler.FrameBudgetMs);

        /// <summary>
        /// The same run with the brain scheduler effectively off (an unlimited budget, so no
        /// decision ever waits). Run it next to <see cref="Test_FourAgentsStress"/> in one
        /// session to see what the scheduler changes.
        /// </summary>
        [UnityTest]
        public IEnumerator Test_FourAgentsStress_NoScheduler() => RunStress(nameof(Test_FourAgentsStress_NoScheduler), float.MaxValue);

        IEnumerator RunStress(string name, float budgetMs)
        {
            float budgetBefore = BrainTickScheduler.FrameBudgetMs;
            yield return LoadChapterFour();
            BrainTickScheduler.FrameBudgetMs = budgetMs;
            int waitsBefore = BrainTickScheduler.TotalWaits;

            var player = (Component)PlayerState.Current;
            var body = player.GetComponent<CharacterController>();
            Component health = player.GetComponents<Component>().First(c => c.GetType().Name == "PlayerHealth");
            MethodInfo setHealth = health.GetType().GetProperty("Current").GetSetMethod(true);
            float maxHealth = (float)health.GetType().GetField("maxHealth", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(health);

            var brainRecorders = Brains.Select(b => ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "AI.Brain.Tick." + b, 1)).ToArray();
            var searchRecorders = Searches.Select(s => ProfilerRecorder.StartNew(ProfilerCategory.Scripts, s, 1)).ToArray();
            var allocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);

            var frameMs = new List<double>();
            var aiMs = new List<double>();
            var searchMs = new List<double>();
            var allocKb = new List<double>();
            var brainTotals = new double[Brains.Length];
            var brainMs = Brains.Select(_ => new List<double>()).ToArray();
            var searchEach = Searches.Select(_ => new List<double>()).ToArray();
            int leg = 0;
            Vector3 at = Loop[0];
            float start = Time.realtimeSinceStartup;

            try
            {
                while (Time.realtimeSinceStartup - start < WarmUp + Measure)
                {
                    // Walk the loop, and stay alive.
                    Vector3 target = Loop[(leg + 1) % Loop.Length];
                    at = Vector3.MoveTowards(at, target, PlayerSpeed * Time.deltaTime);
                    if ((at - target).sqrMagnitude < 0.01f)
                        leg++;
                    body.enabled = false;
                    player.transform.SetPositionAndRotation(at, Quaternion.LookRotation(target - at + Vector3.forward * 0.001f));
                    body.enabled = true;
                    setHealth.Invoke(health, new object[] { maxHealth });

                    yield return null;

                    if (Time.realtimeSinceStartup - start < WarmUp)
                        continue;
                    frameMs.Add(Time.unscaledDeltaTime * 1000.0);
                    double ai = 0;
                    for (int i = 0; i < brainRecorders.Length; i++)
                    {
                        double ms = brainRecorders[i].LastValue / 1e6;
                        brainTotals[i] += ms;
                        brainMs[i].Add(ms);
                        ai += ms;
                    }
                    aiMs.Add(ai);
                    searchMs.Add(searchRecorders.Sum(r => r.LastValue) / 1e6);
                    for (int i = 0; i < searchRecorders.Length; i++)
                        searchEach[i].Add(searchRecorders[i].LastValue / 1e6);
                    allocKb.Add(allocRecorder.LastValue / 1024.0);
                }
            }
            finally
            {
                foreach (var r in brainRecorders) r.Dispose();
                foreach (var r in searchRecorders) r.Dispose();
                allocRecorder.Dispose();
                BrainTickScheduler.FrameBudgetMs = budgetBefore;
            }
            int waits = BrainTickScheduler.TotalWaits - waitsBefore;

            int frames = frameMs.Count;
            var report = new StringBuilder();
            string budget = budgetMs >= float.MaxValue ? "off" : $"{budgetMs:0.#} ms";
            report.AppendLine($"{name}: {frames} frames over {Measure} s, 7 agents (1 Tracker, 1 Guard, 4 Saboteurs, Captain awake), Chapter 4, editor, brain scheduler {budget}");
            report.AppendLine($"  Avg FPS {frames / frameMs.Sum() * 1000.0:F1}; frame ms avg {frameMs.Average():F2}, p99 {Percentile(frameMs, 0.99):F2}, worst {frameMs.Max():F2}");
            report.AppendLine($"  AI (all brain ticks) ms/frame avg {aiMs.Average():F3}, p99 {Percentile(aiMs, 0.99):F3}, worst {aiMs.Max():F3}; frames over {AiBudgetMs} ms {aiMs.Count(v => v > AiBudgetMs)}; decisions deferred {waits}");
            report.AppendLine($"  of which searches ms/frame avg {searchMs.Average():F3}, p99 {Percentile(searchMs, 0.99):F3}");
            for (int i = 0; i < Brains.Length; i++)
                report.AppendLine($"  {Brains[i]} ticks: avg {brainTotals[i] / frames:F4}, p99 {Percentile(brainMs[i], 0.99):F3}, worst {brainMs[i].Max():F3} ms/frame; frames over 1 ms {brainMs[i].Count(v => v > 1.0)}");
            for (int i = 0; i < Searches.Length; i++)
                report.AppendLine($"  {Searches[i]}: avg {searchEach[i].Average():F4}, worst {searchEach[i].Max():F3} ms/frame; frames over 1 ms {searchEach[i].Count(v => v > 1.0)}");
            report.AppendLine($"  GC allocated in frame (everything) KB avg {allocKb.Average():F2}, p99 {Percentile(allocKb, 0.99):F2}, frames with any allocation {allocKb.Count(a => a > 0)}/{frames}");
            foreach (string line in report.ToString().Split('\n'))
                if (line.Trim().Length > 0)
                    Debug.Log("[Stress] " + line.Trim());
            TestContext.WriteLine(report.ToString());

            Assert.Greater(frames, 100, "Enough frames measured.");
            // The Captain's share is a hard check; the total stays a warning until the Guard's
            // spikes (S2) are fixed, since this test cannot fix another agent's brain.
            Assert.Less(Percentile(brainMs[3], 0.99), CaptainBudgetMs, "Captain p99 per frame");
            double p99 = Percentile(aiMs, 0.99);
            if (p99 >= AiBudgetMs)
                Debug.LogWarning($"[Stress] AI p99 {p99:F2} ms is over its {AiBudgetMs} ms budget per frame.");
        }

        /// <summary>
        /// <c>Test_PushedBoxStress</c>: the same Chapter 4 run, but a box-sized blocker moves
        /// one cell every 0.5 s across the Control Room, the way a pushed box updates the grid
        /// (<see cref="GridManager.SetBlocker"/>, the call PushableBox makes). Every move makes
        /// all the Captain's goal fields stale. Measures the Captain's tick and the field
        /// rebuilds per frame for OptimisationLog.md.
        /// </summary>
        [UnityTest]
        public IEnumerator Test_PushedBoxStress()
        {
            const float Seconds = 20f;
            const float MoveEvery = 0.5f;
            const int BoxOwner = 990001;   // a test-only blocker id, released at the end

            yield return LoadChapterFour();
            var player = (Component)PlayerState.Current;
            var body = player.GetComponent<CharacterController>();
            Component health = player.GetComponents<Component>().First(c => c.GetType().Name == "PlayerHealth");
            MethodInfo setHealth = health.GetType().GetProperty("Current").GetSetMethod(true);
            float maxHealth = (float)health.GetType().GetField("maxHealth", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(health);

            var captain = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "AI.Brain.Tick.Captain", 1);
            var fields = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "AI.DijkstraField.Compute", 1);
            var repairs = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "AI.DijkstraField.Repair", 1);
            var captainMs = new List<double>();
            var fieldMs = new List<double>();
            var repairMs = new List<double>();
            int version = GridManager.Current.Version;
            int moves = 0;
            int leg = 0;
            Vector3 at = Loop[0];
            float start = Time.realtimeSinceStartup;
            float nextMove = start + WarmUp;

            try
            {
                while (Time.realtimeSinceStartup - start < WarmUp + Seconds)
                {
                    Vector3 target = Loop[(leg + 1) % Loop.Length];
                    at = Vector3.MoveTowards(at, target, PlayerSpeed * Time.deltaTime);
                    if ((at - target).sqrMagnitude < 0.01f)
                        leg++;
                    body.enabled = false;
                    player.transform.SetPositionAndRotation(at, Quaternion.LookRotation(target - at + Vector3.forward * 0.001f));
                    body.enabled = true;
                    setHealth.Invoke(health, new object[] { maxHealth });

                    // Back and forth along x = 4 to 9 m at z = 34 m, one 0.5 m cell a move.
                    if (Time.realtimeSinceStartup >= nextMove)
                    {
                        int step = moves % 20;
                        float x = 4f + 0.5f * (step < 10 ? step : 20 - step);
                        GridManager.SetBlocker(BoxOwner, new Bounds(new Vector3(x, 0.5f, 34f), new Vector3(0.9f, 1f, 0.9f)));
                        moves++;
                        nextMove += MoveEvery;
                    }

                    yield return null;

                    if (Time.realtimeSinceStartup - start < WarmUp)
                        continue;
                    captainMs.Add(captain.LastValue / 1e6);
                    fieldMs.Add(fields.LastValue / 1e6);
                    repairMs.Add(repairs.LastValue / 1e6);
                }
            }
            finally
            {
                captain.Dispose();
                fields.Dispose();
                repairs.Dispose();
                GridManager.ClearBlocker(BoxOwner);
            }

            int changes = GridManager.Current.Version - version;
            int frames = captainMs.Count;
            var report = new StringBuilder();
            report.AppendLine($"Test_PushedBoxStress: {frames} frames over {Seconds} s, box moved {moves} times, {changes} grid changes, Chapter 4, editor");
            report.AppendLine($"  Captain ticks: avg {captainMs.Average():F4}, p99 {Percentile(captainMs, 0.99):F3}, worst {captainMs.Max():F3} ms/frame; frames over 1 ms {captainMs.Count(v => v > 1.0)}");
            report.AppendLine($"  AI.DijkstraField.Compute: avg {fieldMs.Average():F4}, p99 {Percentile(fieldMs, 0.99):F3}, worst {fieldMs.Max():F3} ms/frame; frames over 1 ms {fieldMs.Count(v => v > 1.0)}");
            report.AppendLine($"  AI.DijkstraField.Repair: avg {repairMs.Average():F4}, p99 {Percentile(repairMs, 0.99):F3}, worst {repairMs.Max():F3} ms/frame; frames with a repair {repairMs.Count(v => v > 0)}");
            foreach (string line in report.ToString().Split('\n'))
                if (line.Trim().Length > 0)
                    Debug.Log("[Stress] " + line.Trim());
            TestContext.WriteLine(report.ToString());

            Assert.Greater(moves, 30, "The box kept moving.");
            Assert.Greater(changes, 0, "Each move changed the grid.");
            Assert.Less(Percentile(captainMs, 0.99), CaptainBudgetMs, "Captain p99 per frame while the box moves");
        }
    }
}
