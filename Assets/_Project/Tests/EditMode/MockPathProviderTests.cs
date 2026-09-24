using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Mock;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Perception;

namespace ToyFactory.Tests.EditMode
{
    public class MockPathProviderTests
    {
        static readonly Vector3 Start = new Vector3(0f, 0f, 0f);
        static readonly Vector3 Middle = new Vector3(5f, 0f, 0f);
        static readonly Vector3 End = new Vector3(5f, 0f, 5f);

        static readonly List<Vector3> Route = new List<Vector3> { Start, Middle, End };

        static AgentContext ContextAt(Vector3 position) =>
            new AgentContext(Vector2Int.zero, position, Vector3.forward, 0f,
                new WorldBlackboard(), new SensorSnapshot());

        [Test]
        public void FirstTickReturnsAllWaypointsInOrder()
        {
            var brain = new MockPathProvider(Route);

            AgentIntent intent = brain.Tick(ContextAt(Start));

            CollectionAssert.AreEqual(Route, intent.Path);
            Assert.AreEqual(MockPathProvider.DefaultSpeed, intent.DesiredSpeed);
            Assert.AreEqual(AgentAction.None, intent.Action);
        }

        [Test]
        public void TickMidRouteReturnsNullPath()
        {
            var brain = new MockPathProvider(Route);
            brain.Tick(ContextAt(Start));

            AgentIntent intent = brain.Tick(ContextAt(Middle));

            Assert.IsNull(intent.Path);
        }

        [Test]
        public void ReachingTheEndPointResendsTheRoute()
        {
            var brain = new MockPathProvider(Route);
            brain.Tick(ContextAt(Start));
            brain.Tick(ContextAt(Middle));

            AgentIntent intent = brain.Tick(ContextAt(End));

            CollectionAssert.AreEqual(Route, intent.Path);
        }

        [Test]
        public void StandingOnTheEndPointResendsOnlyOnce()
        {
            var brain = new MockPathProvider(Route);
            brain.Tick(ContextAt(Start));
            brain.Tick(ContextAt(Middle));
            brain.Tick(ContextAt(End));

            AgentIntent intent = brain.Tick(ContextAt(End));

            Assert.IsNull(intent.Path);
        }

        [Test]
        public void ArrivalIgnoresHeightDifference()
        {
            var brain = new MockPathProvider(Route);
            brain.Tick(ContextAt(Start));
            brain.Tick(ContextAt(Middle));

            // Capsule pivot one metre above the waypoint, still counts as arrived.
            AgentIntent intent = brain.Tick(ContextAt(End + Vector3.up));

            Assert.IsNotNull(intent.Path);
        }

        [Test]
        public void ChangingTheReturnedPathDoesNotChangeTheRoute()
        {
            var brain = new MockPathProvider(Route);
            AgentIntent first = brain.Tick(ContextAt(Start));
            first.Path.Clear();
            brain.Tick(ContextAt(Middle));

            AgentIntent resent = brain.Tick(ContextAt(End));

            CollectionAssert.AreEqual(Route, resent.Path);
        }

        [Test]
        public void EmptyOrNullWaypointsThrow()
        {
            Assert.Throws<ArgumentException>(() => new MockPathProvider(new List<Vector3>()));
            Assert.Throws<ArgumentNullException>(() => new MockPathProvider(null));
        }
    }
}
