using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Animation;
using ToyFactory.Runtime.Movement;

namespace ToyFactory.Tests
{
    /// <summary>
    /// A chasing body stops short of the player and faces them instead of driving into them,
    /// walks on when the player backs off, and the Tracker's pounce plays while it is held.
    /// </summary>
    public sealed class AgentStandOffTests
    {
        readonly List<Object> _created = new List<Object>();

        // Plans one route onto a fixed point, as a chase plans onto the player's cell, then keeps it.
        sealed class RouteBrain : IAgentBrain
        {
            readonly Vector3 _goal;
            bool _sent;
            public RouteBrain(Vector3 goal) { _goal = goal; }

            public AgentIntent Tick(in AgentContext ctx)
            {
                if (_sent)
                    return new AgentIntent { DebugState = "Chase" };
                _sent = true;
                return new AgentIntent { Path = new List<Vector3> { _goal }, DesiredSpeed = 4f, DebugState = "Chase" };
            }

            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        sealed class FakePlayer : IPlayerState
        {
            public Vector3 Position { get; set; }
            public Vector3 Velocity => Vector3.zero;
            public Vector3 Forward => Vector3.forward;
            public float SprintSpeed => 8.6f;
            public bool IsAlive => true;
            public float HealthFraction => 1f;
            public float AmmoFraction => 1f;
            public bool IsReloading => false;
            public float OverchargeTimeLeft => 0f;
            public float LastShotTime => -1f;
            public void TakeDamage(float amount, int sourceAgentId) { }
        }

        [TearDown]
        public void TearDown()
        {
            PlayerState.Publish(null);
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        void Floor()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(5f, 1f, 5f);
            _created.Add(floor);
        }

        AgentController Tracker(Vector3 goal, bool bites, out Transform head)
        {
            var root = new GameObject("Tracker");
            _created.Add(root);
            root.SetActive(false);
            root.transform.position = new Vector3(0f, 0.1f, 0f);
            CharacterController body = root.AddComponent<CharacterController>();
            body.center = new Vector3(0f, 0.6f, 0f);
            body.height = 1.2f;
            body.radius = 0.4f;
            AgentController agent = root.AddComponent<AgentController>();
            root.AddComponent<PlayerStandOff>();

            head = new GameObject("Head_Pivot").transform;
            head.SetParent(root.transform, false);
            if (bites)
            {
                AgentBite bite = root.AddComponent<AgentBite>();
                SetField(bite, "head", head);
            }

            root.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Tracker, 0, 0), new RouteBrain(goal), new WorldBlackboard());
            return agent;
        }

        [UnityTest]
        public IEnumerator AChaseStopsShortOfThePlayerFacingThemThenWalksOnWhenTheyBackOff()
        {
            Floor();
            var player = new FakePlayer { Position = new Vector3(0f, 0f, 6f) };
            PlayerState.Publish(player);
            AgentController agent = Tracker(new Vector3(0f, 0f, 6f), false, out _);
            PlayerStandOff standOff = agent.GetComponent<PlayerStandOff>();

            yield return Seconds(2.5f);

            float gap = 6f - agent.transform.position.z;
            Assert.IsTrue(standOff.IsHolding, "Held in front of the player.");
            Assert.That(gap, Is.InRange(1.2f, 1.75f), "Stopped about 1.4 m short, not on top of the player.");
            Assert.Less(agent.Speed, 0.05f, "Standing still, so the animation is Idle, not Run.");
            Assert.Less(Vector3.Angle(agent.transform.forward, Vector3.forward), 5f, "Facing the player.");

            player.Position = new Vector3(0f, 0f, 12f);
            yield return Seconds(0.5f);
            Assert.IsFalse(standOff.IsHolding, "Walks on once the player is out of reach.");
            Assert.Greater(agent.transform.position.z, 6f - gap + 0.5f, "Carried on along the same route.");
        }

        [UnityTest]
        public IEnumerator TheSaboteurSwipesWithAlternatingArmsWhileItIsHeld()
        {
            Floor();
            PlayerState.Publish(new FakePlayer { Position = new Vector3(0f, 0f, 3f) });
            AgentController agent = Tracker(new Vector3(0f, 0f, 3f), false, out _);
            agent.gameObject.SetActive(false);
            AgentClawSwipe swipe = agent.gameObject.AddComponent<AgentClawSwipe>();
            agent.gameObject.SetActive(true);

            yield return Seconds(1f);
            Assert.GreaterOrEqual(swipe.Strikes, 1, "Swipes as soon as it is held in front of the player.");
            bool firstArm = swipe.RightArm;
            int first = swipe.Strikes;
            float until = Time.time + 3f;
            while (swipe.Strikes == first && Time.time < until)
                yield return null;
            Assert.Greater(swipe.Strikes, first, "Swipes again while still held.");
            Assert.AreNotEqual(firstArm, swipe.RightArm, "The next swipe uses the other arm.");
        }

        [UnityTest]
        public IEnumerator TheTrackerPouncesEveryFewSecondsOnlyWhileItIsHeld()
        {
            Floor();
            var player = new FakePlayer { Position = new Vector3(0f, 0f, 3f) };
            PlayerState.Publish(player);
            AgentController agent = Tracker(new Vector3(0f, 0f, 3f), true, out _);
            AgentBite bite = agent.GetComponent<AgentBite>();

            yield return Seconds(1f);
            Assert.IsTrue(agent.GetComponent<PlayerStandOff>().IsHolding);
            int first = bite.Strikes;
            Assert.GreaterOrEqual(first, 1, "The first pounce comes as soon as it reaches the player.");

            yield return Seconds(2.7f);
            Assert.That(bite.Strikes - first, Is.InRange(2, 3), "One pounce every 1.3 s.");

            player.Position = new Vector3(0f, 0f, 30f);
            yield return Seconds(0.5f);
            int afterRelease = bite.Strikes;
            yield return Seconds(1.5f);
            Assert.AreEqual(afterRelease, bite.Strikes, "No pouncing while it walks.");
            Assert.IsFalse(bite.IsStriking);
        }
    }
}
