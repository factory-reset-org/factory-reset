using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Animation;

namespace ToyFactory.Tests
{
    public sealed class AgentAnimationTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        sealed class FakeWindUp : IWindUpState
        {
            public float Energy01 { get; set; }
            public bool IsRewinding { get; set; }
        }

        // Walks once along a straight path, then keeps following it.
        sealed class StraightLineBrain : IAgentBrain
        {
            readonly List<Vector3> _path;
            bool _sent;
            public StraightLineBrain(Vector3 from, Vector3 to) { _path = new List<Vector3> { from, to }; }
            public AgentIntent Tick(in AgentContext ctx)
            {
                if (_sent)
                    return new AgentIntent { DesiredSpeed = 2f };
                _sent = true;
                return new AgentIntent { Path = _path, DesiredSpeed = 2f };
            }
            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        [Test]
        public void OneTurnOfAWheelCoversItsCircumference()
        {
            Assert.AreEqual(360f, WheelSpinner.DegreesFor(2f * Mathf.PI * 0.5f, 0.5f), 1e-3f);
            Assert.AreEqual(Mathf.Rad2Deg, WheelSpinner.DegreesFor(0.365f, 0.365f), 1e-3f, "Rolling one radius turns one radian.");
        }

        [Test]
        public void KeySlowsAsTheToyRunsDownAndSpinsBackWhileRewinding()
        {
            Assert.AreEqual(90f, WindUpKeySpinner.RateFor(new FakeWindUp { Energy01 = 0.5f }, 180f, -720f), 1e-4f);
            Assert.AreEqual(0f, WindUpKeySpinner.RateFor(new FakeWindUp { Energy01 = 0f }, 180f, -720f), 1e-4f);
            Assert.AreEqual(-720f, WindUpKeySpinner.RateFor(new FakeWindUp { IsRewinding = true }, 180f, -720f));
            Assert.AreEqual(180f, WindUpKeySpinner.RateFor(null, 180f, -720f), "No wind-up energy: a steady key.");
        }

        [UnityTest]
        public IEnumerator WheelsRollWithoutSlippingAsTheAgentMoves()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(ground);
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);

            var body = new GameObject("WheeledAgent");
            _created.Add(body);
            body.SetActive(false);
            var capsule = body.AddComponent<CharacterController>();
            capsule.radius = 0.3f;
            capsule.height = 1f;
            capsule.center = new Vector3(0f, 0.5f, 0f);
            AgentController agent = body.AddComponent<AgentController>();

            var pivot = new GameObject("Wheel_Pivot").transform;
            pivot.SetParent(body.transform, false);
            WheelSpinner spinner = body.AddComponent<WheelSpinner>();
            const float radius = 2f;   // a big wheel keeps the total turn under 180° for the angle check
            var wheel = new WheelSpinner.Wheel { pivot = pivot, radius = radius, localAxis = Vector3.right };
            typeof(WheelSpinner).GetField("wheels", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(spinner, new[] { wheel });
            body.transform.position = new Vector3(0f, 0.05f, 0f);
            body.SetActive(true);

            agent.Initialise(new AgentIdentity(AgentType.Saboteur, 0), new StraightLineBrain(Vector3.zero, new Vector3(10f, 0f, 0f)),
                new WorldBlackboard());

            Vector3 start = body.transform.position;
            float giveUp = Time.time + 5f;
            while (FlatDistance(start, body.transform.position) < 2.5f && Time.time < giveUp)
                yield return null;

            float rolled = FlatDistance(start, body.transform.position);
            Assert.Greater(rolled, 2f, "The agent moved.");
            float turned = Quaternion.Angle(Quaternion.identity, pivot.localRotation);
            Assert.AreEqual(WheelSpinner.DegreesFor(rolled, radius), turned, 4f,
                "The wheel turned by distance / radius: no slipping.");
        }

        [Test]
        public void BridgeWithoutAnAnimatorTurnsItselfOff()
        {
            var body = new GameObject("NoAnimator");
            _created.Add(body);
            body.AddComponent<CharacterController>();
            body.AddComponent<AgentController>();
            LogAssert.Expect(LogType.Warning, new Regex("no Animator with a controller"));

            AgentAnimatorBridge bridge = body.AddComponent<AgentAnimatorBridge>();

            Assert.IsFalse(bridge.enabled);
        }

        [Test]
        public void ControllerExposesTheBrainsWindUpEnergyUntilTheBrainGoes()
        {
            var body = new GameObject("Tracker");
            _created.Add(body);
            body.AddComponent<CharacterController>();
            AgentController agent = body.AddComponent<AgentController>();

            agent.Initialise(new AgentIdentity(AgentType.Tracker, 0), new WindUpBrain(), new WorldBlackboard());
            Assert.IsNotNull(agent.WindUp);
            Assert.AreEqual(0.25f, agent.WindUp.Energy01);

            agent.Scrap();
            Assert.IsNull(agent.WindUp);
        }

        sealed class WindUpBrain : IAgentBrain, IWindUpState
        {
            public float Energy01 => 0.25f;
            public bool IsRewinding => false;
            public AgentIntent Tick(in AgentContext ctx) => default;
            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
