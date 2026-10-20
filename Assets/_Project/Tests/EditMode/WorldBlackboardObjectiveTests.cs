using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.Tests.EditMode;
using UnityEngine;

namespace ToyFactory.Tests
{
    public sealed class WorldBlackboardObjectiveTests
    {
        [Test]
        public void NewBlackboardStartsEmptyAtVersionZero()
        {
            var blackboard = new WorldBlackboard();

            Assert.That(blackboard.ObjectiveTargets, Is.Empty);
            Assert.That(blackboard.ObjectivesVersion, Is.Zero);
        }

        [TestCase(ObjectiveTargetKind.Task)]
        [TestCase(ObjectiveTargetKind.Switch)]
        [TestCase(ObjectiveTargetKind.Console)]
        [TestCase(ObjectiveTargetKind.Battery)]
        public void ObjectiveTargetPreservesValues(ObjectiveTargetKind kind)
        {
            var target = new ObjectiveTarget(-7, new Vector2Int(3, 5), kind);

            Assert.That(target.Id, Is.EqualTo(-7));
            Assert.That(target.Cell, Is.EqualTo(new Vector2Int(3, 5)));
            Assert.That(target.Kind, Is.EqualTo(kind));
        }

        [TestCase("Id")]
        [TestCase("Cell")]
        [TestCase("Kind")]
        public void ObjectiveTargetPropertiesAreReadOnly(string propertyName)
        {
            PropertyInfo property = typeof(ObjectiveTarget).GetProperty(propertyName);

            Assert.That(property, Is.Not.Null);
            Assert.That(property.CanWrite, Is.False);
        }

        [Test]
        public void ObjectiveTargetIsReadonlyValueType()
        {
            Type type = typeof(ObjectiveTarget);

            Assert.That(type.IsValueType, Is.True);
            Assert.That(type.IsDefined(typeof(IsReadOnlyAttribute), false), Is.True);
        }

        [Test]
        public void FirstUpdatePreservesOrderAndIncrementsOnce()
        {
            var blackboard = new WorldBlackboard();
            ObjectiveTarget first = Target(1, 1, 2, ObjectiveTargetKind.Task);
            ObjectiveTarget second = Target(2, 4, 3, ObjectiveTargetKind.Switch);

            blackboard.SetObjectiveTargets(new[] { first, second });

            Assert.That(blackboard.ObjectivesVersion, Is.EqualTo(1));
            AssertTarget(blackboard.ObjectiveTargets[0], first);
            AssertTarget(blackboard.ObjectiveTargets[1], second);
        }

        [Test]
        public void SuppliedMutableCollectionIsCopied()
        {
            var blackboard = new WorldBlackboard();
            ObjectiveTarget original = Target(1, 1, 1, ObjectiveTargetKind.Task);
            var supplied = new List<ObjectiveTarget> { original };

            blackboard.SetObjectiveTargets(supplied);
            supplied[0] = Target(9, 9, 9, ObjectiveTargetKind.Battery);
            supplied.Clear();

            Assert.That(blackboard.ObjectiveTargets, Has.Count.EqualTo(1));
            AssertTarget(blackboard.ObjectiveTargets[0], original);
            Assert.That(blackboard.ObjectivesVersion, Is.EqualTo(1));
        }

        [Test]
        public void ExposedCollectionCannotBeMutated()
        {
            var blackboard = new WorldBlackboard();
            blackboard.SetObjectiveTargets(new[]
            {
                Target(1, 1, 1, ObjectiveTargetKind.Task)
            });
            var list = (IList<ObjectiveTarget>)blackboard.ObjectiveTargets;

            Assert.That(list.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() =>
                list.Add(Target(2, 2, 2, ObjectiveTargetKind.Switch)));
            Assert.Throws<NotSupportedException>(() => list.Clear());
        }

        [Test]
        public void ReadsUseStableViewAndAllocateZeroBytes()
        {
            var blackboard = new WorldBlackboard();
            blackboard.SetObjectiveTargets(new[]
            {
                Target(1, 1, 1, ObjectiveTargetKind.Task)
            });
            IReadOnlyList<ObjectiveTarget> expected = blackboard.ObjectiveTargets;

            for (int i = 0; i < 100; i++)
            {
                _ = blackboard.ObjectiveTargets;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            int checksum = 0;
            int allocated = GcAllocations.Count(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    IReadOnlyList<ObjectiveTarget> actual = blackboard.ObjectiveTargets;
                    checksum += actual[0].Id;
                }

            });
            Assert.That(blackboard.ObjectiveTargets, Is.SameAs(expected));
            Assert.That(checksum, Is.EqualTo(1000));
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void IdenticalUpdateDoesNotIncrementVersion()
        {
            var blackboard = BlackboardWithTwoTargets();

            blackboard.SetObjectiveTargets(new[]
            {
                Target(1, 1, 1, ObjectiveTargetKind.Task),
                Target(2, 2, 2, ObjectiveTargetKind.Switch)
            });

            Assert.That(blackboard.ObjectivesVersion, Is.EqualTo(1));
        }

        [Test]
        public void IdChangeIncrementsVersion()
        {
            AssertSingleChangeIncrements(
                Target(9, 1, 1, ObjectiveTargetKind.Task),
                Target(2, 2, 2, ObjectiveTargetKind.Switch));
        }

        [Test]
        public void CellChangeIncrementsVersion()
        {
            AssertSingleChangeIncrements(
                Target(1, 9, 1, ObjectiveTargetKind.Task),
                Target(2, 2, 2, ObjectiveTargetKind.Switch));
        }

        [Test]
        public void KindChangeIncrementsVersion()
        {
            AssertSingleChangeIncrements(
                Target(1, 1, 1, ObjectiveTargetKind.Console),
                Target(2, 2, 2, ObjectiveTargetKind.Switch));
        }

        [Test]
        public void CountChangeIncrementsVersion()
        {
            var blackboard = BlackboardWithTwoTargets();

            blackboard.SetObjectiveTargets(new[]
            {
                Target(1, 1, 1, ObjectiveTargetKind.Task)
            });

            Assert.That(blackboard.ObjectivesVersion, Is.EqualTo(2));
        }

        [Test]
        public void OrderingChangeIncrementsVersionAndPreservesNewOrder()
        {
            var blackboard = BlackboardWithTwoTargets();
            ObjectiveTarget first = blackboard.ObjectiveTargets[0];
            ObjectiveTarget second = blackboard.ObjectiveTargets[1];

            blackboard.SetObjectiveTargets(new[] { second, first });

            Assert.That(blackboard.ObjectivesVersion, Is.EqualTo(2));
            AssertTarget(blackboard.ObjectiveTargets[0], second);
            AssertTarget(blackboard.ObjectiveTargets[1], first);
        }

        [Test]
        public void ClearingNonEmptyListIncrementsOnce()
        {
            var blackboard = BlackboardWithTwoTargets();

            blackboard.SetObjectiveTargets(Array.Empty<ObjectiveTarget>());

            Assert.That(blackboard.ObjectiveTargets, Is.Empty);
            Assert.That(blackboard.ObjectivesVersion, Is.EqualTo(2));
        }

        [Test]
        public void ClearingEmptyListIsNoOp()
        {
            var blackboard = new WorldBlackboard();

            blackboard.SetObjectiveTargets(Array.Empty<ObjectiveTarget>());

            Assert.That(blackboard.ObjectiveTargets, Is.Empty);
            Assert.That(blackboard.ObjectivesVersion, Is.Zero);
        }

        [Test]
        public void NullUpdateIsRejectedAndLeavesExistingStateUntouched()
        {
            var blackboard = BlackboardWithTwoTargets();
            IReadOnlyList<ObjectiveTarget> view = blackboard.ObjectiveTargets;

            var exception = Assert.Throws<ArgumentNullException>(() =>
                blackboard.SetObjectiveTargets(null));

            Assert.That(exception.ParamName, Is.EqualTo("targets"));
            Assert.That(blackboard.ObjectivesVersion, Is.EqualTo(1));
            Assert.That(blackboard.ObjectiveTargets, Is.SameAs(view));
            Assert.That(blackboard.ObjectiveTargets, Has.Count.EqualTo(2));
            AssertTarget(blackboard.ObjectiveTargets[0],
                Target(1, 1, 1, ObjectiveTargetKind.Task));
            AssertTarget(blackboard.ObjectiveTargets[1],
                Target(2, 2, 2, ObjectiveTargetKind.Switch));
        }

        [Test]
        public void DuplicateIdsAreAcceptedAndPreserved()
        {
            var blackboard = new WorldBlackboard();
            ObjectiveTarget first = Target(5, 1, 1, ObjectiveTargetKind.Task);
            ObjectiveTarget second = Target(5, 2, 2, ObjectiveTargetKind.Console);

            Assert.DoesNotThrow(() =>
                blackboard.SetObjectiveTargets(new[] { first, second }));

            Assert.That(blackboard.ObjectiveTargets, Has.Count.EqualTo(2));
            AssertTarget(blackboard.ObjectiveTargets[0], first);
            AssertTarget(blackboard.ObjectiveTargets[1], second);
        }

        static WorldBlackboard BlackboardWithTwoTargets()
        {
            var blackboard = new WorldBlackboard();
            blackboard.SetObjectiveTargets(new[]
            {
                Target(1, 1, 1, ObjectiveTargetKind.Task),
                Target(2, 2, 2, ObjectiveTargetKind.Switch)
            });
            return blackboard;
        }

        static void AssertSingleChangeIncrements(ObjectiveTarget first, ObjectiveTarget second)
        {
            var blackboard = BlackboardWithTwoTargets();

            blackboard.SetObjectiveTargets(new[] { first, second });

            Assert.That(blackboard.ObjectivesVersion, Is.EqualTo(2));
        }

        static ObjectiveTarget Target(int id, int x, int y, ObjectiveTargetKind kind) =>
            new ObjectiveTarget(id, new Vector2Int(x, y), kind);

        static void AssertTarget(ObjectiveTarget actual, ObjectiveTarget expected)
        {
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.Cell, Is.EqualTo(expected.Cell));
            Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
        }
    }
}
