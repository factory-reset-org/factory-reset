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
    /// <summary>A stand-in player: a capsule on the Player layer that records the damage it takes.</summary>
    public sealed class FakeShotPlayer : MonoBehaviour, IPlayerState
    {
        public readonly List<(float amount, int source)> Damage = new List<(float, int)>();
        public Vector3 Position => transform.position;
        public Vector3 Velocity => Vector3.zero;
        public Vector3 Forward => transform.forward;
        public float SprintSpeed => 7f;
        public bool IsAlive => true;
        public float HealthFraction => 1f;
        public float AmmoFraction => 1f;
        public bool IsReloading => false;
        public float OverchargeTimeLeft => 0f;
        public float LastShotTime => -1f;
        public void TakeDamage(float amount, int sourceAgentId) => Damage.Add((amount, sourceAgentId));
    }

    public sealed class AgentCombatTests
    {
        readonly List<Object> _created = new List<Object>();

        sealed class IdleBrain : IAgentBrain
        {
            public bool Shoot;
            public AgentIntent Tick(in AgentContext ctx) => new AgentIntent { Action = Shoot ? AgentAction.Shoot : AgentAction.None };
            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
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

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        // An agent at the origin facing +Z, with a weapon if asked. Fields are set before Awake.
        AgentController Agent(int hitPoints = 3, float knockOut = 7f, bool scrap = false, bool armed = false,
            IdleBrain brain = null)
        {
            var go = new GameObject("Agent");
            _created.Add(go);
            go.SetActive(false);
            go.layer = LayerMask.NameToLayer("Agents");
            var capsule = go.AddComponent<CharacterController>();
            capsule.center = new Vector3(0f, 1f, 0f);
            AgentController agent = go.AddComponent<AgentController>();
            SetField(agent, "hitPoints", hitPoints);
            SetField(agent, "knockOutSeconds", knockOut);
            SetField(agent, "scrapWhenDown", scrap);
            if (armed)
                go.AddComponent<AgentWeapon>();
            go.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Guard, 4), brain ?? new IdleBrain(), new WorldBlackboard());
            return agent;
        }

        FakeShotPlayer Player(Vector3 feet)
        {
            var go = new GameObject("Player");
            _created.Add(go);
            go.layer = LayerMask.NameToLayer("Player");
            go.transform.position = feet;
            var capsule = go.AddComponent<CharacterController>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.4f;
            FakeShotPlayer player = go.AddComponent<FakeShotPlayer>();
            PlayerState.Publish(player);
            return player;
        }

        GameObject Block(Vector3 centre, string layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(go);
            go.layer = LayerMask.NameToLayer(layer);
            go.transform.position = centre;
            go.transform.localScale = new Vector3(3f, 3f, 0.2f);
            return go;
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        // ---- Taking hits ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator HitsCountDownThenKnockOutAndRebootWithFullHitPoints()
        {
            AgentController agent = Agent(hitPoints: 3, knockOut: 0.2f);

            agent.TakeHit();
            agent.TakeHit();
            Assert.IsFalse(agent.IsDisabled);
            Assert.AreEqual(1, agent.HitPointsLeft);

            agent.TakeHit();
            Assert.IsTrue(agent.IsDisabled, "The third hit knocks it out.");
            agent.TakeHit();
            Assert.AreEqual(0, agent.HitPointsLeft, "Hits on a downed agent are ignored.");

            yield return Seconds(0.3f);
            Assert.IsFalse(agent.IsDisabled, "Reassembled after the knock-out time.");
            Assert.AreEqual(3, agent.HitPointsLeft, "Back to full hit points.");
        }

        [Test]
        public void SaboteurIsScrappedForGoodWhenItGoesDown()
        {
            AgentController agent = Agent(hitPoints: 2, scrap: true);

            agent.TakeHit();
            Assert.IsFalse(agent.IsDead);
            agent.TakeHit();

            Assert.IsTrue(agent.IsDead);
            Assert.IsFalse(agent.IsDisabled, "Scrapped, not knocked out.");
            agent.TakeHit();
            Assert.IsTrue(agent.IsDead, "Further hits change nothing.");
        }

        [Test]
        public void AgentIsFoundAsDamageableFromItsCapsule()
        {
            AgentController agent = Agent();
            Collider capsule = agent.GetComponent<CharacterController>();

            Assert.AreSame(agent, capsule.GetComponentInParent<IDamageable>(), "S2's blaster finds the agent from the collider it hits.");
            Assert.AreEqual(LayerMask.NameToLayer("Agents"), agent.gameObject.layer);
        }

        // ---- Shooting ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ShotIsTelegraphedThenHitsThePlayer()
        {
            AgentController agent = Agent(armed: true);
            AgentWeapon weapon = agent.GetComponent<AgentWeapon>();
            FakeShotPlayer player = Player(new Vector3(0f, 0f, 5f));

            weapon.RequestShot();
            Assert.IsTrue(weapon.IsAiming);
            Assert.IsTrue(agent.IsAttacking, "Aiming counts as attacking, for the aim pose.");

            yield return Seconds(0.15f);
            Assert.AreEqual(0, weapon.ShotsFired, "Still aiming: the 0.3 s telegraph.");

            yield return Seconds(0.25f);
            Assert.AreEqual(1, weapon.ShotsFired);
            Assert.IsTrue(weapon.LastShotHit);
            Assert.AreEqual(1, player.Damage.Count);
            Assert.AreEqual(10f, player.Damage[0].amount);
            Assert.AreEqual(4, player.Damage[0].source, "The damage names the agent that fired.");
        }

        [UnityTest]
        public IEnumerator AShotWaitsForTheBodyToTurnToTheTargetSoItNeverLeavesSideways()
        {
            AgentController agent = Agent(armed: true);   // facing +Z
            AgentWeapon weapon = agent.GetComponent<AgentWeapon>();
            FakeShotPlayer player = Player(new Vector3(0f, 0f, -5f));   // behind it

            weapon.RequestShot();
            yield return Seconds(0.35f);
            Assert.AreEqual(0, weapon.ShotsFired, "The telegraph is over, but it is still turning round (360 degrees a second).");

            yield return Seconds(0.4f);
            Assert.AreEqual(1, weapon.ShotsFired, "Fired once it faced the player.");
            Assert.IsTrue(weapon.LastShotHit);
            Assert.Less(Vector3.Angle(agent.transform.forward, Vector3.back), 26f, "Facing the player when it fired.");
        }

        [UnityTest]
        public IEnumerator AShotTheBodyCannotTurnForIsDroppedNotFiredSideways()
        {
            AgentController agent = Agent(armed: true);
            SetField(agent.GetComponent<AgentPathFollower>(), "turnSpeed", 1f);
            AgentWeapon weapon = agent.GetComponent<AgentWeapon>();
            Player(new Vector3(5f, 0f, 0f));   // 90 degrees to its side

            weapon.RequestShot();
            yield return Seconds(1f);
            Assert.AreEqual(0, weapon.ShotsFired);
            Assert.AreEqual(1, weapon.ShotsDropped);
            Assert.IsFalse(agent.IsAttacking, "The aim is over.");
        }

        [UnityTest]
        public IEnumerator EachShotKicksTheArmOfTheCannonThatFired()
        {
            var go = new GameObject("Captain");
            _created.Add(go);
            go.SetActive(false);
            go.layer = LayerMask.NameToLayer("Agents");
            go.AddComponent<CharacterController>().center = new Vector3(0f, 1f, 0f);
            AgentController agent = go.AddComponent<AgentController>();
            var arms = new Transform[2];
            var barrels = new Renderer[2];
            for (int i = 0; i < 2; i++)
            {
                arms[i] = new GameObject("Arm " + i).transform;
                arms[i].SetParent(go.transform, false);
                var cannon = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(cannon.GetComponent<Collider>());
                cannon.transform.SetParent(arms[i], false);
                cannon.transform.localPosition = new Vector3(i == 0 ? -0.7f : 0.7f, 1.2f, 0.4f);
                cannon.transform.localScale = Vector3.one * 0.2f;
                barrels[i] = cannon.GetComponent<Renderer>();
            }
            AgentWeapon weapon = go.AddComponent<AgentWeapon>();
            SetField(weapon, "barrels", barrels);
            AgentShotRecoil recoil = go.AddComponent<AgentShotRecoil>();
            SetField(recoil, "arms", arms);
            go.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Captain, 6), new IdleBrain(), new WorldBlackboard());
            Player(new Vector3(0f, 0f, 6f));

            weapon.RequestShot();
            yield return Seconds(0.35f);
            Assert.AreEqual(1, weapon.ShotsFired);
            Assert.IsTrue(recoil.IsKicking, "The shot kicks.");
            Assert.AreEqual(0, recoil.LastBarrel);

            yield return Seconds(0.4f);
            Assert.IsFalse(recoil.IsKicking, "Settled after the kick.");
            weapon.RequestShot();
            yield return Seconds(0.35f);
            Assert.AreEqual(2, weapon.ShotsFired);
            Assert.AreEqual(1, recoil.LastBarrel, "The second shot comes from, and kicks, the other cannon.");
        }

        [UnityTest]
        public IEnumerator WallInTheWayTakesTheShot()
        {
            AgentController agent = Agent(armed: true);
            AgentWeapon weapon = agent.GetComponent<AgentWeapon>();
            FakeShotPlayer player = Player(new Vector3(0f, 0f, 5f));
            Block(new Vector3(0f, 1.2f, 2.5f), "Default");

            weapon.RequestShot();
            yield return Seconds(0.4f);

            Assert.AreEqual(1, weapon.ShotsFired);
            Assert.IsFalse(weapon.LastShotHit);
            Assert.IsEmpty(player.Damage);
        }

        [UnityTest]
        public IEnumerator AnotherAgentInTheWayDoesNotBlockOrTakeTheShot()
        {
            AgentController agent = Agent(armed: true);
            AgentWeapon weapon = agent.GetComponent<AgentWeapon>();
            FakeShotPlayer player = Player(new Vector3(0f, 0f, 5f));
            Block(new Vector3(0f, 1.2f, 2.5f), "Agents");

            weapon.RequestShot();
            yield return Seconds(0.4f);

            Assert.IsTrue(weapon.LastShotHit, "No friendly fire: agents are not in the hit mask.");
            Assert.AreEqual(1, player.Damage.Count);
        }

        [UnityTest]
        public IEnumerator KnockOutDuringTheAimCancelsTheShot()
        {
            AgentController agent = Agent(hitPoints: 1, armed: true);
            AgentWeapon weapon = agent.GetComponent<AgentWeapon>();
            FakeShotPlayer player = Player(new Vector3(0f, 0f, 5f));

            weapon.RequestShot();
            agent.TakeHit();
            yield return Seconds(0.4f);

            Assert.IsFalse(weapon.IsAiming);
            Assert.AreEqual(0, weapon.ShotsFired);
            Assert.IsEmpty(player.Damage);
        }

        [UnityTest]
        public IEnumerator BrainShootGoesThroughTheWeaponOneShotAtATime()
        {
            var brain = new IdleBrain { Shoot = true };   // asks to shoot on every tick
            AgentController agent = Agent(armed: true, brain: brain);
            AgentWeapon weapon = agent.GetComponent<AgentWeapon>();
            Player(new Vector3(0f, 0f, 5f));

            yield return null;
            Assert.IsTrue(weapon.IsAiming, "The brain's Shoot started an aim.");

            yield return Seconds(0.33f);
            Assert.AreEqual(1, weapon.ShotsFired, "Requests during the aim were ignored.");
        }
    }
}
