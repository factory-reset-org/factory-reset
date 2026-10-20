using System;

namespace ToyFactory.AI.Core.FSM
{
    /// <summary>Immutable transition data evaluated by a shared state machine.</summary>
    public sealed class Transition<TContext>
    {
        /// <summary>Required current state, or null when the transition applies from any state.</summary>
        public IState<TContext> From { get; }

        /// <summary>State entered when <see cref="Condition"/> succeeds.</summary>
        public IState<TContext> To { get; }

        /// <summary>Predicate evaluated with the current context.</summary>
        public Func<TContext, bool> Condition { get; }

        /// <summary>Higher values are considered before lower values.</summary>
        public int Priority { get; }

        public Transition(IState<TContext> from, IState<TContext> to,
            Func<TContext, bool> condition, int priority)
        {
            From = from;
            To = to ?? throw new ArgumentNullException(nameof(to));
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Priority = priority;
        }
    }
}
