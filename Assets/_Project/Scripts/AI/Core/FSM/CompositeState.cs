using System;
using System.Collections.Generic;

namespace ToyFactory.AI.Core.FSM
{
    /// <summary>
    /// A state that contains its own state machine: the building block of a hierarchical
    /// FSM. While this state is current, each <see cref="Tick"/> ticks the child machine,
    /// so child transitions only compete with each other, and the parent machine's
    /// transitions (for example an interrupt from any state) still win over every child.
    /// </summary>
    /// <remarks>
    /// The child machine is rebuilt from the same initial state and transition list on
    /// every <see cref="Enter"/>, so re-entering the parent always starts from its initial
    /// child instead of wherever it was left. That costs one small allocation per entry,
    /// never per tick.
    /// </remarks>
    public sealed class CompositeState<TContext> : IState<TContext>
    {
        readonly IState<TContext> _initial;
        readonly IReadOnlyList<Transition<TContext>> _transitions;
        StateMachine<TContext> _child;
        bool _childTicked;

        /// <summary>Name shown by debug tools, e.g. "Calm".</summary>
        public string Name { get; }

        /// <summary>The child state that will run on the next tick, or null before the first <see cref="Enter"/>.</summary>
        public IState<TContext> CurrentChild => _child?.Current;

        /// <summary>The child transitions, in priority order once entered.</summary>
        public IReadOnlyList<Transition<TContext>> ChildTransitions => _child != null ? _child.Transitions : _transitions;

        public CompositeState(string name, IState<TContext> initialChild,
            IReadOnlyList<Transition<TContext>> childTransitions)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _initial = initialChild ?? throw new ArgumentNullException(nameof(initialChild));
            _transitions = childTransitions ?? throw new ArgumentNullException(nameof(childTransitions));
        }

        public void Enter(TContext ctx)
        {
            // The child's own Enter runs on its first Tick (StateMachine enters lazily).
            _child = new StateMachine<TContext>(_initial, _transitions);
            _childTicked = false;
        }

        public void Tick(TContext ctx)
        {
            _child.Tick(ctx);
            _childTicked = true;
        }

        public void Exit(TContext ctx)
        {
            // Give the running child its Exit so it can clean up. A child that never ticked
            // was never entered (StateMachine enters lazily), so it must not be exited either.
            if (_child != null && _childTicked)
                _child.Current.Exit(ctx);
            _child = null;
            _childTicked = false;
        }

        public override string ToString() => Name;
    }
}
