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
    /// <summary>Stands in for the Aim clip: writes a lifted arm pose every frame, as the Animator does.</summary>
    public sealed class LiftedArmsClip : MonoBehaviour
    {
        public Transform[] Arms = new Transform[0];
        public float LiftDegrees = 20f;

        void Update()
        {
            foreach (Transform arm in Arms)
                arm.localRotation = Quaternion.Euler(-LiftDegrees, 0f, 0f);
        }
    }

    /// <summary>
    /// The cannons point at what they shoot: while aiming each barrel's axis points at the
    /// target's chest, whatever pose the clip lifted the arms to; between shots the clip's pose
    /// is left alone; the shot leaves from the barrel's tip; and the recoil kick stays small.
    /// Before, the Captain's barrels pointed 19° up (47° at the kick) while its shots went out
    /// level.
    /// </summary>
    public sealed class AgentCannonAimTests
    {
        readonly List<Object> _created = new List<Object>();

        sealed class IdleBrain : IAgentBrain
        {
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

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        // Poses are measured at the end of the frame: a coroutine resumed by "yield return null"
        // runs before LateUpdate, so it would see the clip's pose before the aim and the kick.
        static WaitForEndOfFrame EndOfFrame() => new WaitForEndOfFrame();

        // A two-cannon body facing +Z with arms the "clip" lifts by 20°, and the player 6 m ahead.
        AgentWeapon Shooter(out AgentCannonAim aim, out AgentShotRecoil recoil)
        {
            // A floor: the body has gravity, and a falling body changes every angle to the target.
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _created.Add(floor);
            floor.transform.localScale = new Vector3(3f, 1f, 3f);

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
                arms[i].localPosition = new Vector3(i == 0 ? -0.7f : 0.7f, 2f, 0f);   // the shoulder
                var cannon = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(cannon.GetComponent<Collider>());
                cannon.transform.SetParent(arms[i], false);
                cannon.transform.localPosition = new Vector3(0f, -0.7f, 0.3f);      // the hand
                cannon.transform.localScale = new Vector3(0.2f, 0.2f, 0.6f);        // a barrel along +Z
                barrels[i] = cannon.GetComponent<Renderer>();
            }
            AgentWeapon weapon = go.AddComponent<AgentWeapon>();
            SetField(weapon, "barrels", barrels);
            go.AddComponent<LiftedArmsClip>().Arms = arms;
            recoil = go.AddComponent<AgentShotRecoil>();
            SetField(recoil, "arms", arms);
            aim = go.AddComponent<AgentCannonAim>();
            SetField(aim, "arms", arms);
            go.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Captain, 6), new IdleBrain(), new WorldBlackboard());

            var player = new GameObject("Player");
            _created.Add(player);
            player.layer = LayerMask.NameToLayer("Player");
            player.transform.position = new Vector3(0f, 0f, 6f);
            var capsule = player.AddComponent<CharacterController>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            PlayerState.Publish(player.AddComponent<FakeShotPlayer>());
            return weapon;
        }

        // How far barrel i points above the line from its tip to the target, in degrees.
        static float PitchError(AgentWeapon weapon, int i)
        {
            Assert.IsTrue(weapon.TryGetTarget(out Vector3 target));
            Assert.IsTrue(weapon.TryGetBarrel(i, out Vector3 tip, out Vector3 direction));
            Vector3 to = (target - tip).normalized;
            return (Mathf.Asin(direction.y) - Mathf.Asin(to.y)) * Mathf.Rad2Deg;
        }

        static string Describe(AgentWeapon weapon)
        {
            Transform arm = weapon.GetComponent<LiftedArmsClip>().Arms[0];
            weapon.TryGetBarrel(0, out Vector3 tip, out Vector3 direction);
            return $"arm {arm.localEulerAngles}, barrel dir {direction}, tip {tip}";
        }

        [UnityTest]
        public IEnumerator AimingPointsEachBarrelAtTheTarget()
        {
            AgentWeapon weapon = Shooter(out AgentCannonAim aim, out _);
            yield return Seconds(0.05f);   // components added this frame start updating next frame
            yield return EndOfFrame();
            Assert.Greater(PitchError(weapon, 0), 15f, "The clip alone lifts the barrel well above the target. " + Describe(weapon));

            weapon.RequestShot();
            yield return Seconds(0.2f);   // aiming, eased fully in
            yield return EndOfFrame();

            Assert.IsTrue(weapon.IsAiming);
            Assert.AreEqual(1f, aim.Weight, 1e-3f);
            for (int i = 0; i < 2; i++)
                Assert.AreEqual(0f, PitchError(weapon, i), 1f, $"Barrel {i} points at the chest.");
        }

        [UnityTest]
        public IEnumerator BetweenShotsTheClipsPoseIsLeftAlone()
        {
            AgentWeapon weapon = Shooter(out AgentCannonAim aim, out _);
            weapon.RequestShot();
            yield return Seconds(0.2f);
            yield return EndOfFrame();
            Assert.AreEqual(0f, PitchError(weapon, 0), 1f);

            yield return Seconds(0.8f);   // the shot is gone, the kick has settled
            yield return EndOfFrame();
            Assert.IsFalse(weapon.IsBusy);
            Assert.AreEqual(0f, aim.Weight, 1e-3f);
            Assert.Greater(PitchError(weapon, 0), 15f, "Back to the clip's own pose. " + Describe(weapon));
        }

        [UnityTest]
        public IEnumerator TheShotLeavesFromTheBarrelsTipAndTheKickIsSmall()
        {
            AgentWeapon weapon = Shooter(out _, out AgentShotRecoil recoil);
            Vector3 tipAtShot = default;
            int barrelAtShot = -1;
            weapon.Fired += barrel =>
            {
                barrelAtShot = barrel;
                weapon.TryGetBarrel(barrel, out tipAtShot, out _);
            };
            LineRenderer line = weapon.GetComponentInChildren<LineRenderer>();

            weapon.RequestShot();
            float worstKick = 0f;
            float until = Time.time + 0.6f;
            while (Time.time < until)
            {
                yield return EndOfFrame();
                // While the tracer shows (the kick peaks 0.04 s after the shot). After it, the
                // aim eases out, and this stand-in clip, unlike the real Aim layer, does not.
                if (barrelAtShot >= 0 && recoil.IsKicking && weapon.IsBusy)
                    worstKick = Mathf.Max(worstKick, PitchError(weapon, barrelAtShot));
            }

            Assert.AreEqual(1, weapon.ShotsFired);
            Assert.Less(Vector3.Distance(line.GetPosition(0), tipAtShot), 0.05f, "The tracer starts at the tip of the barrel that fired.");
            Assert.Greater(worstKick, 3f, "The kick is still seen.");
            Assert.Less(worstKick, 10f, "But the barrel never points far above the shot (it was 28° more).");
        }
    }
}
