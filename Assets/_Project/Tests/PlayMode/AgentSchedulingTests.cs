using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Movement;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The AI frame budget spreads heavy brain decisions over frames without ever holding a
    /// brain back more than two frames; bodies ease up to speed; and a route changed mid-walk
    /// is joined with a curve instead of a pivot.
    /// </summary>
    public sealed class AgentSchedulingTests
    {
        readonly List<Object> _created = new List<Object>();

        // Each tick costs `costMs` of busy time (a stand-in for a heavy route search) and logs its frame.
        sealed class CostlyBrain : IAgentBrain
        {
            readonly double _costMs;
            public readonly List<int> Frames = new List<int>();
            public List<Vector3> Route;
            public CostlyBrain(double costMs) { _costMs = costMs; }

            public AgentIntent Tick(in AgentContext ctx)
            {
                Frames.Add(Time.frameCount);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (watch.Elapsed.TotalMilliseconds < _costMs) { }
                var intent = new AgentIntent { DesiredSpeed = 4f, Path = Route, DebugState = "Busy" };
                Route = null;   // a route is sent once; null keeps it
                return intent;
            }

            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        [TearDown]
        public void TearDown()
        {
            BrainTickScheduler.FrameBudgetMs = 2f;
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        void Floor()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(5f, 1f, 5f);
            _created.Add(floor);
        }

        AgentController Agent(IAgentBrain brain, Vector3 position, GridGraph grid = null)
        {
            var root = new GameObject("Agent");
            _created.Add(root);
            root.transform.position = position;
            CharacterController body = root.AddComponent<CharacterController>();
            body.center = new Vector3(0f, 0.9f, 0f);
            body.height = 1.8f;
            body.radius = 0.4f;
            AgentController agent = root.AddComponent<AgentController>();
            agent.Initialise(new AgentIdentity(AgentType.Guard, 0), brain, new WorldBlackboard(), grid);
            return agent;
        }

        [UnityTest]
        public IEnumerator HeavyDecisionsAreSpreadOverFramesButNoBrainWaitsLong()
        {
            var brains = new[] { new CostlyBrain(3), new CostlyBrain(3), new CostlyBrain(3) };
            for (int i = 0; i < brains.Length; i++)
                Agent(brains[i], new Vector3(i * 3f, 0f, 0f));
            int waitsBefore = BrainTickScheduler.TotalWaits;

            yield return Frames(30);

            Assert.Greater(BrainTickScheduler.TotalWaits, waitsBefore, "Some ticks waited for the next frame.");
            foreach (CostlyBrain brain in brains)
            {
                Assert.Greater(brain.Frames.Count, 5);
                for (int i = 1; i < brain.Frames.Count; i++)
                    Assert.LessOrEqual(brain.Frames[i] - brain.Frames[i - 1], BrainTickScheduler.MaxWaitFrames + 1,
                        "Never more than two frames late.");
            }

            // Fewer heavy ticks share a frame than without the budget (three every frame).
            var perFrame = new Dictionary<int, int>();
            foreach (CostlyBrain brain in brains)
                foreach (int frame in brain.Frames)
                    perFrame[frame] = perFrame.TryGetValue(frame, out int n) ? n + 1 : 1;
            int crowded = 0;
            foreach (int count in perFrame.Values)
                if (count == 3)
                    crowded++;
            Assert.Less(crowded, perFrame.Count / 2, "Most frames carry fewer than all three heavy decisions.");
        }

        [UnityTest]
        public IEnumerator LightBrainsNeverWait()
        {
            var brains = new[] { new CostlyBrain(0), new CostlyBrain(0), new CostlyBrain(0) };
            for (int i = 0; i < brains.Length; i++)
                Agent(brains[i], new Vector3(i * 3f, 0f, 0f));
            yield return null;
            int waitsBefore = BrainTickScheduler.TotalWaits;

            yield return Frames(20);

            Assert.AreEqual(waitsBefore, BrainTickScheduler.TotalWaits);
            foreach (CostlyBrain brain in brains)
                Assert.GreaterOrEqual(brain.Frames.Count, 20, "Every brain ticks every frame.");
        }

        [UnityTest]
        public IEnumerator BodiesEaseUpToSpeed()
        {
            Floor();
            var root = new GameObject("Walker");
            _created.Add(root);
            root.transform.position = new Vector3(0f, 0.1f, 0f);
            root.AddComponent<CharacterController>();
            AgentPathFollower follower = root.AddComponent<AgentPathFollower>();
            yield return null;

            follower.SetPath(new List<Vector3> { new Vector3(0f, 0f, 20f) }, 4.6f);
            yield return null;
            yield return null;
            Assert.Less(follower.CurrentSpeed, 2f, "Not at full speed straight away.");
            yield return Seconds(0.6f);
            Assert.AreEqual(4.6f, follower.CurrentSpeed, 0.05f, "Up to speed in about 0.4 s.");
        }

        [UnityTest]
        public IEnumerator ARouteChangedMidWalkIsJoinedWithACurve()
        {
            Floor();
            var grid = new GridGraph(80, 80, new Vector3(-20f, 0f, -20f));
            var brain = new CostlyBrain(0) { Route = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(15f, 0f, 0f) } };
            AgentController agent = Agent(brain, new Vector3(0f, 0.1f, 0f), grid);

            yield return Seconds(1f);   // walking east at speed
            float x = agent.transform.position.x;
            Vector3 cell = grid.CellToWorld(grid.WorldToCell(agent.transform.position));
            brain.Route = new List<Vector3> { cell, cell + new Vector3(0f, 0f, 2f), cell + new Vector3(0f, 0f, 12f) };   // now north

            float maxX = x;
            float until = Time.time + 0.3f;
            while (Time.time < until)
            {
                maxX = Mathf.Max(maxX, agent.transform.position.x);
                yield return null;
            }
            Assert.Greater(maxX - x, 0.15f, "Carried on east a little while bending north, instead of pivoting.");
            Assert.Greater(agent.transform.position.z, 0.3f, "And is heading north.");
        }
    }
}
