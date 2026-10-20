using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Movement;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The body honours the brain's LookTarget while it stands (a Captain in ambush faces the
    /// way the player will come) and an idle weapon does not cancel that, faces where it walks
    /// while it walks, and a new route does not send it back to the centre of the cell it is
    /// already leaving.
    /// </summary>
    public sealed class AgentFacingTests
    {
        readonly List<Object> _created = new List<Object>();

        // Sends one route (or a stop) on its first tick, then keeps it; always looks at Look.
        sealed class LookBrain : IAgentBrain
        {
            readonly List<Vector3> _route;
            bool _sent;
            public Vector3? Look;

            public LookBrain(List<Vector3> route) { _route = route; }

            public AgentIntent Tick(in AgentContext ctx)
            {
                var intent = new AgentIntent { LookTarget = Look, DesiredSpeed = 3f, DebugState = "Ambush" };
                if (!_sent)
                {
                    _sent = true;
                    intent.Path = _route;
                }
                return intent;
            }

            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        [TearDown]
        public void TearDown()
        {
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

        AgentController Agent(LookBrain brain, Vector3 position)
        {
            var root = new GameObject("Agent");
            _created.Add(root);
            root.transform.position = position;   // facing +Z
            CharacterController body = root.AddComponent<CharacterController>();
            body.center = new Vector3(0f, 0.9f, 0f);
            body.height = 1.8f;
            body.radius = 0.4f;
            AgentController agent = root.AddComponent<AgentController>();
            agent.Initialise(new AgentIdentity(AgentType.Captain, 0), brain, new WorldBlackboard());
            return agent;
        }

        [UnityTest]
        public IEnumerator AStandingAgentTurnsToFaceItsLookTarget()
        {
            Floor();
            var brain = new LookBrain(new List<Vector3>()) { Look = new Vector3(6f, 0f, 0.1f) };   // stop, look east
            AgentController agent = Agent(brain, new Vector3(0f, 0.1f, 0f));

            yield return Seconds(0.6f);   // 90 degrees at 360 deg/s
            Assert.Less(Vector3.Angle(agent.transform.forward, Vector3.right), 3f, "Faces where the brain looks.");

            brain.Look = null;
            Quaternion held = agent.transform.rotation;
            yield return Seconds(0.3f);
            Assert.Less(Quaternion.Angle(held, agent.transform.rotation), 0.5f, "With no look target it keeps its facing.");
        }

        [UnityTest]
        public IEnumerator AnIdleWeaponLeavesTheBodysFacingAlone()
        {
            // The weapon used to stop the facing on every idle frame. Depending on which script
            // ran first, a Captain guarding the console stood with its back to the way the
            // player would come.
            Floor();
            var brain = new LookBrain(new List<Vector3>());
            AgentController agent = Agent(brain, new Vector3(0f, 0.1f, 0f));
            agent.gameObject.AddComponent<AgentWeapon>();
            yield return null;

            agent.Follower.FaceTowards(new Vector3(6f, 0f, 0.1f));
            yield return Seconds(0.6f);
            Assert.Less(Vector3.Angle(agent.transform.forward, Vector3.right), 3f, "Still turned to face it.");
        }

        [UnityTest]
        public IEnumerator AWalkingAgentFacesWhereItWalksNotItsLookTarget()
        {
            Floor();
            var route = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 8f) };
            var brain = new LookBrain(route) { Look = new Vector3(6f, 0f, 0f) };
            AgentController agent = Agent(brain, new Vector3(0f, 0.1f, 0f));

            yield return Seconds(0.8f);
            Assert.IsTrue(agent.Follower.HasPath);
            Assert.Less(Vector3.Angle(agent.transform.forward, Vector3.forward), 5f, "No walking sideways.");

            yield return Seconds(3f);   // arrived: now it turns to the look target
            Assert.IsFalse(agent.Follower.HasPath);
            Assert.Less(Vector3.Angle(agent.transform.forward, new Vector3(6f, 0f, -8f).normalized), 5f);
        }

        [UnityTest]
        public IEnumerator ANewRouteDoesNotSendTheAgentBackToItsCellCentre()
        {
            Floor();
            var root = new GameObject("Walker");
            _created.Add(root);
            root.transform.position = new Vector3(0.25f, 0.1f, 0f);   // 0.25 m past its cell's centre
            root.AddComponent<CharacterController>();
            AgentPathFollower follower = root.AddComponent<AgentPathFollower>();
            yield return null;

            // The route a brain plans from the cell under the agent: its centre first.
            follower.SetPath(new List<Vector3> { new Vector3(-0.25f, 0f, 0f), new Vector3(1.5f, 0f, 0f), new Vector3(3f, 0f, 0f) }, 2f);
            float minX = root.transform.position.x;
            for (float until = Time.time + 0.5f; Time.time < until;)
            {
                minX = Mathf.Min(minX, root.transform.position.x);
                yield return null;
            }
            Assert.GreaterOrEqual(minX, 0.24f, "Went straight on instead of walking back first.");
            Assert.Greater(root.transform.position.x, 0.6f);
        }
    }
}
