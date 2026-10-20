using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Tests
{
    public sealed class ImpactBurstTests
    {
        const float Tolerance = 1e-4f;

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        ImpactBurst NewBurst()
        {
            var go = new GameObject("Burst");
            _created.Add(go);
            return go.AddComponent<ImpactBurst>();
        }

        [Test]
        public void ItStartsJustOffTheSurfaceAndThrowsSparksAlongTheNormal()
        {
            ImpactBurst burst = NewBurst();

            burst.Play(new Vector3(1f, 2f, 3f), Vector3.up, Color.cyan);

            Assert.AreEqual(1f, burst.transform.position.x, Tolerance);
            Assert.Greater(burst.transform.position.y, 2f, "Lifted off the surface, not buried in it.");
            Assert.Less(burst.transform.position.y, 2.1f);
            Assert.AreEqual(3f, burst.transform.position.z, Tolerance);
            Assert.AreEqual(1f, Vector3.Dot(burst.transform.forward, Vector3.up), Tolerance);
            Assert.IsTrue(burst.IsPlaying);
        }

        [Test]
        public void TheSparksTakeTheShotsColour()
        {
            ImpactBurst burst = NewBurst();
            var gold = new Color(1f, 0.78f, 0.2f);

            burst.Play(Vector3.zero, Vector3.back, gold);

            Color start = burst.GetComponent<ParticleSystem>().main.startColor.color;
            Assert.AreEqual(gold.r, start.r, Tolerance);
            Assert.AreEqual(gold.g, start.g, Tolerance);
            Assert.AreEqual(gold.b, start.b, Tolerance);
        }

        [Test]
        public void ANormalOfZeroIsHandledWithoutAnErrorInTheConsole()
        {
            // Unity fails a test that logs an error or warning, so a bad look rotation would show here.
            ImpactBurst burst = NewBurst();

            Assert.DoesNotThrow(() => burst.Play(new Vector3(4f, 0f, 4f), Vector3.zero, Color.white));

            Assert.AreEqual(4f, burst.transform.position.x, Tolerance);
        }

        [UnityTest]
        public IEnumerator ItIsFinishedExactlyOnceAfterItsLifetime()
        {
            ImpactBurst burst = NewBurst();
            int finished = 0;
            burst.Finished += _ => finished++;

            burst.Play(Vector3.zero, Vector3.up, Color.white);
            float until = Time.realtimeSinceStartup + 3f;
            while (finished == 0 && Time.realtimeSinceStartup < until)
                yield return null;
            for (int i = 0; i < 5; i++)
                yield return null;

            Assert.AreEqual(1, finished);
            Assert.IsFalse(burst.IsPlaying);
        }

        [UnityTest]
        public IEnumerator ItCanBePlayedAgainAfterItHasFinished()
        {
            ImpactBurst burst = NewBurst();
            int finished = 0;
            burst.Finished += _ => finished++;

            for (int round = 1; round <= 2; round++)
            {
                burst.Play(new Vector3(round, 0f, 0f), Vector3.up, Color.white);
                float until = Time.realtimeSinceStartup + 3f;
                while (finished < round && Time.realtimeSinceStartup < until)
                    yield return null;
            }

            Assert.AreEqual(2, finished);
            Assert.AreEqual(2f, burst.transform.position.x, Tolerance);
        }
    }
}
