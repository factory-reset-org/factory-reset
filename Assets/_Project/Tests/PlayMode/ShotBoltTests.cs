using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Tests
{
    public sealed class ShotBoltTests
    {
        const float Tolerance = 1e-4f;

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        ShotBolt NewBolt()
        {
            var go = new GameObject("Bolt");
            _created.Add(go);
            return go.AddComponent<ShotBolt>();
        }

        static IEnumerator Until(System.Func<bool> condition, float seconds = 2f)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until)
                yield return null;
        }

        [TestCase(0f, 10f, 1.5f, 0f, 0f)]
        [TestCase(1f, 10f, 1.5f, 0f, 1f)]
        [TestCase(5f, 10f, 1.5f, 3.5f, 5f)]
        [TestCase(10f, 10f, 1.5f, 8.5f, 10f)]
        [TestCase(11.5f, 10f, 1.5f, 10f, 10f)]
        [TestCase(99f, 10f, 1.5f, 10f, 10f)]
        [TestCase(5f, 0f, 1.5f, 0f, 0f)]
        public void TheSegmentNeverGoesPastTheTarget(float travelled, float distance, float length, float tail, float head)
        {
            ShotBolt.SegmentAt(travelled, distance, length, out float actualTail, out float actualHead);

            Assert.AreEqual(tail, actualTail, Tolerance);
            Assert.AreEqual(head, actualHead, Tolerance);
        }

        [UnityTest]
        public IEnumerator ItArrivesOnceThenFinishesOnceAndEndsAtTheTarget()
        {
            ShotBolt bolt = NewBolt();
            var order = new List<string>();
            bolt.Arrived += _ => order.Add("arrived");
            bolt.Finished += _ => order.Add("finished");

            bolt.Launch(Vector3.zero, new Vector3(0f, 0f, 6f), Color.cyan, true, Vector3.back);
            Assert.IsTrue(bolt.IsFlying);
            yield return Until(() => !bolt.IsFlying);
            yield return null;
            yield return null;

            CollectionAssert.AreEqual(new[] { "arrived", "finished" }, order);
            Assert.IsFalse(bolt.IsFlying);
            var line = bolt.GetComponent<LineRenderer>();
            Assert.AreEqual(6f, line.GetPosition(1).z, Tolerance);
            Assert.AreEqual(6f, line.GetPosition(0).z, Tolerance, "The tail has caught up with the head.");
        }

        [UnityTest]
        public IEnumerator TheImpactDataIsOnTheBoltWhenItArrives()
        {
            ShotBolt bolt = NewBolt();
            Vector3 point = default, normal = default;
            Color colour = default;
            bool hasImpact = false;
            bolt.Arrived += b =>
            {
                point = b.ImpactPoint;
                normal = b.ImpactNormal;
                colour = b.Colour;
                hasImpact = b.HasImpact;
            };

            var gold = new Color(1f, 0.78f, 0.2f);
            bolt.Launch(new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 5f), gold, true, Vector3.up);
            yield return Until(() => !bolt.IsFlying);

            Assert.IsTrue(hasImpact);
            Assert.AreEqual(new Vector3(1f, 1f, 5f), point);
            Assert.AreEqual(Vector3.up, normal);
            Assert.AreEqual(gold, colour);
        }

        [UnityTest]
        public IEnumerator AShotThatHitNothingHasNoImpact()
        {
            ShotBolt bolt = NewBolt();
            bool hasImpact = true;
            bolt.Arrived += b => hasImpact = b.HasImpact;

            bolt.Launch(Vector3.zero, new Vector3(0f, 0f, 30f), Color.white, false, Vector3.back);
            yield return Until(() => !bolt.IsFlying);

            Assert.IsFalse(hasImpact);
        }

        [UnityTest]
        public IEnumerator AShotWithNoLengthStillArrivesAndFinishes()
        {
            ShotBolt bolt = NewBolt();
            int arrived = 0, finished = 0;
            bolt.Arrived += _ => arrived++;
            bolt.Finished += _ => finished++;

            bolt.Launch(Vector3.one, Vector3.one, Color.white, true, Vector3.up);
            yield return Until(() => !bolt.IsFlying);

            Assert.AreEqual(1, arrived);
            Assert.AreEqual(1, finished);
        }

        [UnityTest]
        public IEnumerator TheBoltFreezesWhileGameTimeIsStopped()
        {
            ShotBolt bolt = NewBolt();
            int arrived = 0;
            bolt.Arrived += _ => arrived++;
            Time.timeScale = 0f;

            bolt.Launch(Vector3.zero, new Vector3(0f, 0f, 6f), Color.white, true, Vector3.back);
            for (int i = 0; i < 10; i++)
                yield return null;

            Assert.IsTrue(bolt.IsFlying);
            Assert.AreEqual(0, arrived);
            Assert.AreEqual(0f, bolt.GetComponent<LineRenderer>().GetPosition(1).z, Tolerance);
        }

        [Test]
        public void TheTailFadesOutAndTheHeadIsTheShotsColour()
        {
            ShotBolt bolt = NewBolt();
            var colour = new Color(0.4f, 0.9f, 1f, 1f);

            bolt.Launch(Vector3.zero, Vector3.forward * 10f, colour, false, Vector3.back);

            var line = bolt.GetComponent<LineRenderer>();
            Assert.AreEqual(0f, line.startColor.a, Tolerance);
            Assert.AreEqual(1f, line.endColor.a, Tolerance);
            Assert.GreaterOrEqual(line.endColor.r, colour.r - Tolerance, "The core is the shot's colour pushed towards white.");
            Assert.GreaterOrEqual(line.endColor.g, colour.g - Tolerance);
            Assert.Greater(line.widthMultiplier, 0f);
            Assert.AreEqual(colour, bolt.Colour, "The bolt remembers the shot's own colour for the sparks.");
        }

        [UnityTest]
        public IEnumerator TheGlowFollowsTheHeadAndGoesOutWhenItArrives()
        {
            ShotBolt bolt = NewBolt();
            var glow = new GameObject("Glow");
            _created.Add(glow);
            glow.transform.SetParent(bolt.transform, false);
            var glowRenderer = glow.AddComponent<MeshRenderer>();
            glow.AddComponent<MeshFilter>();
            typeof(ShotBolt).GetField("glow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(bolt, glow.transform);
            typeof(ShotBolt).GetField("glowRenderer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(bolt, glowRenderer);

            bolt.Launch(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 30f), Color.cyan, true, Vector3.back);

            Assert.IsTrue(glowRenderer.enabled);
            Assert.AreEqual(0f, glow.transform.position.z, Tolerance, "At the muzzle to begin with.");
            yield return null;
            yield return null;
            Assert.Greater(glow.transform.position.z, 0f, "It moves with the head.");
            Assert.Less(glow.transform.position.z, 30.01f);

            float until = Time.realtimeSinceStartup + 2f;
            while (bolt.IsFlying && Time.realtimeSinceStartup < until)
                yield return null;

            Assert.IsFalse(glowRenderer.enabled, "The glow goes out on impact.");
            Assert.AreEqual(30f, glow.transform.position.z, Tolerance, "It was last at the target, never past it.");
        }

        [Test]
        public void ItIsPlacedAtTheMuzzleStraightAway()
        {
            ShotBolt bolt = NewBolt();

            bolt.Launch(new Vector3(2f, 3f, 4f), new Vector3(2f, 3f, 20f), Color.white, false, Vector3.back);

            var line = bolt.GetComponent<LineRenderer>();
            Assert.AreEqual(new Vector3(2f, 3f, 4f), line.GetPosition(0));
            Assert.AreEqual(new Vector3(2f, 3f, 4f), line.GetPosition(1));
        }
    }
}
