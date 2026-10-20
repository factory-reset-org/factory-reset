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
using ToyFactory.Runtime.Effects;
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

        // A wind-up toy brain (the Tracker) that is rewinding while Rewinding is set.
        sealed class WindUpBrain : IAgentBrain, IWindUpState
        {
            public bool Rewinding;
            public float Energy01 => Rewinding ? 0f : 1f;
            public bool IsRewinding => Rewinding;
            public AgentIntent Tick(in AgentContext ctx) => default;
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
            IAgentBrain brain = null)
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
        public void HitsCountDoubleWhileAWindUpToyRewinds()
        {
            var brain = new WindUpBrain();
            AgentController agent = Agent(hitPoints: 3, brain: brain);

            agent.TakeHit();
            Assert.AreEqual(2, agent.HitPointsLeft, "A wound-up toy loses one per hit.");

            brain.Rewinding = true;
            agent.TakeHit();
            Assert.AreEqual(0, agent.HitPointsLeft, "Rewinding, the hit costs two.");
            Assert.IsTrue(agent.IsDisabled, "Knocked out.");
        }

        [Test]
        public void ADoubleHitNeverTakesHitPointsBelowZero()
        {
            var brain = new WindUpBrain { Rewinding = true };
            AgentController agent = Agent(hitPoints: 3, brain: brain);

            agent.TakeHit();
            agent.TakeHit();

            Assert.AreEqual(0, agent.HitPointsLeft);
            Assert.IsTrue(agent.IsDisabled, "Three hit points go in two rewinding hits.");
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

        // A body whose cannon is held out 1.5 m in front at chest height, off to its right, as
        // the Captain's is in its aim pose: well past the edge of its capsule.
        AgentWeapon HeldOutCannon(out AgentController agent)
        {
            var go = new GameObject("Captain");
            _created.Add(go);
            go.SetActive(false);
            go.layer = LayerMask.NameToLayer("Agents");
            go.AddComponent<CharacterController>().center = new Vector3(0f, 1f, 0f);
            agent = go.AddComponent<AgentController>();
            var cannon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(cannon);
            Object.DestroyImmediate(cannon.GetComponent<Collider>());
            cannon.transform.SetParent(go.transform, false);
            cannon.transform.localPosition = new Vector3(0.7f, 1.9f, 1.1f);
            cannon.transform.localScale = new Vector3(0.2f, 0.2f, 0.8f);   // along +Z: the tip at z 1.5
            AgentWeapon weapon = go.AddComponent<AgentWeapon>();
            SetField(weapon, "barrels", new[] { cannon.GetComponent<Renderer>() });
            go.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Captain, 6), new IdleBrain(), new WorldBlackboard());
            return weapon;
        }

        [UnityTest]
        public IEnumerator ACannonHeldOutIntoACrateCannotShootThroughIt()
        {
            AgentWeapon weapon = HeldOutCannon(out _);
            // A crate round the cannon's tip, clear of the body's capsule: the muzzle is inside it.
            GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(crate);
            crate.transform.position = new Vector3(0.7f, 1.6f, 1.6f);
            crate.transform.localScale = Vector3.one;
            FakeShotPlayer player = Player(new Vector3(0.7f, 0f, 8f));   // in plain view past the crate
            weapon.RequestShot();
            yield return Seconds(0.4f);

            // 19 October: a shot whose line is blocked is held, not wasted on the obstacle.
            Assert.AreEqual(0, weapon.ShotsFired, "The cannon is in the crate: it holds fire.");
            Assert.AreEqual(1, weapon.ShotsDropped);
            Assert.AreEqual(0, player.Damage.Count, "and the player behind the crate is not hit");
        }

        [UnityTest]
        public IEnumerator ACannonHeldOutPastAnOpenSpaceStillHits()
        {
            AgentWeapon weapon = HeldOutCannon(out _);
            FakeShotPlayer player = Player(new Vector3(0.7f, 0f, 8f));

            weapon.RequestShot();
            yield return Seconds(0.4f);

            Assert.AreEqual(1, weapon.ShotsFired);
            Assert.IsTrue(weapon.LastShotHit, "Nothing round the cannon: it hits.");
            Assert.AreEqual(1, player.Damage.Count);
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
        public IEnumerator TheMuzzleGlowDoesNotStayOnScreenWhileTheGameIsFrozen()
        {
            AgentController agent = Agent(armed: true);
            AgentWeapon weapon = agent.GetComponent<AgentWeapon>();
            Player(new Vector3(0f, 0f, 5f));

            weapon.RequestShot();
            yield return null;
            yield return null;
            Assert.IsTrue(weapon.ChargeGlowVisible, "The muzzle glows while it charges.");

            // A cutscene starts mid-aim: every body freezes.
            agent.OnGameStateChanged(GameState.Playing, GameState.Cutscene);
            yield return null;
            Assert.IsFalse(weapon.ChargeGlowVisible, "No glow left hanging over the frozen game.");
        }

        [UnityTest]
        public IEnumerator TheGlowGrowsThroughTheAimThenFlashesAndGoes()
        {
            AgentController agent = Agent(armed: true);
            AgentWeapon weapon = agent.GetComponent<AgentWeapon>();
            Player(new Vector3(0f, 0f, 5f));

            weapon.RequestShot();
            yield return null;
            yield return null;
            float early = weapon.ChargeGlowSize;
            float late = 0f;
            while (weapon.ShotsFired == 0)
            {
                late = Mathf.Max(late, weapon.ChargeGlowSize);
                yield return null;
            }
            Assert.Greater(late, early * 2f, $"It grows as it charges: {early} then {late}.");
            Assert.IsTrue(weapon.ChargeGlowVisible, "The shot flashes at the muzzle.");
            yield return Seconds(0.3f);
            Assert.IsFalse(weapon.ChargeGlowVisible, "And the flash goes.");
        }

        [UnityTest]
        public IEnumerator AWeaponWithItsOwnChargeEffectShowsNoGlow()
        {
            // The Captain's power cell is its telegraph, so its weapon grows nothing.
            var go = new GameObject("Agent");
            _created.Add(go);
            go.SetActive(false);
            go.AddComponent<CharacterController>();
            AgentController agent = go.AddComponent<AgentController>();
            AgentWeapon weapon = go.AddComponent<AgentWeapon>();
            SetField(weapon, "chargeGlow", false);
            go.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Captain, 6), new IdleBrain(), new WorldBlackboard());
            Player(new Vector3(0f, 0f, 5f));

            weapon.RequestShot();
            for (int i = 0; i < 5; i++)
                yield return null;
            Assert.IsTrue(weapon.IsAiming);
            Assert.IsFalse(weapon.ChargeGlowVisible);
            Assert.IsNull(go.transform.Find("ChargeGlow"), "No glow is even made.");
        }

        [UnityTest]
        public IEnumerator AShotFliesAsABoltInTheRobotsColourAndSparksWhereItLands()
        {
            // A bolt and a spark burst made the way the player's blaster assets are.
            var boltObject = new GameObject("Bolt");
            _created.Add(boltObject);
            boltObject.SetActive(false);
            boltObject.AddComponent<LineRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            ShotBolt bolt = boltObject.AddComponent<ShotBolt>();
            var burstObject = new GameObject("Burst");
            _created.Add(burstObject);
            burstObject.SetActive(false);
            burstObject.AddComponent<ParticleSystem>();
            ImpactBurst burst = burstObject.AddComponent<ImpactBurst>();

            var go = new GameObject("Agent");
            _created.Add(go);
            go.SetActive(false);
            go.layer = LayerMask.NameToLayer("Agents");
            go.AddComponent<CharacterController>().center = new Vector3(0f, 1f, 0f);
            AgentController agent = go.AddComponent<AgentController>();
            AgentWeapon weapon = go.AddComponent<AgentWeapon>();
            var colour = new Color(1f, 0.22f, 0.2f);
            SetField(weapon, "boltPrefab", bolt);
            SetField(weapon, "impactPrefab", burst);
            SetField(weapon, "boltColour", colour);
            go.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Guard, 4), new IdleBrain(), new WorldBlackboard());
            FakeShotPlayer player = Player(new Vector3(0f, 0f, 6f));

            ShotBolt launched = null;
            weapon.Fired += _ =>
            {
                foreach (ShotBolt b in Object.FindObjectsByType<ShotBolt>(FindObjectsSortMode.None))
                    if (b.IsFlying)
                        launched = b;
            };
            weapon.RequestShot();
            yield return Seconds(0.6f);

            Assert.AreEqual(1, weapon.ShotsFired);
            Assert.AreEqual(1, weapon.BoltsLaunched, "One bolt per shot.");
            Assert.IsNotNull(launched, "The bolt flies.");
            Assert.AreEqual(colour, launched.Colour, "In the robot's own colour.");
            Assert.IsTrue(weapon.LastShotHit);
            Assert.AreEqual(1, weapon.ImpactsPlayed, "Sparks where it landed, on the player.");
            Assert.Less(weapon.LastShotTo.z, 6f, "It ends on the player's capsule.");
            Assert.IsNull(go.transform.Find("ShotLine"), "No laser line any more.");
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

            // 19 October: a wall on the line means the shot is held, not wasted on the wall.
            Assert.AreEqual(0, weapon.ShotsFired, "The wall is in the way: it holds fire.");
            Assert.AreEqual(1, weapon.ShotsDropped);
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
