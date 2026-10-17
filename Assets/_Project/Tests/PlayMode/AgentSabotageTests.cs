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
    /// <summary>
    /// The runtime carries out a Saboteur's door, trap and battery actions through the
    /// <see cref="SabotageTargets"/> registry: once per request when in reach, with exactly one
    /// answer to the brain per request; and it tells a brain that asks about its own health.
    /// </summary>
    public sealed class AgentSabotageTests
    {
        readonly List<Object> _created = new List<Object>();
        readonly List<(SabotageKind kind, int id, ISabotageable target)> _registered =
            new List<(SabotageKind, int, ISabotageable)>();

        // Emits whatever action the test sets, every tick, and records what it is told.
        sealed class ScriptBrain : IAgentBrain, IActionFeedback, IHealthAware
        {
            public AgentAction Action;
            public int TargetId;
            public readonly List<(AgentAction action, int id, bool success)> Answers =
                new List<(AgentAction, int, bool)>();
            public readonly List<(int left, int max)> Health = new List<(int, int)>();

            public AgentIntent Tick(in AgentContext ctx) =>
                new AgentIntent { Action = Action, ActionTargetId = TargetId, DebugState = "Sabotage" };

            public void OnActionResolved(AgentAction action, int targetId, bool success) =>
                Answers.Add((action, targetId, success));

            public void OnHealthChanged(int hitPointsLeft, int maxHitPoints) =>
                Health.Add((hitPointsLeft, maxHitPoints));

            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        // Stands in for the Animator, which writes the clip pose every frame before the
        // hands-on move adds to it in LateUpdate.
        sealed class RestPose : MonoBehaviour
        {
            void Update() => transform.localRotation = Quaternion.identity;
        }

        sealed class CountingTarget : ISabotageable
        {
            public int Executed;
            public void Execute() => Executed++;
        }

        // A registered door, as S2's Door is: closed by Execute, opened through IDoor.
        sealed class CountingDoor : ISabotageable, IDoor
        {
            public int Closed, Opened;
            public bool IsOpen => Opened > Closed;
            public void Execute() => Closed++;
            public void Open() => Opened++;
            public void Close() => Closed++;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var (kind, id, target) in _registered)
                SabotageTargets.Unregister(kind, id, target);
            _registered.Clear();
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

        static IEnumerator Until(System.Func<bool> condition, string what, float seconds = 2f)
        {
            float until = Time.time + seconds;
            while (!condition())
            {
                if (Time.time > until)
                    Assert.Fail("Timed out waiting: " + what);
                yield return null;
            }
        }

        AgentController Body(ScriptBrain brain, int hitPoints = 3, float knockOut = 1f)
        {
            var root = new GameObject("Saboteur");
            _created.Add(root);
            root.SetActive(false);
            root.AddComponent<CharacterController>();
            AgentController agent = root.AddComponent<AgentController>();
            SetField(agent, "hitPoints", hitPoints);
            SetField(agent, "knockOutSeconds", knockOut);
            root.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Saboteur, 0, 0), brain, new WorldBlackboard());
            return agent;
        }

        CountingTarget Target(SabotageKind kind, int id, Vector3 at)
        {
            var marker = new GameObject($"{kind}{id}");
            _created.Add(marker);
            marker.transform.position = at;
            var target = new CountingTarget();
            SabotageTargets.Register(kind, id, target, marker.transform);
            _registered.Add((kind, id, target));
            return target;
        }

        [UnityTest]
        public IEnumerator InReachItActsOncePerRequestAndSaysItWorked()
        {
            // 2 m away on the flat and 1 m up: in reach (2.5 m, height ignored).
            CountingTarget door = Target(SabotageKind.Door, 7, new Vector3(2f, 1f, 0f));
            var brain = new ScriptBrain { Action = AgentAction.CloseDoor, TargetId = 7 };
            Body(brain);

            yield return Seconds(0.3f);
            Assert.AreEqual(1, door.Executed, "Repeating the action every tick is one request.");
            CollectionAssert.AreEqual(new[] { (AgentAction.CloseDoor, 7, true) }, brain.Answers);

            brain.Action = AgentAction.None;
            yield return null;
            yield return null;
            brain.Action = AgentAction.CloseDoor;
            yield return Seconds(0.1f);
            Assert.AreEqual(2, door.Executed, "Asking again after a break is a new request.");
            Assert.AreEqual(2, brain.Answers.Count);
        }

        [UnityTest]
        public IEnumerator OutOfReachItGivesUpAfterTwoSeconds()
        {
            CountingTarget trap = Target(SabotageKind.Trap, 2, new Vector3(6f, 0f, 0f));
            var brain = new ScriptBrain { Action = AgentAction.ArmTrap, TargetId = 2 };
            Body(brain);

            yield return Seconds(1.2f);
            Assert.IsEmpty(brain.Answers, "Still waiting to get close enough.");

            yield return Seconds(1.2f);
            Assert.AreEqual(0, trap.Executed);
            CollectionAssert.AreEqual(new[] { (AgentAction.ArmTrap, 2, false) }, brain.Answers);
        }

        [UnityTest]
        public IEnumerator AnUnknownTargetFailsStraightAway()
        {
            var brain = new ScriptBrain { Action = AgentAction.StealBattery, TargetId = 3 };
            Body(brain);

            yield return null;
            yield return null;
            CollectionAssert.AreEqual(new[] { (AgentAction.StealBattery, 3, false) }, brain.Answers);
        }

        [UnityTest]
        public IEnumerator ARequestStillWaitingFailsWhenTheAgentGoesDownOrIsReplaced()
        {
            CountingTarget far = Target(SabotageKind.Door, 1, new Vector3(8f, 0f, 0f));
            CountingTarget near = Target(SabotageKind.Battery, 4, new Vector3(1f, 0f, 0f));
            var brain = new ScriptBrain { Action = AgentAction.CloseDoor, TargetId = 1 };
            AgentController agent = Body(brain);

            yield return Seconds(0.2f);
            brain.Action = AgentAction.StealBattery;
            brain.TargetId = 4;
            yield return Seconds(0.1f);
            CollectionAssert.AreEqual(new[]
            {
                (AgentAction.CloseDoor, 1, false),   // replaced by the newer request
                (AgentAction.StealBattery, 4, true)
            }, brain.Answers);
            Assert.AreEqual(0, far.Executed);
            Assert.AreEqual(1, near.Executed);

            brain.Action = AgentAction.CloseDoor;
            brain.TargetId = 1;
            yield return Seconds(0.2f);
            agent.Disable(0.5f);
            Assert.AreEqual((AgentAction.CloseDoor, 1, false), brain.Answers[brain.Answers.Count - 1],
                "Knocked out before it got there.");
            Assert.AreEqual(3, brain.Answers.Count);
        }

        [UnityTest]
        public IEnumerator FrozenOrDownItDoesNothing()
        {
            CountingTarget door = Target(SabotageKind.Door, 5, new Vector3(1f, 0f, 0f));
            var brain = new ScriptBrain();
            AgentController agent = Body(brain, hitPoints: 1, knockOut: 0.6f);

            agent.TakeHit();   // down for 0.6 s
            brain.Action = AgentAction.CloseDoor;
            brain.TargetId = 5;
            yield return Seconds(0.3f);
            Assert.AreEqual(0, door.Executed, "Not while knocked out.");

            yield return Seconds(0.5f);
            Assert.AreEqual(1, door.Executed, "Carried out after the reboot.");
        }

        [UnityTest]
        public IEnumerator TheBrainHearsItsHealthAtTheStartOnEachHitAndAtTheReboot()
        {
            var brain = new ScriptBrain();
            AgentController agent = Body(brain, hitPoints: 2, knockOut: 0.3f);
            CollectionAssert.AreEqual(new[] { (2, 2) }, brain.Health, "Told once at the start.");

            agent.TakeHit();
            agent.TakeHit();
            CollectionAssert.AreEqual(new[] { (2, 2), (1, 2), (0, 2) }, brain.Health);

            yield return Seconds(0.5f);
            CollectionAssert.AreEqual(new[] { (2, 2), (1, 2), (0, 2), (2, 2) }, brain.Health,
                "Back to full at the reboot.");
        }

        [UnityTest]
        public IEnumerator OpenDoorOpensARegisteredDoorAndFailsOnAnythingElse()
        {
            var marker = new GameObject("Door12");
            _created.Add(marker);
            marker.transform.position = new Vector3(1.5f, 0f, 0f);
            var door = new CountingDoor();
            SabotageTargets.Register(SabotageKind.Door, 12, door, marker.transform);
            _registered.Add((SabotageKind.Door, 12, door));
            CountingTarget notADoor = Target(SabotageKind.Door, 13, new Vector3(-1.5f, 0f, 0f));
            var brain = new ScriptBrain { Action = AgentAction.OpenDoor, TargetId = 12 };
            Body(brain);

            yield return Seconds(0.2f);
            Assert.AreEqual(1, door.Opened, "Opened, once.");
            Assert.AreEqual(0, door.Closed, "Not closed.");
            CollectionAssert.AreEqual(new[] { (AgentAction.OpenDoor, 12, true) }, brain.Answers);

            brain.TargetId = 13;
            yield return Seconds(0.2f);
            Assert.AreEqual(0, notADoor.Executed, "Something that is not a door is not 'opened' by closing it.");
            Assert.AreEqual((AgentAction.OpenDoor, 13, false), brain.Answers[brain.Answers.Count - 1]);
        }

        [Test]
        public void TheRegistryForgetsADestroyedTargetAndOnlyItsOwnerUnregisters()
        {
            var marker = new GameObject("Door9");
            var first = new CountingTarget();
            var second = new CountingTarget();
            SabotageTargets.Register(SabotageKind.Door, 9, first, marker.transform);
            SabotageTargets.Register(SabotageKind.Door, 9, second, marker.transform);
            _registered.Add((SabotageKind.Door, 9, second));

            SabotageTargets.Unregister(SabotageKind.Door, 9, first);   // replaced already: ignored
            Assert.IsTrue(SabotageTargets.TryGet(SabotageKind.Door, 9, out ISabotageable found, out _));
            Assert.AreSame(second, found);
            Assert.IsFalse(SabotageTargets.TryGet(SabotageKind.Trap, 9, out _, out _), "Kinds are kept apart.");

            Object.DestroyImmediate(marker);
            Assert.IsFalse(SabotageTargets.TryGet(SabotageKind.Door, 9, out _, out _));
        }

        [UnityTest]
        public IEnumerator TheControllerSaysWhatItCarriedOutAndWhereBeforeTheBrainHears()
        {
            CountingTarget trap = Target(SabotageKind.Trap, 4, new Vector3(1f, 0f, 1f));
            var brain = new ScriptBrain { Action = AgentAction.ArmTrap, TargetId = 4 };
            AgentController agent = Body(brain);
            var carried = new List<(AgentAction action, Vector3 at, int answersSoFar)>();
            agent.ActionCarriedOut += (action, at) => carried.Add((action, at, brain.Answers.Count));

            yield return Seconds(0.3f);
            Assert.AreEqual(1, trap.Executed);
            Assert.AreEqual(1, carried.Count, "Once per action carried out.");
            Assert.AreEqual(AgentAction.ArmTrap, carried[0].action);
            Assert.AreEqual(new Vector3(1f, 0f, 1f), carried[0].at, "Where the target is.");
            Assert.AreEqual(0, carried[0].answersSoFar, "Raised before the brain is told it worked.");

            brain.Action = AgentAction.StealBattery;   // nothing registered: fails, not carried out
            brain.TargetId = 5;
            yield return Seconds(0.2f);
            Assert.AreEqual(1, carried.Count, "A failed request is not carried out.");
        }

        [UnityTest]
        public IEnumerator TheSaboteurReachesInToActAndAStolenBatteryShowsInItsCrate()
        {
            var brain = new ScriptBrain { Action = AgentAction.ArmTrap, TargetId = 4 };
            Target(SabotageKind.Trap, 4, new Vector3(0f, 0f, 1.5f));
            Target(SabotageKind.Battery, 6, new Vector3(0f, 0f, 1.5f));

            // A Saboteur body with the move, a body pivot to lean and the battery in its crate.
            var root = new GameObject("Saboteur");
            _created.Add(root);
            root.SetActive(false);
            root.AddComponent<CharacterController>();
            AgentController agent = root.AddComponent<AgentController>();
            var model = new GameObject("SaboteurBot_Root").transform;
            model.SetParent(root.transform, false);
            var body = new GameObject("Body_Pivot").transform;
            body.SetParent(model, false);
            body.gameObject.AddComponent<RestPose>();
            var battery = new GameObject("Battery");
            battery.transform.SetParent(body, false);
            battery.SetActive(false);
            SaboteurHandsOn handsOn = root.AddComponent<SaboteurHandsOn>();
            SetField(handsOn, "root", model);
            SetField(handsOn, "body", body);
            SetField(handsOn, "carriedBattery", battery);
            root.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Saboteur, 0, 0), brain, new WorldBlackboard());

            yield return Until(() => handsOn.Moves == 1, "arming the trap plays the reach");
            Assert.IsTrue(handsOn.IsPlaying);
            yield return Seconds(0.27f);   // 0.21-0.46 s is full reach
            yield return new WaitForEndOfFrame();   // after LateUpdate, where the move poses
            Assert.AreEqual(18f, Vector3.Angle(Vector3.up, body.up), 1f, "Leaning 18° in at full reach.");
            Assert.Greater(body.up.z, 0f, "Forwards, towards the trap.");
            Assert.AreEqual(0.2f, model.localPosition.z, 0.01f,
                "The trap is 1.5 m away and the pincers reach 1.3 m: it rolls the other 0.2 m in.");
            Assert.IsFalse(handsOn.CarriesBattery, "Arming a trap takes no battery.");
            yield return Seconds(0.6f);
            Assert.IsFalse(handsOn.IsPlaying, "Over after 0.7 s.");
            Assert.AreEqual(Vector3.zero, model.localPosition, "And rolled back to rest.");

            brain.Action = AgentAction.StealBattery;
            brain.TargetId = 6;
            yield return Until(() => handsOn.Moves == 2, "taking the battery plays the reach");
            Assert.IsFalse(handsOn.CarriesBattery, "Not yet: the battery appears as the pincers close.");
            yield return Seconds(0.6f);
            Assert.IsTrue(handsOn.CarriesBattery, "The stolen battery is in the crate.");
            Assert.AreEqual(2, handsOn.Moves);
        }
    }
}
