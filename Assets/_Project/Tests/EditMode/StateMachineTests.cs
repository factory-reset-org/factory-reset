using System;
using System.Collections.Generic;
using NUnit.Framework;
using ToyFactory.AI.Core.FSM;

namespace ToyFactory.Tests
{
    public sealed class StateMachineTests
    {
        [Test]
        public void ConstructorRejectsNullInitialState()
        {
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new StateMachine<TestContext>(null, Array.Empty<Transition<TestContext>>()));

            Assert.That(exception.ParamName, Is.EqualTo("initialState"));
        }

        [Test]
        public void ConstructorRejectsNullTransitionList()
        {
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new StateMachine<TestContext>(new RecordingState("A"), null));

            Assert.That(exception.ParamName, Is.EqualTo("transitions"));
        }

        [Test]
        public void ConstructorRejectsNullTransitionEntry()
        {
            var transitions = new Transition<TestContext>[] { null };

            var exception = Assert.Throws<ArgumentException>(() =>
                new StateMachine<TestContext>(new RecordingState("A"), transitions));

            Assert.That(exception.ParamName, Is.EqualTo("transitions"));
        }

        [Test]
        public void ConstructorSnapshotsAndSortsTransitionsWithoutChangingInput()
        {
            var state = new RecordingState("A");
            var low = Transition(state, state, true, 1);
            var firstHigh = Transition(state, state, true, 5);
            var secondHigh = Transition(state, state, true, 5);
            var input = new List<Transition<TestContext>> { low, firstHigh, secondHigh };

            var machine = new StateMachine<TestContext>(state, input);
            input.Clear();

            Assert.That(machine.Transitions,
                Is.EqualTo(new[] { firstHigh, secondHigh, low }));
        }

        [Test]
        public void TransitionViewCannotBeModified()
        {
            var state = new RecordingState("A");
            var machine = new StateMachine<TestContext>(state,
                new[] { Transition(state, state, true, 0) });
            var list = (IList<Transition<TestContext>>)machine.Transitions;

            Assert.That(list.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() =>
                list.Add(Transition(state, state, true, 0)));
        }

        [Test]
        public void InitialStateEntersLazilyAndOnlyOnce()
        {
            var state = new RecordingState("A");
            var machine = EmptyMachine(state);
            var context = new TestContext();

            Assert.That(state.EnterCount, Is.Zero);

            machine.Tick(context);
            machine.Tick(context);

            Assert.That(state.EnterCount, Is.EqualTo(1));
            Assert.That(state.TickCount, Is.EqualTo(2));
            Assert.That(machine.Current, Is.SameAs(state));
        }

        [Test]
        public void HighestPriorityEligibleTransitionWins()
        {
            var from = new RecordingState("A");
            var lowTarget = new RecordingState("Low");
            var highTarget = new RecordingState("High");
            var machine = new StateMachine<TestContext>(from, new[]
            {
                Transition(from, lowTarget, true, 1),
                Transition(from, highTarget, true, 10)
            });

            machine.Tick(new TestContext());

            Assert.That(machine.Current, Is.SameAs(highTarget));
            Assert.That(lowTarget.EnterCount, Is.Zero);
        }

        [Test]
        public void EqualPrioritiesPreserveDeclarationOrder()
        {
            var from = new RecordingState("A");
            var first = new RecordingState("First");
            var second = new RecordingState("Second");
            var machine = new StateMachine<TestContext>(from, new[]
            {
                Transition(from, first, true, 5),
                Transition(from, second, true, 5)
            });

            machine.Tick(new TestContext());

            Assert.That(machine.Current, Is.SameAs(first));
        }

        [Test]
        public void AnyStateTransitionCanChangeCurrentState()
        {
            var initial = new RecordingState("A");
            var target = new RecordingState("Target");
            var machine = new StateMachine<TestContext>(initial, new[]
            {
                Transition(null, target, true, 0)
            });

            machine.Tick(new TestContext());

            Assert.That(machine.Current, Is.SameAs(target));
        }

        [Test]
        public void TransitionForAnotherStateDoesNotEvaluateItsCondition()
        {
            var initial = new RecordingState("A");
            var other = new RecordingState("Other");
            var target = new RecordingState("Target");
            int evaluations = 0;
            var machine = new StateMachine<TestContext>(initial, new[]
            {
                new Transition<TestContext>(other, target, _ =>
                {
                    evaluations++;
                    return true;
                }, 0)
            });

            machine.Tick(new TestContext());

            Assert.That(evaluations, Is.Zero);
            Assert.That(machine.Current, Is.SameAs(initial));
        }

        [Test]
        public void NoEligibleTransitionKeepsAndTicksCurrentState()
        {
            var initial = new RecordingState("A");
            var target = new RecordingState("Target");
            var machine = new StateMachine<TestContext>(initial, new[]
            {
                Transition(initial, target, false, 0)
            });

            machine.Tick(new TestContext());

            Assert.That(machine.Current, Is.SameAs(initial));
            Assert.That(initial.ExitCount, Is.Zero);
            Assert.That(initial.TickCount, Is.EqualTo(1));
            Assert.That(target.EnterCount, Is.Zero);
        }

        [Test]
        public void TickPerformsAtMostOneTransition()
        {
            var first = new RecordingState("A");
            var second = new RecordingState("B");
            var third = new RecordingState("C");
            var machine = new StateMachine<TestContext>(first, new[]
            {
                Transition(first, second, true, 0),
                Transition(second, third, true, 0)
            });

            machine.Tick(new TestContext());

            Assert.That(machine.Current, Is.SameAs(second));
            Assert.That(second.TickCount, Is.EqualTo(1));
            Assert.That(third.EnterCount, Is.Zero);
        }

        [Test]
        public void TransitionLifecycleIsExitUpdateEnterThenTick()
        {
            var context = new TestContext();
            var from = new RecordingState("A");
            var to = new RecordingState("B");
            StateMachine<TestContext> machine = null;
            from.OnExit = _ => context.Events.Add("exit");
            to.OnEnter = _ =>
            {
                Assert.That(machine.Current, Is.SameAs(to));
                context.Events.Add("enter");
            };
            to.OnTick = _ => context.Events.Add("tick");
            machine = new StateMachine<TestContext>(from, new[]
            {
                Transition(from, to, true, 0)
            });

            machine.Tick(context);

            Assert.That(context.Events, Is.EqualTo(new[] { "exit", "enter", "tick" }));
            Assert.That(from.EnterCount, Is.EqualTo(1));
            Assert.That(from.ExitCount, Is.EqualTo(1));
            Assert.That(to.EnterCount, Is.EqualTo(1));
            Assert.That(to.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void SelfTransitionUsesNormalExitEnterLifecycle()
        {
            var state = new RecordingState("A");
            var machine = new StateMachine<TestContext>(state, new[]
            {
                Transition(state, state, true, 0)
            });

            machine.Tick(new TestContext());

            Assert.That(machine.Current, Is.SameAs(state));
            Assert.That(state.EnterCount, Is.EqualTo(2));
            Assert.That(state.ExitCount, Is.EqualTo(1));
            Assert.That(state.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void ConditionReceivesTickContext()
        {
            var from = new RecordingState("A");
            var to = new RecordingState("B");
            var expected = new TestContext { Enabled = true };
            TestContext received = null;
            var machine = new StateMachine<TestContext>(from, new[]
            {
                new Transition<TestContext>(from, to, ctx =>
                {
                    received = ctx;
                    return ctx.Enabled;
                }, 0)
            });

            machine.Tick(expected);

            Assert.That(received, Is.SameAs(expected));
            Assert.That(machine.Current, Is.SameAs(to));
        }

        [Test]
        public void ReusedTickAllocatesZeroBytesAfterWarmUp()
        {
            var initial = new AllocationState();
            var unrelated = new AllocationState();
            var target = new AllocationState();
            var machine = new StateMachine<int>(initial, new[]
            {
                new Transition<int>(unrelated, target, _ => true, 0),
                new Transition<int>(initial, target, _ => false, -1)
            });

            for (int i = 0; i < 100; i++)
            {
                machine.Tick(i);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                machine.Tick(i);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        static StateMachine<TestContext> EmptyMachine(RecordingState initial)
        {
            return new StateMachine<TestContext>(initial,
                Array.Empty<Transition<TestContext>>());
        }

        static Transition<TestContext> Transition(RecordingState from,
            RecordingState to, bool result, int priority)
        {
            return new Transition<TestContext>(from, to, _ => result, priority);
        }

        sealed class TestContext
        {
            public bool Enabled;
            public readonly List<string> Events = new List<string>();
        }

        sealed class RecordingState : IState<TestContext>
        {
            public readonly string Name;
            public int EnterCount;
            public int TickCount;
            public int ExitCount;
            public Action<TestContext> OnEnter;
            public Action<TestContext> OnTick;
            public Action<TestContext> OnExit;

            public RecordingState(string name)
            {
                Name = name;
            }

            public void Enter(TestContext ctx)
            {
                EnterCount++;
                OnEnter?.Invoke(ctx);
            }

            public void Tick(TestContext ctx)
            {
                TickCount++;
                OnTick?.Invoke(ctx);
            }

            public void Exit(TestContext ctx)
            {
                ExitCount++;
                OnExit?.Invoke(ctx);
            }
        }

        sealed class AllocationState : IState<int>
        {
            public void Enter(int ctx) { }
            public void Tick(int ctx) { }
            public void Exit(int ctx) { }
        }
    }
}
