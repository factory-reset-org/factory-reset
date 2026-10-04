using System.Collections.Generic;
using NUnit.Framework;
using ToyFactory.AI.Core.FSM;

namespace ToyFactory.Tests.EditMode
{
    public class CompositeStateTests
    {
        sealed class Context
        {
            public readonly List<string> Log = new List<string>();
            public bool ChildFlag;
            public bool LeaveParent;
            public bool ReturnToParent;
        }

        sealed class Recorder : IState<Context>
        {
            readonly string _name;
            public Recorder(string name) { _name = name; }
            public void Enter(Context ctx) => ctx.Log.Add(_name + ".Enter");
            public void Tick(Context ctx) => ctx.Log.Add(_name + ".Tick");
            public void Exit(Context ctx) => ctx.Log.Add(_name + ".Exit");
        }

        Context _ctx;
        Recorder _a, _b, _other;
        CompositeState<Context> _parent;
        StateMachine<Context> _machine;

        [SetUp]
        public void SetUp()
        {
            _ctx = new Context();
            _a = new Recorder("A");
            _b = new Recorder("B");
            _other = new Recorder("Other");
            _parent = new CompositeState<Context>("Parent", _a, new List<Transition<Context>>
            {
                new Transition<Context>(_a, _b, c => c.ChildFlag, 0)
            });
            _machine = new StateMachine<Context>(_parent, new List<Transition<Context>>
            {
                new Transition<Context>(_parent, _other, c => c.LeaveParent, 0),
                new Transition<Context>(_other, _parent, c => c.ReturnToParent, 0)
            });
        }

        [Test]
        public void TickingTheParentEntersAndTicksTheInitialChild()
        {
            _machine.Tick(_ctx);

            CollectionAssert.AreEqual(new[] { "A.Enter", "A.Tick" }, _ctx.Log);
            Assert.AreSame(_a, _parent.CurrentChild);
        }

        [Test]
        public void ChildTransitionsRunInsideTheParent()
        {
            _machine.Tick(_ctx);
            _ctx.ChildFlag = true;
            _ctx.Log.Clear();

            _machine.Tick(_ctx);

            CollectionAssert.AreEqual(new[] { "A.Exit", "B.Enter", "B.Tick" }, _ctx.Log);
            Assert.AreSame(_parent, _machine.Current);
            Assert.AreSame(_b, _parent.CurrentChild);
        }

        [Test]
        public void LeavingTheParentExitsTheRunningChildFirst()
        {
            _machine.Tick(_ctx);
            _ctx.ChildFlag = true;
            _machine.Tick(_ctx);
            _ctx.LeaveParent = true;
            _ctx.Log.Clear();

            _machine.Tick(_ctx);

            CollectionAssert.AreEqual(new[] { "B.Exit", "Other.Enter", "Other.Tick" }, _ctx.Log);
            Assert.IsNull(_parent.CurrentChild);
        }

        [Test]
        public void ReEnteringTheParentRestartsFromTheInitialChild()
        {
            _machine.Tick(_ctx);
            _ctx.ChildFlag = true;
            _machine.Tick(_ctx);
            _ctx.ChildFlag = false;
            _ctx.LeaveParent = true;
            _machine.Tick(_ctx);
            _ctx.LeaveParent = false;
            _ctx.ReturnToParent = true;
            _ctx.Log.Clear();

            _machine.Tick(_ctx);

            CollectionAssert.AreEqual(new[] { "Other.Exit", "A.Enter", "A.Tick" }, _ctx.Log);
            Assert.AreSame(_a, _parent.CurrentChild);
        }

        [Test]
        public void ExitingBeforeTheChildEverRanDoesNotExitIt()
        {
            _parent.Enter(_ctx);
            _parent.Exit(_ctx);

            CollectionAssert.IsEmpty(_ctx.Log);
        }

        [Test]
        public void ToStringIsTheName()
        {
            Assert.AreEqual("Parent", _parent.ToString());
            Assert.AreEqual("Parent", _parent.Name);
        }
    }
}
