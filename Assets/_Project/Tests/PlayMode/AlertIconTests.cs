using System.Collections;
using System.Collections.Generic;
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
    public sealed class AlertIconTests
    {
        readonly List<Object> _created = new List<Object>();

        sealed class StateBrain : IAgentBrain
        {
            public string State = "Patrol";
            public AlertLevel Alert;
            public AgentIntent Tick(in AgentContext ctx) => new AgentIntent { DebugState = State, Alert = Alert };
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

        // A bare body: the agent components on the root, ticking a brain that reports a state.
        AgentController Body(StateBrain brain)
        {
            var root = new GameObject("Body");
            _created.Add(root);
            root.SetActive(false);
            root.AddComponent<CharacterController>();
            AgentController agent = root.AddComponent<AgentController>();
            root.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Guard, 0), brain, new WorldBlackboard());
            return agent;
        }

        [Test]
        public void StateNamesFromEachBrainMapToAnAlertLevel()
        {
            Assert.AreEqual(AlertLevel.Alert, AlertFromState.For("Chase"));
            Assert.AreEqual(AlertLevel.Alert, AlertFromState.For("PeekAndShoot"));
            Assert.AreEqual(AlertLevel.Alert, AlertFromState.For("Engage"));
            Assert.AreEqual(AlertLevel.Suspicious, AlertFromState.For("Search"));
            Assert.AreEqual(AlertLevel.Suspicious, AlertFromState.For("Observe"));
            Assert.AreEqual(AlertLevel.None, AlertFromState.For("Patrol"));
            Assert.AreEqual(AlertLevel.None, AlertFromState.For(null));
        }

        [UnityTest]
        public IEnumerator BrainsAlertWinsAndTheStateNameIsTheFallback()
        {
            var brain = new StateBrain { State = "Investigate" };
            AgentController agent = Body(brain);

            yield return null;
            Assert.AreEqual(AlertLevel.Suspicious, agent.Alert, "No alert from the brain: worked out from the state.");

            brain.Alert = AlertLevel.Alert;
            yield return null;
            Assert.AreEqual(AlertLevel.Alert, agent.Alert, "The brain's own level wins.");

            agent.Disable(5f);
            Assert.AreEqual(AlertLevel.None, agent.Alert, "No icon while down.");
        }

        [UnityTest]
        public IEnumerator IconShowsQuestionThenExclamationAndHidesWhenCalm()
        {
            var brain = new StateBrain { State = "Search" };
            AgentController agent = Body(brain);
            AlertIcon icon = agent.gameObject.AddComponent<AlertIcon>();
            TextMesh text = agent.GetComponentInChildren<TextMesh>(true);

            yield return null;
            yield return null;
            Assert.AreEqual(AlertLevel.Suspicious, icon.Shown);
            Assert.IsTrue(text.gameObject.activeSelf);
            Assert.AreEqual("?", text.text);

            brain.State = "Chase";
            yield return null;
            yield return null;
            Assert.AreEqual("!", text.text);

            brain.State = "Patrol";
            yield return null;
            yield return null;
            Assert.AreEqual(AlertLevel.None, icon.Shown);
            Assert.IsFalse(text.gameObject.activeSelf);
        }
    }
}
