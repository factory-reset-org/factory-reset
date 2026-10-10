using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    public sealed class AgentHearingTests
    {
        const float Tolerance = 1e-3f;

        sealed class FakeListener : INoiseListener
        {
            public Vector3 HearingPosition { get; set; }
            public bool CanHear { get; set; } = true;
            public readonly List<SensorSnapshot> Heard = new List<SensorSnapshot>();
            public void Hear(in SensorSnapshot heard) => Heard.Add(heard);
        }

        // Cells are 0.5 m; the grid origin is (0, 0, 0), so cell (x, y) is centred at
        // (x * 0.5 + 0.25, 0, y * 0.5 + 0.25).
        static Vector3 CellCentre(int x, int y) => new Vector3(x * 0.5f + 0.25f, 0f, y * 0.5f + 0.25f);

        readonly List<AgentHearing> _created = new List<AgentHearing>();

        AgentHearing Create(IReadOnlyList<INoiseListener> listeners, GridGraph grid)
        {
            var hearing = new AgentHearing(listeners, () => grid);
            _created.Add(hearing);
            return hearing;
        }

        [TearDown]
        public void DisposeHearing()
        {
            foreach (AgentHearing hearing in _created)
                hearing.Dispose();
            _created.Clear();
        }

        [Test]
        public void ListenerInTheOpenHearsTheSourceLevelMinusFourPerMetre()
        {
            var grid = new GridGraph(40, 10, Vector3.zero);
            var listener = new FakeListener { HearingPosition = CellCentre(15, 5) }; // 5 m east
            var hearing = Create(new[] { listener }, grid);

            hearing.Hear(new NoiseEvent(CellCentre(5, 5), 100f, -1, 7.5f));

            Assert.AreEqual(1, listener.Heard.Count);
            SensorSnapshot heard = listener.Heard[0];
            Assert.IsTrue(heard.HasNoise);
            Assert.AreEqual(100f - 4f * 5f, heard.NoiseLevel, Tolerance);
            Assert.AreEqual(CellCentre(5, 5), heard.NoisePosition, "The brain gets the source, not its own position.");
            Assert.AreEqual(-1, heard.NoiseSourceId);
            Assert.AreEqual(7.5f, heard.NoiseTime);
            Assert.IsFalse(heard.NoiseIsLure);
        }

        [Test]
        public void ALureNoiseReachesTheListenerMarkedAsALure()
        {
            var grid = new GridGraph(40, 10, Vector3.zero);
            var listener = new FakeListener { HearingPosition = CellCentre(15, 5) };
            var hearing = Create(new[] { listener }, grid);

            hearing.Hear(new NoiseEvent(CellCentre(5, 5), 70f, 9, 1f, isLure: true));

            Assert.AreEqual(1, listener.Heard.Count);
            Assert.IsTrue(listener.Heard[0].NoiseIsLure, "The thrown toy's tick stays a lure all the way to the brain.");
        }

        [Test]
        public void ClosedDoorCostsThirtyFiveMore()
        {
            // A wall across x = 10 with one door cell at (10, 5), closed.
            var grid = new GridGraph(30, 11, Vector3.zero);
            for (int y = 0; y < 11; y++)
                if (y != 5)
                    grid.SetWalkable(new Vector2Int(10, y), false);
            grid.SetDoorway(new Vector2Int(10, 5), true);
            grid.SetDoor(new Vector2Int(10, 5), 1, true);

            var listener = new FakeListener { HearingPosition = CellCentre(15, 5) };
            var hearing = Create(new[] { listener }, grid);

            hearing.Hear(new NoiseEvent(CellCentre(5, 5), 100f, 3, 0f));

            Assert.AreEqual(1, listener.Heard.Count);
            Assert.AreEqual(100f - 4f * 5f - 35f, listener.Heard[0].NoiseLevel, Tolerance);
        }

        [Test]
        public void ListenerOutOfRangeHearsNothing()
        {
            // Footsteps (25) carry (25 - 10) / 4 = 3.75 m; the listener is 5 m away.
            var grid = new GridGraph(40, 10, Vector3.zero);
            var listener = new FakeListener { HearingPosition = CellCentre(15, 5) };
            var hearing = Create(new[] { listener }, grid);

            hearing.Hear(new NoiseEvent(CellCentre(5, 5), 25f, -1, 0f));

            Assert.AreEqual(0, listener.Heard.Count);
        }

        [Test]
        public void ListenerThatCannotHearIsSkipped()
        {
            var grid = new GridGraph(40, 10, Vector3.zero);
            var knockedOut = new FakeListener { HearingPosition = CellCentre(8, 5), CanHear = false };
            var awake = new FakeListener { HearingPosition = CellCentre(9, 5) };
            var hearing = Create(new INoiseListener[] { knockedOut, awake }, grid);

            hearing.Hear(new NoiseEvent(CellCentre(5, 5), 100f, -1, 0f));

            Assert.AreEqual(0, knockedOut.Heard.Count);
            Assert.AreEqual(1, awake.Heard.Count);
        }

        [Test]
        public void WithoutAGridNothingIsHeard()
        {
            var listener = new FakeListener { HearingPosition = Vector3.zero };
            var hearing = Create(new[] { listener }, null);

            hearing.Hear(new NoiseEvent(Vector3.zero, 100f, -1, 0f));

            Assert.AreEqual(0, listener.Heard.Count);
        }

        [Test]
        public void EmittedNoiseReachesListenersUntilDisposed()
        {
            var grid = new GridGraph(40, 10, Vector3.zero);
            var listener = new FakeListener { HearingPosition = CellCentre(10, 5) };
            AgentHearing hearing = Create(new[] { listener }, grid);

            NoiseEvents.Emit(new NoiseEvent(CellCentre(5, 5), 60f, 2, 1f));
            Assert.AreEqual(1, listener.Heard.Count, "Subscribed to NoiseEvents.");

            hearing.Dispose();
            NoiseEvents.Emit(new NoiseEvent(CellCentre(5, 5), 60f, 2, 2f));
            Assert.AreEqual(1, listener.Heard.Count, "A disposed hearing no longer listens.");
        }

        [Test]
        public void ListenersAddedLaterAreHeard()
        {
            var grid = new GridGraph(40, 10, Vector3.zero);
            var listeners = new List<INoiseListener>();
            var hearing = Create(listeners, grid);

            var late = new FakeListener { HearingPosition = CellCentre(8, 5) };
            listeners.Add(late);
            hearing.Hear(new NoiseEvent(CellCentre(5, 5), 100f, -1, 0f));

            Assert.AreEqual(1, late.Heard.Count, "The spawner passes its live agent list.");
        }

        // ---- AgentController: keeps the loudest noise and hands it to the brain once ----

        sealed class RecordingBrain : IAgentBrain
        {
            public readonly List<SensorSnapshot> Senses = new List<SensorSnapshot>();
            public AgentIntent Tick(in AgentContext ctx)
            {
                Senses.Add(ctx.Senses);
                return default;
            }
            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        [UnityTest]
        public IEnumerator ControllerPassesTheLoudestNoiseToTheBrainOnce()
        {
            var body = new GameObject("HearingAgent");
            try
            {
                body.AddComponent<CharacterController>();
                AgentController agent = body.AddComponent<AgentController>();
                var brain = new RecordingBrain();
                agent.Initialise(new AgentIdentity(AgentType.Tracker, 0), brain, new WorldBlackboard());

                agent.Hear(new SensorSnapshot(Vector3.zero, 40f, 1, 1f));
                agent.Hear(new SensorSnapshot(Vector3.one, 75f, 2, 2f));
                agent.Hear(new SensorSnapshot(Vector3.zero, 50f, 3, 3f));
                yield return null; // one Update: the brain ticks once
                yield return null; // and again, with nothing new heard

                Assert.GreaterOrEqual(brain.Senses.Count, 2);
                Assert.IsTrue(brain.Senses[0].HasNoise);
                Assert.AreEqual(75f, brain.Senses[0].NoiseLevel, "Several noises between ticks keep the loudest.");
                Assert.AreEqual(2, brain.Senses[0].NoiseSourceId);
                Assert.IsFalse(brain.Senses[1].HasNoise, "Each noise reaches the brain once.");
            }
            finally
            {
                Object.Destroy(body);
            }
        }

        [Test]
        public void KnockedOutControllerCannotHear()
        {
            var body = new GameObject("StunnedAgent");
            try
            {
                body.AddComponent<CharacterController>();
                AgentController agent = body.AddComponent<AgentController>();
                agent.Initialise(new AgentIdentity(AgentType.Guard, 1), new RecordingBrain(), new WorldBlackboard());

                Assert.IsTrue(agent.CanHear);
                agent.Disable(5f);
                Assert.IsFalse(agent.CanHear);
            }
            finally
            {
                Object.DestroyImmediate(body);
            }
        }
    }
}
