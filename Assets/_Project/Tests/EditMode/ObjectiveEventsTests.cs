using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using ToyFactory.Interfaces;
using UnityEngine;

namespace ToyFactory.Tests
{
    public sealed class ObjectiveEventsTests
    {
        static readonly MethodInfo ClearListenersMethod = typeof(ObjectiveEvents).GetMethod(
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
        public void ObjectiveKindDefinesRequiredValuesInOrder()
        {
            Assert.That(Enum.GetValues(typeof(ObjectiveKind)), Is.EqualTo(new[]
            {
                ObjectiveKind.Task,
                ObjectiveKind.Switch,
                ObjectiveKind.Console,
                ObjectiveKind.Battery
            }));
        }

        [TestCase(ObjectiveKind.Task)]
        [TestCase(ObjectiveKind.Switch)]
        [TestCase(ObjectiveKind.Console)]
        [TestCase(ObjectiveKind.Battery)]
        public void ObjectiveTargetInfoStoresEveryKind(ObjectiveKind kind)
        {
            var target = new ObjectiveTargetInfo(17, new Vector3(1f, 2f, 3f), kind);

            Assert.That(target.Id, Is.EqualTo(17));
            Assert.That(target.Position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(target.Kind, Is.EqualTo(kind));
        }

        [TestCase("Id")]
        [TestCase("Position")]
        [TestCase("Kind")]
        public void ObjectiveTargetInfoPropertiesAreReadOnly(string propertyName)
        {
            PropertyInfo property = typeof(ObjectiveTargetInfo).GetProperty(propertyName);

            Assert.That(property, Is.Not.Null);
            Assert.That(property.CanWrite, Is.False);
        }

        [Test]
        public void ObjectiveTargetInfoIsReadonlyValueType()
        {
            Type type = typeof(ObjectiveTargetInfo);

            Assert.That(type.IsValueType, Is.True);
            Assert.That(type.IsDefined(typeof(IsReadOnlyAttribute), false), Is.True);
        }

        [Test]
        public void RaiseTargetsChangedSynchronouslyForwardsExactList()
        {
            IReadOnlyList<ObjectiveTargetInfo> expected = new[]
            {
                new ObjectiveTargetInfo(1, Vector3.left, ObjectiveKind.Task),
                new ObjectiveTargetInfo(2, Vector3.right, ObjectiveKind.Switch)
            };
            IReadOnlyList<ObjectiveTargetInfo> received = null;
            bool returned = false;
            ObjectiveEvents.OnTargetsChanged += targets =>
            {
                Assert.That(returned, Is.False);
                received = targets;
            };

            ObjectiveEvents.RaiseTargetsChanged(expected);
            returned = true;

            Assert.That(received, Is.SameAs(expected));
        }

        [Test]
        public void RaiseTargetsChangedForwardsNullWithoutValidation()
        {
            IReadOnlyList<ObjectiveTargetInfo> received = Array.Empty<ObjectiveTargetInfo>();
            ObjectiveEvents.OnTargetsChanged += targets => received = targets;

            ObjectiveEvents.RaiseTargetsChanged(null);

            Assert.That(received, Is.Null);
        }

        [Test]
        public void RaiseTargetsChangedNotifiesEverySubscriber()
        {
            int firstCalls = 0;
            int secondCalls = 0;
            ObjectiveEvents.OnTargetsChanged += _ => firstCalls++;
            ObjectiveEvents.OnTargetsChanged += _ => secondCalls++;

            ObjectiveEvents.RaiseTargetsChanged(Array.Empty<ObjectiveTargetInfo>());

            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.EqualTo(1));
        }

        [Test]
        public void RaiseTargetsChangedWithoutSubscribersDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                ObjectiveEvents.RaiseTargetsChanged(Array.Empty<ObjectiveTargetInfo>()));
        }

        [Test]
        public void RemovedSubscriberIsNotNotified()
        {
            int calls = 0;
            Action<IReadOnlyList<ObjectiveTargetInfo>> listener = _ => calls++;
            ObjectiveEvents.OnTargetsChanged += listener;
            ObjectiveEvents.OnTargetsChanged -= listener;

            ObjectiveEvents.RaiseTargetsChanged(Array.Empty<ObjectiveTargetInfo>());

            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void SubsystemRegistrationCleanupRemovesListeners()
        {
            int calls = 0;
            ObjectiveEvents.OnTargetsChanged += _ => calls++;

            ClearListeners();
            ObjectiveEvents.RaiseTargetsChanged(Array.Empty<ObjectiveTargetInfo>());

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
