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
    /// The Saboteur's swipe hurts the player once per move, at the moment of contact, and only
    /// if the player is alive, within reach and in front. The Tracker's pounce deals 8 the
    /// same way; set to 0 it is cosmetic.
    /// </summary>
    public sealed class AgentMeleeDamageTests
    {
        readonly List<Object> _created = new List<Object>();

        // Walks onto a fixed point once (as a chase does), or stands and asks to attack.
        sealed class TestBrain : IAgentBrain
        {
            readonly Vector3? _goal;
            bool _sent;
            public bool Attack;
            public bool Ignore;   // busy with something else: AgentIntent.IgnorePlayer
            public TestBrain(Vector3? goal) { _goal = goal; }

            public AgentIntent Tick(in AgentContext ctx)
            {
                var intent = new AgentIntent
                {
                    Action = Attack ? AgentAction.Shoot : AgentAction.None,
                    DebugState = "Chase",
                    IgnorePlayer = Ignore
                };
                if (_goal.HasValue && !_sent)
                {
                    _sent = true;
                    intent.Path = new List<Vector3> { _goal.Value };
                    intent.DesiredSpeed = 4f;
                }
                return intent;
            }

            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        sealed class CountingPlayer : IPlayerState
        {
            public readonly List<(float amount, int source)> Damage = new List<(float, int)>();
            public Vector3 Position { get; set; }
            public bool Alive = true;
            public Vector3 Velocity => Vector3.zero;
            public Vector3 Forward => Vector3.forward;
            public float SprintSpeed => 8.6f;
            public bool IsAlive => Alive;
            public float HealthFraction => 1f;
            public float AmmoFraction => 1f;
            public bool IsReloading => false;
            public float OverchargeTimeLeft => 0f;
            public float LastShotTime => -1f;
            public void TakeDamage(float amount, int sourceAgentId) => Damage.Add((amount, sourceAgentId));
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

        void Floor()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(5f, 1f, 5f);
            _created.Add(floor);
        }

        T Body<T>(AgentType type, int id, TestBrain brain, out AgentController agent) where T : AgentMeleeMove
        {
            var root = new GameObject(type.ToString());
            _created.Add(root);
            root.SetActive(false);
            root.transform.position = new Vector3(0f, 0.1f, 0f);
            CharacterController body = root.AddComponent<CharacterController>();
            body.center = new Vector3(0f, 0.6f, 0f);
            body.height = 1.2f;
            body.radius = 0.4f;
            agent = root.AddComponent<AgentController>();
            root.AddComponent<PlayerStandOff>();
            T move = root.AddComponent<T>();
            root.SetActive(true);
            agent.Initialise(new AgentIdentity(type, id, 0), brain, new WorldBlackboard());
            return move;
        }

        [UnityTest]
        public IEnumerator EachSwipeHurtsThePlayerOnceAtTheMomentOfContact()
        {
            Floor();
            var player = new CountingPlayer { Position = new Vector3(0f, 0f, 3f) };
            PlayerState.Publish(player);
            AgentClawSwipe swipe = Body<AgentClawSwipe>(AgentType.Saboteur, 2, new TestBrain(player.Position), out AgentController agent);

            float until = Time.time + 3f;
            while (swipe.Strikes == 0 && Time.time < until)
                yield return null;
            Assert.AreEqual(1, swipe.Strikes);
            Assert.IsEmpty(player.Damage, "Nothing in the wind-up: the raised arm is the warning.");

            yield return Seconds(0.45f);   // past contact (0.3 s into the 0.6 s move)
            CollectionAssert.AreEqual(new[] { (10f, agent.Identity.Id) }, player.Damage, "One swipe, 10 damage, from this Saboteur.");

            yield return Seconds(2.6f);
            Assert.That(swipe.Strikes, Is.InRange(3, 4));
            Assert.AreEqual(swipe.Hits, player.Damage.Count, "Every swipe that connected counted once.");
            Assert.GreaterOrEqual(swipe.Hits, swipe.Strikes - 1, "All but a swipe still winding up connected.");
        }

        [UnityTest]
        public IEnumerator TheTrackersPounceDealsEightAndZeroMakesItCosmetic()
        {
            Floor();
            var player = new CountingPlayer { Position = new Vector3(0f, 0f, 3f) };
            PlayerState.Publish(player);
            AgentBite bite = Body<AgentBite>(AgentType.Tracker, 0, new TestBrain(player.Position), out AgentController agent);

            yield return Seconds(1.5f);
            Assert.AreEqual(8f, bite.Damage);
            Assert.GreaterOrEqual(bite.Hits, 1, "The pounce lands.");
            Assert.AreEqual((8f, agent.Identity.Id), player.Damage[0]);

            int hitsSoFar = player.Damage.Count;
            bite.GetType().GetField("damage", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(bite, 0f);
            yield return Seconds(1.5f);
            Assert.AreEqual(hitsSoFar, player.Damage.Count, "At 0 the same pounce is cosmetic.");
        }

        [UnityTest]
        public IEnumerator ABrainIgnoringThePlayerStopsTheHoldAndThePounces()
        {
            Floor();
            var player = new CountingPlayer { Position = new Vector3(0f, 0f, 3f) };
            PlayerState.Publish(player);
            var brain = new TestBrain(player.Position);
            AgentBite bite = Body<AgentBite>(AgentType.Tracker, 0, brain, out AgentController agent);
            var standOff = agent.GetComponent<PlayerStandOff>();

            yield return Seconds(1.5f);
            Assert.IsTrue(standOff.IsHolding, "Engaged: held in front of the player.");
            Assert.GreaterOrEqual(bite.Strikes, 1, "Engaged: it pounces.");

            // The Tracker watching a thrown toy: its brain says it is busy with something else.
            brain.Ignore = true;
            yield return null;
            yield return null;
            int strikes = bite.Strikes;
            yield return Seconds(2f);

            Assert.IsTrue(agent.IgnoresPlayer);
            Assert.IsFalse(standOff.IsHolding, "No longer held facing the player.");
            Assert.AreEqual(strikes, bite.Strikes, "No new pounce while it ignores the player.");
        }

        [UnityTest]
        public IEnumerator APlayerWhoBacksOffDuringTheWindUpIsNotHit()
        {
            Floor();
            var player = new CountingPlayer { Position = new Vector3(0f, 0f, 3f) };
            PlayerState.Publish(player);
            AgentClawSwipe swipe = Body<AgentClawSwipe>(AgentType.Saboteur, 0, new TestBrain(player.Position), out _);

            float until = Time.time + 3f;
            while (swipe.Strikes == 0 && Time.time < until)
                yield return null;
            player.Position = new Vector3(0f, 0f, 9f);   // stepped back out of reach

            yield return Seconds(0.5f);
            Assert.AreEqual(1, swipe.Strikes);
            Assert.IsEmpty(player.Damage, "Dodged.");
        }

        [UnityTest]
        public IEnumerator AnAttackFromTooFarBehindOrAtADeadPlayerWhiffs()
        {
            Floor();
            var player = new CountingPlayer { Position = new Vector3(0f, 0f, 6f) };
            PlayerState.Publish(player);
            var brain = new TestBrain(null);
            AgentClawSwipe swipe = Body<AgentClawSwipe>(AgentType.Saboteur, 1, brain, out _);

            // The brain's attack starts a swipe wherever the player is.
            IEnumerator SwipeOnce()
            {
                int before = swipe.Strikes;
                brain.Attack = true;
                float until = Time.time + 1f;
                while (swipe.Strikes == before && Time.time < until)
                    yield return null;
                yield return Seconds(0.7f);
                brain.Attack = false;
                yield return null;
                yield return null;
            }

            yield return SwipeOnce();
            Assert.AreEqual(1, swipe.Strikes);
            Assert.IsEmpty(player.Damage, "6 m away: out of reach.");

            player.Position = new Vector3(0f, 0f, -1.8f);   // within reach, but behind
            yield return SwipeOnce();
            Assert.AreEqual(2, swipe.Strikes);
            Assert.IsEmpty(player.Damage, "Behind the agent.");

            player.Position = new Vector3(0f, 0f, 1.8f);
            player.Alive = false;
            yield return SwipeOnce();
            Assert.AreEqual(3, swipe.Strikes);
            Assert.IsEmpty(player.Damage, "Already dead.");

            player.Alive = true;
            yield return SwipeOnce();
            Assert.AreEqual(1, player.Damage.Count, "In front and in reach: hit.");
        }
    }
}
