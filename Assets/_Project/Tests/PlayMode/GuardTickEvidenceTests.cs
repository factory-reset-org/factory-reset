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
using ToyFactory.AI.Agents.Guard;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Debugging;
using ToyFactory.Runtime.Agents;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Evidence for the performance log: where the Guard's tick time goes. It runs the scenario
    /// of <see cref="FourAgentsStressTests"/> (Bootstrap, Chapter 4, the player walking a loop at
    /// 4 m/s for 30 s), but round the Painting room, because the Guard fights only for its own
    /// room. It breaks every slow Guard frame down by the markers
    /// inside <see cref="GuardBrain"/>, the two search markers, the line-of-sight checks made
    /// and the Guard's state.
    /// <para>Explicit, so it only runs when asked for (about 40 s). It reports and never fails
    /// on timing.</para>
    /// </summary>
    [Explicit("Evidence run: about 40 s. Run it on its own for the performance log.")]
    [Category("Evidence")]
    public sealed class GuardTickEvidenceTests
    {
        const float WarmUp = 3f;
        const float Measure = 30f;
        const float PlayerSpeed = 4f;
        const double SlowMs = 1.0;

        static readonly string[] Markers =
        {
            "AI.Brain.Tick.Guard",
            "AI.Guard.Perceive", "AI.Guard.EvaluateCover", "AI.Guard.FindBest", "AI.Guard.PathCost",
            "AI.Guard.States", "AI.Guard.MoveTo", "AI.Guard.Retreat",
            "AI.AStarSearch.FindPath", "AI.OneToOneCost.Compute",
        };

        // A loop round the Painting room, the Guard's own room: it fights only for that room, so
        // the evidence has to be taken there. Waypoints keep clear of the paint tanks.
        static readonly Vector3[] Loop =
        {
            new Vector3(23f, 0.1f, 8f), new Vector3(29f, 0.1f, 5f), new Vector3(36f, 0.1f, 9f),
            new Vector3(38f, 0.1f, 13f), new Vector3(33f, 0.1f, 18.5f), new Vector3(27f, 0.1f, 15.5f),
            new Vector3(23f, 0.1f, 11f),
        };

        struct Frame
        {
            public double[] Ms;       // one per marker
            public int SightChecks;   // line-of-sight checks made this frame
            public int CellsTested;   // by the latest cover search
            public string State;
            public float Distance;    // Guard to player, metres
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene empty = SceneManager.CreateScene("Empty after guard evidence test");
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

        [UnityTest]
        public IEnumerator Test_GuardTickBreakdown()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && Object.FindAnyObjectByType<ChapterJump>() != null && AgentSpawner.Instance != null
                && AgentSpawner.Instance.SpawnedAgents.Count == 7 && PlayerState.Current != null,
                30f, "Bootstrap to load");
            ChapterJump jump = Object.FindAnyObjectByType<ChapterJump>();
            jump.JumpTo(4);
            yield return Until(() => !jump.IsJumping, 20f, "the jump to Chapter 4");

            AgentController guard = AgentSpawner.Instance.SpawnedAgents.First(a => a.Brain is GuardBrain);
            var brain = (GuardBrain)guard.Brain;

            var player = (Component)PlayerState.Current;
            var body = player.GetComponent<CharacterController>();
            Component health = player.GetComponents<Component>().First(c => c.GetType().Name == "PlayerHealth");
            MethodInfo setHealth = health.GetType().GetProperty("Current").GetSetMethod(true);
            float maxHealth = (float)health.GetType().GetField("maxHealth", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(health);

            ProfilerRecorder[] recorders = Markers.Select(m => ProfilerRecorder.StartNew(ProfilerCategory.Scripts, m, 1)).ToArray();
            var frames = new List<Frame>();
            int leg = 0;
            Vector3 at = Loop[0];
            int checksBefore = brain.SightChecksMade;
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

                    int checksNow = brain.SightChecksMade;
                    int checks = checksNow - checksBefore;
                    checksBefore = checksNow;
                    if (Time.realtimeSinceStartup - start < WarmUp)
                        continue;

                    Vector3 toPlayer = player.transform.position - guard.transform.position;
                    toPlayer.y = 0f;
                    frames.Add(new Frame
                    {
                        Ms = recorders.Select(r => r.LastValue / 1e6).ToArray(),
                        SightChecks = checks,
                        CellsTested = brain.CoverCellsTested,
                        State = brain.StateName,
                        Distance = toPlayer.magnitude,
                    });
                }
            }
            finally
            {
                foreach (ProfilerRecorder recorder in recorders)
                    recorder.Dispose();
            }

            Debug.Log(Report(frames));
            Assert.Greater(frames.Count, 0);
        }

        static string Report(List<Frame> frames)
        {
            var report = new StringBuilder();
            List<Frame> slow = frames.Where(f => f.Ms[0] > SlowMs).ToList();
            List<double> ticks = frames.Select(f => f.Ms[0]).ToList();

            report.AppendLine($"[GuardTick] Test_GuardTickBreakdown: {frames.Count} frames over {Measure} s, Chapter 4, editor");
            report.AppendLine($"[GuardTick] Guard tick ms/frame: avg {ticks.Average():F4}, p99 {Percentile(ticks, 0.99):F3}, worst {ticks.Max():F3}; frames over {SlowMs} ms: {slow.Count}");
            report.AppendLine($"[GuardTick] Engaged in {frames.Count(f => f.State != "Patrol")} frames; sight checks total {frames.Sum(f => (long)f.SightChecks)}, in slow frames {slow.Sum(f => (long)f.SightChecks)}");

            report.AppendLine("[GuardTick] Per marker, all frames: avg ms/frame, worst, frames over 0.5 ms | in the slow frames: avg ms, share of the Guard tick");
            double slowTick = slow.Sum(f => f.Ms[0]);
            for (int i = 0; i < Markers.Length; i++)
            {
                int index = i;
                List<double> all = frames.Select(f => f.Ms[index]).ToList();
                double slowAvg = slow.Count > 0 ? slow.Average(f => f.Ms[index]) : 0;
                double share = slowTick > 0 ? slow.Sum(f => f.Ms[index]) / slowTick * 100.0 : 0;
                report.AppendLine($"[GuardTick] {Markers[i]}: avg {all.Average():F4}, worst {all.Max():F3}, over 0.5 ms {all.Count(v => v > 0.5)} | slow avg {slowAvg:F3}, {share:F0}%");
            }

            report.AppendLine("[GuardTick] Slow frames by state: " + string.Join(", ",
                slow.GroupBy(f => f.State).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
            if (slow.Count > 0)
            {
                report.AppendLine($"[GuardTick] Slow frames: sight checks avg {slow.Average(f => f.SightChecks):F0}, worst {slow.Max(f => f.SightChecks)}; " +
                                  $"cover cells tested avg {slow.Average(f => f.CellsTested):F0}, worst {slow.Max(f => f.CellsTested)}; " +
                                  $"Guard to player avg {slow.Average(f => f.Distance):F1} m");
                // What the largest single cause is in each slow frame.
                report.AppendLine("[GuardTick] Slow frames by biggest part: " + string.Join(", ",
                    slow.GroupBy(BiggestPart).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
            }

            report.AppendLine("[GuardTick] The 10 slowest frames (ms): tick | FindBest PathCost MoveTo Retreat | A* OneToOne | sight checks, cells tested, state, distance");
            foreach (Frame f in frames.OrderByDescending(f => f.Ms[0]).Take(10))
                report.AppendLine($"[GuardTick] {f.Ms[0]:F2} | {f.Ms[3]:F2} {f.Ms[4]:F2} {f.Ms[6]:F2} {f.Ms[7]:F2} | {f.Ms[8]:F2} {f.Ms[9]:F2} | {f.SightChecks}, {f.CellsTested}, {f.State}, {f.Distance:F1} m");
            return report.ToString();
        }

        // FindBest, PathCost, MoveTo and Retreat do not overlap each other, except that Retreat
        // contains its own PathCost calls; whichever is largest names the frame.
        static string BiggestPart(Frame f)
        {
            double findBest = f.Ms[3], pathCost = f.Ms[4], moveTo = f.Ms[6], retreat = f.Ms[7];
            double other = f.Ms[0] - findBest - moveTo - Math.Max(pathCost, retreat);
            double max = Math.Max(Math.Max(findBest, pathCost), Math.Max(Math.Max(moveTo, retreat), other));
            if (max == retreat && retreat > 0) return "Retreat";
            if (max == findBest) return "FindBest";
            if (max == pathCost) return "PathCost";
            if (max == moveTo) return "MoveTo";
            return "other";
        }
    }
}
