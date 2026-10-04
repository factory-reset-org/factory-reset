using System;
using NUnit.Framework;
using ToyFactory.AI.Agents.Tracker;

namespace ToyFactory.Tests.EditMode
{
    public class WindUpEnergyTests
    {
        const float Tolerance = 1e-4f;

        [Test]
        public void StartsFullAndNotRewinding()
        {
            var energy = new WindUpEnergy();
            Assert.AreEqual(WindUpEnergy.MaxEnergy, energy.Energy);
            Assert.AreEqual(1f, energy.Energy01);
            Assert.IsFalse(energy.IsRewinding);
        }

        [Test]
        public void FirstTickOnlyRecordsTheTime()
        {
            var energy = new WindUpEnergy();
            energy.Tick(50f, true);
            Assert.AreEqual(WindUpEnergy.MaxEnergy, energy.Energy);
        }

        [TestCase(true, 50f)]
        [TestCase(false, 90f)]
        public void FiveSecondsDrainsByState(bool chasing, float expected)
        {
            var energy = new WindUpEnergy();
            energy.Tick(0f, chasing);
            energy.Tick(5f, chasing);
            Assert.AreEqual(expected, energy.Energy, Tolerance);
        }

        [Test]
        public void DrainingToZeroStartsRewindingAndNeverGoesNegative()
        {
            var energy = new WindUpEnergy();
            energy.Tick(0f, true);
            energy.Tick(20f, true);   // 200 worth of drain from a 100 spring
            Assert.AreEqual(0f, energy.Energy);
            Assert.IsTrue(energy.IsRewinding);
        }

        [Test]
        public void RewindIgnoresChasingAndIsFullAfterExactlyThreeSeconds()
        {
            var energy = new WindUpEnergy();
            energy.Tick(0f, true);
            energy.Tick(10f, true);
            Assert.IsTrue(energy.IsRewinding);

            energy.Tick(11f, true);
            Assert.AreEqual(WindUpEnergy.MaxEnergy / 3f, energy.Energy, Tolerance);
            energy.Tick(12f, true);
            Assert.IsTrue(energy.IsRewinding);
            Assert.Less(energy.Energy, WindUpEnergy.MaxEnergy);

            energy.Tick(13f, true);
            Assert.AreEqual(WindUpEnergy.MaxEnergy, energy.Energy);
            Assert.IsFalse(energy.IsRewinding);

            energy.Tick(14f, true);   // draining again once rewound
            Assert.AreEqual(WindUpEnergy.MaxEnergy - WindUpEnergy.ChaseDrain, energy.Energy, Tolerance);
        }

        [Test]
        public void Energy01StaysBetweenZeroAndOne()
        {
            var energy = new WindUpEnergy();
            for (int i = 0; i <= 400; i++)
            {
                energy.Tick(i * 0.25f, i % 3 != 0);
                Assert.GreaterOrEqual(energy.Energy01, 0f);
                Assert.LessOrEqual(energy.Energy01, 1f);
            }
        }

        [Test]
        public void ResumeAfterStunGapDoesNotDrain()
        {
            var energy = new WindUpEnergy();
            energy.Tick(0f, false);
            energy.Tick(1f, false);
            Assert.AreEqual(98f, energy.Energy, Tolerance);

            energy.Resume(8f);   // 7 s stun: the brain was not ticked
            Assert.AreEqual(98f, energy.Energy, Tolerance);

            energy.Tick(9f, false);   // only this tick's own 1 s drains
            Assert.AreEqual(96f, energy.Energy, Tolerance);
        }

        [Test]
        public void TimeGoingBackwardsDoesNotChangeEnergy()
        {
            var energy = new WindUpEnergy();
            energy.Tick(10f, true);
            energy.Tick(5f, true);
            Assert.AreEqual(WindUpEnergy.MaxEnergy, energy.Energy);
        }

        [Test]
        public void RepeatedTicksAllocateZeroBytes()
        {
            var energy = new WindUpEnergy();
            for (int i = 0; i < 100; i++)
                energy.Tick(i * 0.1f, true);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 100; i < 1100; i++)
                energy.Tick(i * 0.1f, i % 2 == 0);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated);
        }
    }
}
