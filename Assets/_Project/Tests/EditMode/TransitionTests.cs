using System;
using System.Reflection;
using NUnit.Framework;
using ToyFactory.AI.Core.FSM;

namespace ToyFactory.Tests
{
    public sealed class TransitionTests
    {
        [Test]
        public void ConstructorStoresTransitionData()
        {
            var from = new TestState();
            var to = new TestState();
            Func<TestContext, bool> condition = ctx => ctx.Enabled;

            var transition = new Transition<TestContext>(from, to, condition, 42);

            Assert.That(transition.From, Is.SameAs(from));
            Assert.That(transition.To, Is.SameAs(to));
            Assert.That(transition.Condition, Is.SameAs(condition));
            Assert.That(transition.Priority, Is.EqualTo(42));
        }

        [Test]
        public void NullFromRepresentsAnyStateAndConditionReceivesContext()
        {
            var transition = new Transition<TestContext>(null, new TestState(),
                ctx => ctx.Enabled && ctx.Value == 7, 0);

            Assert.That(transition.From, Is.Null);
            Assert.That(transition.Condition(new TestContext(true, 7)), Is.True);
            Assert.That(transition.Condition(new TestContext(false, 7)), Is.False);
        }

        [Test]
        public void NullTargetIsRejected()
        {
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new Transition<TestContext>(new TestState(), null, _ => true, 0));
            Assert.That(exception.ParamName, Is.EqualTo("to"));
        }

        [Test]
        public void NullConditionIsRejected()
        {
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new Transition<TestContext>(new TestState(), new TestState(), null, 0));
            Assert.That(exception.ParamName, Is.EqualTo("condition"));
        }

        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(int.MaxValue)]
        public void PriorityPreservesAnyInteger(int priority)
        {
            var transition = new Transition<TestContext>(null, new TestState(), _ => true, priority);
            Assert.That(transition.Priority, Is.EqualTo(priority));
        }

        [TestCase("From")]
        [TestCase("To")]
        [TestCase("Condition")]
        [TestCase("Priority")]
        public void PublicPropertiesAreReadOnly(string propertyName)
        {
            PropertyInfo property = typeof(Transition<TestContext>).GetProperty(propertyName);
            Assert.That(property, Is.Not.Null);
            Assert.That(property.CanWrite, Is.False);
        }

        [Test]
        public void TransitionTypeIsSealed()
        {
            Assert.That(typeof(Transition<TestContext>).IsSealed, Is.True);
        }

        readonly struct TestContext
        {
            public bool Enabled { get; }
            public int Value { get; }

            public TestContext(bool enabled, int value)
            {
                Enabled = enabled;
                Value = value;
            }
        }

        sealed class TestState : IState<TestContext>
        {
            public void Enter(TestContext ctx) { }
            public void Tick(TestContext ctx) { }
            public void Exit(TestContext ctx) { }
        }
    }
}
