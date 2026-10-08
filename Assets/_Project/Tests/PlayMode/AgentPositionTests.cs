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

namespace ToyFactory.Tests
{
    public sealed class AgentPositionTests
    {
        readonly List<Object> _created = new List<Object>();

        sealed class IdleBrain : IAgentBrain
        {
            public AgentIntent Tick(in AgentContext ctx) => new AgentIntent { DebugState = "Idle" };
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

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        // Saboteur A: one hit scraps it, as in the game.
        AgentController SaboteurA(Vector3 at)
        {
            var root = new GameObject("Saboteur A");
            _created.Add(root);
            root.SetActive(false);
            root.transform.position = at;
            root.AddComponent<CharacterController>();
            AgentController agent = root.AddComponent<AgentController>();
            SetField(agent, "hitPoints", 1);
            SetField(agent, "scrapWhenDown", true);
            root.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Saboteur, 0, 0), new IdleBrain(), new WorldBlackboard());
            return agent;
        }

        [UnityTest]
        public IEnumerator PositionIsWhereTheBodyStands()
        {
            AgentController agent = SaboteurA(new Vector3(31f, 0f, 35f));
            yield return null;
            IAgentState state = agent;
            Assert.AreEqual(new Vector3(31f, 0f, 35f), state.Position);

            agent.transform.position = new Vector3(36f, 0f, 38.5f);
            Assert.AreEqual(new Vector3(36f, 0f, 38.5f), state.Position, "Read live, not cached.");
        }

        [UnityTest]
        public IEnumerator ScrappedEventCarriesWhereSaboteurAFell()
        {
            AgentController agent = SaboteurA(new Vector3(28.5f, 0f, 33f));
            yield return null;

            Vector3? fell = null;
            char letter = '-';
            void Handle(IAgentState scrapped)
            {
                fell = scrapped.Position;
                letter = scrapped.Identity.SquadLetter;
            }
            AgentEvents.OnDestroyed += Handle;
            try
            {
                agent.TakeHit();
            }
            finally
            {
                AgentEvents.OnDestroyed -= Handle;
            }

            Assert.IsTrue(agent.IsDead);
            Assert.AreEqual('A', letter, "Listeners still tell Saboteur A apart by its identity.");
            Assert.AreEqual(new Vector3(28.5f, 0f, 33f), fell);
        }

        [UnityTest]
        public IEnumerator PositionOutlivesTheBody()
        {
            AgentController agent = SaboteurA(new Vector3(23.5f, 0f, 23f));
            yield return null;
            IAgentState state = agent;

            Object.Destroy(agent.gameObject);
            yield return null;

            Assert.IsTrue(agent == null, "The body is gone.");
            Assert.AreEqual(new Vector3(23.5f, 0f, 23f), state.Position, "The last position, not an exception.");
        }
    }
}
