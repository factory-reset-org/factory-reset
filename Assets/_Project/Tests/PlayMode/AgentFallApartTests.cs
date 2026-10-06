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

namespace ToyFactory.Tests
{
    public sealed class AgentFallApartTests
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

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        // A body like the real ones: agent components on the root, and a model whose mesh
        // parts sit under a pivot, each with a disabled collider (as in S3's prefab variants).
        AgentController Body(out List<Transform> parts, float knockOut = 1.2f, bool scrap = false, StateBrain brain = null)
        {
            var root = new GameObject("Body");
            _created.Add(root);
            root.SetActive(false);
            root.AddComponent<CharacterController>();
            AgentController agent = root.AddComponent<AgentController>();
            SetField(agent, "hitPoints", 1);
            SetField(agent, "knockOutSeconds", knockOut);
            SetField(agent, "scrapWhenDown", scrap);

            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            var pivot = new GameObject("Body_Pivot").transform;
            pivot.SetParent(model, false);
            pivot.localPosition = new Vector3(0f, 0.5f, 0f);
            parts = new List<Transform>();
            for (int i = 0; i < 3; i++)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                part.name = "Part" + i;
                part.SetParent(pivot, false);
                part.localPosition = new Vector3(i * 0.3f, 0.2f * i, 0f);
                part.localScale = Vector3.one * 0.25f;
                part.GetComponent<Collider>().enabled = false;
                parts.Add(part);
            }

            AgentFallApart fallApart = root.AddComponent<AgentFallApart>();
            SetField(fallApart, "model", model);
            SetField(fallApart, "reassembleSeconds", 0.5f);
            SetField(fallApart, "scrapLingerSeconds", 0.2f);
            SetField(fallApart, "scrapShrinkSeconds", 0.2f);
            root.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Guard, 0), brain ?? new StateBrain(), new WorldBlackboard());
            return agent;
        }

        [UnityTest]
        public IEnumerator KnockOutBreaksThePartsOffAndPutsThemBackByTheReboot()
        {
            AgentController agent = Body(out List<Transform> parts, knockOut: 1.2f);
            AgentFallApart fallApart = agent.GetComponent<AgentFallApart>();
            Transform pivot = parts[0].parent;
            var rest = new List<Vector3>();
            foreach (Transform part in parts)
                rest.Add(part.localPosition);

            agent.TakeHit();
            yield return null;

            Assert.IsTrue(fallApart.IsApart);
            foreach (Transform part in parts)
            {
                Assert.AreNotSame(pivot, part.parent, "Detached from the body.");
                Assert.IsNotNull(part.GetComponent<Rigidbody>(), "A physics body while apart.");
                Assert.IsTrue(part.GetComponent<Collider>().enabled);
                Assert.AreEqual(LayerMask.NameToLayer("Debris"), part.gameObject.layer);
            }

            yield return Seconds(1.6f);   // past the reboot; the last 0.5 s was the flight back

            Assert.IsFalse(agent.IsDisabled);
            Assert.IsFalse(fallApart.IsApart);
            for (int i = 0; i < parts.Count; i++)
            {
                Assert.AreSame(pivot, parts[i].parent, "Back under its pivot.");
                Assert.That(Vector3.Distance(rest[i], parts[i].localPosition), Is.LessThan(1e-3f));
                Assert.IsNull(parts[i].GetComponent<Rigidbody>());
                Assert.IsFalse(parts[i].GetComponent<Collider>().enabled, "Collider back to disabled, as S3 left it.");
                Assert.AreEqual(0, parts[i].gameObject.layer);
            }
        }

        [UnityTest]
        public IEnumerator ScrappedAgentShrinksAwayAndIsSwitchedOff()
        {
            AgentController agent = Body(out List<Transform> parts, scrap: true);

            agent.TakeHit();
            yield return null;
            Assert.IsTrue(agent.IsDead);
            Assert.IsTrue(agent.GetComponent<AgentFallApart>().IsApart);

            yield return Seconds(0.6f);
            Assert.IsFalse(agent.gameObject.activeSelf, "Switched off, not destroyed: the spawner still lists it.");
            foreach (Transform part in parts)
                Assert.IsTrue(part == null, "The parts are gone.");
        }

        // ---- Alert level and icon ------------------------------------------------------

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
            AgentController agent = Body(out _, brain: brain);

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
            AgentController agent = Body(out _, brain: brain);
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
