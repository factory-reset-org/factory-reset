using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Animation;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The remade Captain's real body: its kneel rests on the floor where it was measured (front
    /// boot flat, back knee and boot tip down, cannon planted) and getting back up never pushes
    /// it through the floor; its cannon charges through the aim, flashes, slams back and
    /// settles, and its shots leave from the muzzle.
    /// </summary>
    public sealed class CaptainBodyTests
    {
        readonly List<Object> _created = new List<Object>();

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

        // Poses are measured after LateUpdate, where the kneel and the cannon write theirs.
        static WaitForEndOfFrame EndOfFrame() => new WaitForEndOfFrame();

        AgentController Captain(bool awake)
        {
            AgentBodyFixture.Floor(_created);
            AgentController captain = AgentBodyFixture.Body("CaptainBot", AgentType.Captain, new StateNameBrain(), _created);
            if (awake)
                captain.World.SetCaptainAwake();
            return captain;
        }

        // The lowest point of the meshes under a part, above the body's feet (the capsule's skin
        // holds the body a few centimetres above the floor plane, so not world y 0).
        static float Lowest(Transform part)
            => LowestWorld(part) - part.GetComponentInParent<AgentController>().transform.position.y;

        static float LowestWorld(Transform part)
        {
            float lowest = float.MaxValue;
            foreach (MeshFilter mesh in part.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                    lowest = Mathf.Min(lowest, mesh.transform.TransformPoint(vertex).y);
            return lowest;
        }

        static Transform Part(AgentController captain, string name)
        {
            foreach (Transform part in captain.GetComponentsInChildren<Transform>(true))
                if (part.name == name)
                    return part;
            Assert.Fail("No part " + name);
            return null;
        }

        [UnityTest]
        public IEnumerator TheDormantCaptainKneelsOnTheFloorWhereItWasMeasured()
        {
            AgentController captain = Captain(awake: false);
            yield return null;
            yield return EndOfFrame();
            Assert.AreEqual(1f, captain.GetComponent<AgentKneel>().Amount, "Kneeling from the first frame.");

            Assert.That(Lowest(captain.transform.Find("CaptainBot")), Is.EqualTo(0f).Within(0.006f), "Resting on the floor, not in it.");
            Assert.Less(Lowest(Part(captain, "Sole_L")), 0.006f, "The front boot stands on the floor.");
            Assert.Less(Lowest(Part(captain, "KneeCap_R")), 0.006f, "The back knee rests on it.");
            Assert.Less(Lowest(Part(captain, "Ankle_R_Pivot")), 0.006f, "The back boot stands on its tip.");
            Assert.Less(Lowest(Part(captain, "Barrel_Pivot")), 0.01f, "The cannon is planted muzzle-down.");
            Assert.That(Lowest(Part(captain, "Fist_L")), Is.InRange(0.7f, 0.9f), "The fist rests at the front knee.");
        }

        [UnityTest]
        public IEnumerator GettingUpNeverPushesItThroughTheFloor()
        {
            AgentController captain = Captain(awake: false);
            AgentKneel kneel = captain.GetComponent<AgentKneel>();
            Transform model = captain.transform.Find("CaptainBot");
            yield return null;
            captain.World.SetCaptainAwake();   // the wake: the visor flickers on, then it stands

            float lowest = float.MaxValue, highest = float.MinValue;
            float until = Time.time + 2.2f;
            while (Time.time < until)
            {
                yield return EndOfFrame();
                float low = Lowest(model);
                lowest = Mathf.Min(lowest, low);
                if (kneel.Amount < 1f)
                    highest = Mathf.Max(highest, low);
            }
            Assert.AreEqual(0f, kneel.Amount, "Standing.");
            Assert.Greater(lowest, -0.03f, "Nothing goes more than a bevel into the floor on the way up.");
            Assert.Less(highest, 0.03f, "And it never floats: a boot or a knee is always down.");
        }

        AgentWeapon ShootingCaptain(out CaptainCannon cannon, out FakeShotPlayer player)
        {
            AgentController captain = Captain(awake: true);
            cannon = captain.GetComponent<CaptainCannon>();
            var target = new GameObject("Player");
            _created.Add(target);
            target.layer = LayerMask.NameToLayer("Player");
            target.transform.position = new Vector3(0f, 0f, 7f);
            target.AddComponent<CharacterController>().center = new Vector3(0f, 0.9f, 0f);
            player = target.AddComponent<FakeShotPlayer>();
            PlayerState.Publish(player);
            return captain.GetComponent<AgentWeapon>();
        }

        [UnityTest]
        public IEnumerator TheCellChargesThroughTheAimAndTheBarrelCreepsOut()
        {
            AgentWeapon weapon = ShootingCaptain(out CaptainCannon cannon, out _);
            yield return Seconds(0.1f);
            yield return EndOfFrame();
            Assert.AreEqual(0f, cannon.Charge);
            Assert.AreEqual(1f, cannon.CellGlow, 1e-3f, "Glowing steadily at rest.");

            weapon.RequestShot();
            yield return Seconds(0.2f);
            yield return EndOfFrame();
            Assert.IsTrue(weapon.IsAiming);
            Assert.Greater(cannon.Charge, 0.5f);
            Assert.Greater(cannon.CellGlow, 1.8f, "Brighter as it charges: the telegraph.");
            Assert.Less(cannon.BarrelOffset, -0.01f, "The barrel creeps out.");
        }

        [UnityTest]
        public IEnumerator TheShotLeavesTheMuzzleFlashesAndTheBarrelSlamsBackThenSettles()
        {
            AgentWeapon weapon = ShootingCaptain(out CaptainCannon cannon, out _);
            Transform muzzle = Part(weapon.GetComponent<AgentController>(), "Muzzle_Point");
            Vector3 muzzleAtShot = default;
            weapon.Fired += _ => muzzleAtShot = muzzle.position;
            yield return null;

            weapon.RequestShot();
            float slam = 0f, dimmest = float.MaxValue;
            float until = Time.time + 0.6f;
            while (Time.time < until)
            {
                yield return EndOfFrame();
                if (weapon.ShotsFired == 0)
                    continue;
                slam = Mathf.Max(slam, cannon.BarrelOffset);
                dimmest = Mathf.Min(dimmest, cannon.CellGlow);
            }

            Assert.AreEqual(1, weapon.ShotsFired);
            Assert.AreEqual(1, cannon.Flashes);
            Assert.Less(Vector3.Distance(weapon.LastShotFrom, muzzleAtShot), 0.05f, "The shot leaves from the muzzle point.");
            Assert.Greater(slam, 0.1f, "The barrel slams back along the forearm.");
            Assert.Less(dimmest, 0.5f, "The spent cell goes dim.");

            yield return Seconds(0.5f);
            yield return EndOfFrame();
            Assert.AreEqual(0f, cannon.BarrelOffset, 0.005f, "And springs back out to rest.");
            Assert.AreEqual(1f, cannon.CellGlow, 0.01f, "Recharged.");
        }

        [UnityTest]
        public IEnumerator TheCannonIsDarkWhileTheCaptainIsDormant()
        {
            AgentController captain = Captain(awake: false);
            yield return null;
            yield return EndOfFrame();
            var cell = Part(captain, "Cannon_Cell").GetComponent<Renderer>();
            Color glow = cell.sharedMaterial.GetColor("_EmissionColor");
            Assert.Less(glow.maxColorComponent, 0.01f, "Its cell is out, like its visor.");

            captain.World.SetCaptainAwake();
            yield return Seconds(1f);
            yield return EndOfFrame();
            Assert.Greater(cell.sharedMaterial.GetColor("_EmissionColor").maxColorComponent, 1f, "And lights with it at the wake.");
        }
    }
}
