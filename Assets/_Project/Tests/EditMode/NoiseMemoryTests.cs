using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Tracker;

namespace ToyFactory.Tests.EditMode
{
    public class NoiseMemoryTests
    {
        [Test]
        public void ScoreDecaysExponentiallyWithAge()
        {
            Assert.AreEqual(50f, NoiseMemory.Score(50f, 0f), 1e-4f);
            Assert.AreEqual(50f * Mathf.Exp(-1.5f), NoiseMemory.Score(50f, 5f), 1e-4f);
            Assert.AreEqual(50f, NoiseMemory.Score(50f, -1f), 1e-4f, "A future noise is not boosted.");
        }

        [Test]
        public void EmptyMemoryHasNoBestNoise()
        {
            Assert.IsFalse(new NoiseMemory().TryGetBest(0f, Vector3.zero, out _));
        }

        [Test]
        public void RepeatingBeepBeatsAnOlderLouderShot()
        {
            var memory = new NoiseMemory();
            memory.Remember(1, new Vector3(10f, 0f, 0f), 80f, 0f);     // shot
            memory.Remember(2, new Vector3(0f, 0f, 10f), 30f, 4.0f);   // terminal beeps every 0.8 s
            memory.Remember(2, new Vector3(0f, 0f, 10f), 30f, 4.8f);

            Assert.IsTrue(memory.TryGetBest(5f, Vector3.zero, out NoiseTarget best));
            Assert.AreEqual(2, best.SourceId);
            Assert.IsTrue(best.IsRepeating);
            Assert.AreEqual(30f * Mathf.Exp(-0.3f * 0.2f), best.Score, 1e-3f);
        }

        [Test]
        public void ASingleNoiseIsNotRepeating()
        {
            var memory = new NoiseMemory();
            memory.Remember(1, Vector3.zero, 50f, 0f);

            Assert.IsTrue(memory.TryGetBest(0.1f, Vector3.zero, out NoiseTarget best));
            Assert.IsFalse(best.IsRepeating);
            Assert.IsFalse(memory.IsStillRepeating(1, 0.1f));
        }

        [Test]
        public void ASourceStopsRepeatingAfterOnePointFiveSecondsOfSilence()
        {
            var memory = new NoiseMemory();
            memory.Remember(7, Vector3.zero, 60f, 0f);
            memory.Remember(7, Vector3.zero, 60f, 0.8f);

            Assert.IsTrue(memory.IsStillRepeating(7, 2.2f));
            Assert.IsFalse(memory.IsStillRepeating(7, 2.4f));
        }

        [Test]
        public void NoisesFurtherApartThanTheWindowDoNotCountAsRepeating()
        {
            var memory = new NoiseMemory();
            memory.Remember(7, Vector3.zero, 60f, 0f);
            memory.Remember(7, Vector3.zero, 60f, 2f);

            Assert.IsFalse(memory.IsStillRepeating(7, 2f));
        }

        [Test]
        public void EqualScoresPreferTheCloserNoise()
        {
            var memory = new NoiseMemory();
            memory.Remember(1, new Vector3(5f, 0f, 0f), 40f, 1f);
            memory.Remember(2, new Vector3(0f, 0f, 2f), 40f, 1f);

            Assert.IsTrue(memory.TryGetBest(1f, Vector3.zero, out NoiseTarget best));
            Assert.AreEqual(2, best.SourceId);
        }

        [Test]
        public void HandledNoiseIsIgnoredUntilTheSourceMakesANewOne()
        {
            var memory = new NoiseMemory();
            memory.Remember(3, Vector3.zero, 50f, 0f);
            memory.MarkHandled(3);
            Assert.IsFalse(memory.TryGetBest(0.5f, Vector3.zero, out _));

            memory.Remember(3, Vector3.one, 50f, 1f);
            Assert.IsTrue(memory.TryGetBest(1f, Vector3.zero, out NoiseTarget best));
            Assert.AreEqual(Vector3.one, best.Position);
        }

        [Test]
        public void NoiseIsForgottenOnceItsScoreFallsToTheHearingThreshold()
        {
            var memory = new NoiseMemory();
            memory.Remember(1, Vector3.zero, 20f, 0f);   // 20 e^(-0.3 t) = 10 at t = 2.31 s

            Assert.IsTrue(memory.TryGetBest(2.2f, Vector3.zero, out _));
            Assert.IsFalse(memory.TryGetBest(2.4f, Vector3.zero, out _));
            Assert.IsFalse(memory.TryGetPosition(1, out _));
        }

        [Test]
        public void AnOlderNoiseFromTheSameSourceIsIgnored()
        {
            var memory = new NoiseMemory();
            memory.Remember(1, Vector3.one, 50f, 2f);
            memory.Remember(1, Vector3.zero, 90f, 1f);

            Assert.IsTrue(memory.TryGetPosition(1, out Vector3 position));
            Assert.AreEqual(Vector3.one, position);
        }

        [Test]
        public void AFullMemoryReplacesItsWeakestEntry()
        {
            var memory = new NoiseMemory(capacity: 2);
            memory.Remember(1, Vector3.zero, 30f, 0f);
            memory.Remember(2, Vector3.zero, 90f, 0f);
            memory.Remember(3, Vector3.zero, 60f, 0f);

            Assert.IsFalse(memory.TryGetPosition(1, out _));
            Assert.IsTrue(memory.TryGetPosition(2, out _));
            Assert.IsTrue(memory.TryGetPosition(3, out _));
        }

        // ---- CopyTo (debug overlay) -----------------------------------------------------

        [Test]
        public void CopyToListsEveryRememberedSourceWithItsFlags()
        {
            var memory = new NoiseMemory();
            memory.Remember(1, new Vector3(1f, 0f, 0f), 60f, 0f);
            memory.Remember(2, new Vector3(2f, 0f, 0f), 30f, 0.2f);
            memory.Remember(2, new Vector3(2f, 0f, 0f), 30f, 1.0f);   // repeating
            memory.MarkHandled(1);
            var noises = new List<RememberedNoise>();

            memory.CopyTo(1.0f, noises);

            Assert.AreEqual(2, noises.Count, "Handled sources are listed too.");
            RememberedNoise shot = noises.Find(n => n.SourceId == 1);
            RememberedNoise beep = noises.Find(n => n.SourceId == 2);
            Assert.IsTrue(shot.IsHandled);
            Assert.IsFalse(shot.IsRepeating);
            Assert.AreEqual(NoiseMemory.Score(60f, 1f), shot.Score, 1e-4f);
            Assert.AreEqual(60f, shot.Level);
            Assert.IsTrue(beep.IsRepeating);
            Assert.IsFalse(beep.IsHandled);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), beep.Position);
        }

        [Test]
        public void CopyToLeavesOutDecayedNoisesAndChangesNothing()
        {
            var memory = new NoiseMemory();
            memory.Remember(1, Vector3.zero, 20f, 0f);   // score 8.1 at 3 s: below the threshold
            memory.Remember(2, Vector3.zero, 90f, 0f);
            var noises = new List<RememberedNoise>();

            memory.CopyTo(3f, noises);

            Assert.AreEqual(1, noises.Count);
            Assert.AreEqual(2, noises[0].SourceId);
            Assert.IsTrue(memory.TryGetPosition(1, out _), "Looking does not forget anything; only the brain's own queries do.");
            Assert.Throws<System.ArgumentNullException>(() => memory.CopyTo(0f, null));
        }
    }
}
