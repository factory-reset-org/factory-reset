using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using ToyFactory.Interfaces;
using UnityEngine;

namespace ToyFactory.Tests
{
    public sealed class NoiseEventTests
    {
        static readonly MethodInfo ClearListenersMethod = typeof(NoiseEvents).GetMethod(
            "ClearListeners", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            ClearListeners();
        }

        [TearDown]
        public void TearDown()
        {
            ClearListeners();
        }

        [Test]
        public void ConstructorStoresAllValues()
        {
            var position = new Vector3(1.5f, -2f, 8.25f);

            var noise = new NoiseEvent(position, 72.5f, 14, 3.75f);

            Assert.That(noise.Position, Is.EqualTo(position));
            Assert.That(noise.Loudness, Is.EqualTo(72.5f));
            Assert.That(noise.SourceId, Is.EqualTo(14));
            Assert.That(noise.Time, Is.EqualTo(3.75f));
        }

        [Test]
        public void PlayerSourceIdIsPreserved()
        {
            var noise = new NoiseEvent(Vector3.zero, 0f, -1, 0f);

            Assert.That(noise.SourceId, Is.EqualTo(-1));
        }

        [TestCase("Position")]
        [TestCase("Loudness")]
        [TestCase("SourceId")]
        [TestCase("Time")]
        public void PublicPropertiesAreReadOnly(string propertyName)
        {
            PropertyInfo property = typeof(NoiseEvent).GetProperty(propertyName);

            Assert.That(property, Is.Not.Null);
            Assert.That(property.CanWrite, Is.False);
        }

        [Test]
        public void NoiseEventIsReadonlyValueType()
        {
            Type type = typeof(NoiseEvent);

            Assert.That(type.IsValueType, Is.True);
            Assert.That(type.IsDefined(typeof(IsReadOnlyAttribute), false), Is.True);
        }

        [Test]
        public void EmitInvokesSubscriberOnceWithEventValues()
        {
            var expected = new NoiseEvent(new Vector3(4f, 5f, 6f), 50f, 9, 12f);
            NoiseEvent received = default;
            int calls = 0;
            NoiseEvents.OnNoise += e =>
            {
                received = e;
                calls++;
            };

            NoiseEvents.Emit(expected);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(received.Position, Is.EqualTo(expected.Position));
            Assert.That(received.Loudness, Is.EqualTo(expected.Loudness));
            Assert.That(received.SourceId, Is.EqualTo(expected.SourceId));
            Assert.That(received.Time, Is.EqualTo(expected.Time));
        }

        [Test]
        public void EmitNotifiesEverySubscriber()
        {
            int firstCalls = 0;
            int secondCalls = 0;
            NoiseEvents.OnNoise += _ => firstCalls++;
            NoiseEvents.OnNoise += _ => secondCalls++;

            NoiseEvents.Emit(default);

            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.EqualTo(1));
        }

        [Test]
        public void EmitWithoutSubscribersDoesNotThrow()
        {
            Assert.DoesNotThrow(() => NoiseEvents.Emit(default));
        }

        [Test]
        public void RemovedSubscriberIsNotNotified()
        {
            int calls = 0;
            Action<NoiseEvent> listener = _ => calls++;
            NoiseEvents.OnNoise += listener;
            NoiseEvents.OnNoise -= listener;

            NoiseEvents.Emit(default);

            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void SubsystemRegistrationCleanupRemovesListeners()
        {
            int calls = 0;
            NoiseEvents.OnNoise += _ => calls++;

            ClearListeners();
            NoiseEvents.Emit(default);

            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void CleanupUsesSubsystemRegistration()
        {
            var attribute = ClearListenersMethod.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.loadType,
                Is.EqualTo(RuntimeInitializeLoadType.SubsystemRegistration));
        }

        static void ClearListeners()
        {
            Assert.That(ClearListenersMethod, Is.Not.Null);
            ClearListenersMethod.Invoke(null, null);
        }
    }
}
