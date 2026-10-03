using System;
using System.Collections.Generic;

namespace ToyFactory.AI.Core.FSM
{
    /// <summary>Runs an ordered, data-driven finite-state machine.</summary>
    public sealed class StateMachine<TContext>
    {
        readonly Transition<TContext>[] _transitions;
        readonly IReadOnlyList<Transition<TContext>> _transitionView;
        bool _hasEnteredCurrent;

        /// <summary>The state that will receive the next state tick.</summary>
        public IState<TContext> Current { get; private set; }

        /// <summary>Immutable transition snapshot in evaluation order.</summary>
        public IReadOnlyList<Transition<TContext>> Transitions => _transitionView;

        public StateMachine(IState<TContext> initialState,
            IReadOnlyList<Transition<TContext>> transitions)
        {
            Current = initialState ?? throw new ArgumentNullException(nameof(initialState));
            if (transitions == null)
            {
                throw new ArgumentNullException(nameof(transitions));
            }

            _transitions = new Transition<TContext>[transitions.Count];
            for (int i = 0; i < transitions.Count; i++)
            {
                Transition<TContext> transition = transitions[i];
                if (transition == null)
                {
                    throw new ArgumentException(
                        $"Transition at index {i} cannot be null.", nameof(transitions));
                }

                _transitions[i] = transition;
            }

            StableSortByDescendingPriority(_transitions);
            _transitionView = Array.AsReadOnly(_transitions);
        }

        /// <summary>Evaluates at most one transition, then ticks the current state.</summary>
        public void Tick(TContext ctx)
        {
            if (!_hasEnteredCurrent)
            {
                Current.Enter(ctx);
                _hasEnteredCurrent = true;
            }

            for (int i = 0; i < _transitions.Length; i++)
            {
                Transition<TContext> transition = _transitions[i];
                if (transition.From != null && !ReferenceEquals(transition.From, Current))
                {
                    continue;
                }

                if (!transition.Condition(ctx))
                {
                    continue;
                }

                Current.Exit(ctx);
                Current = transition.To;
                Current.Enter(ctx);
                break;
            }

            Current.Tick(ctx);
        }

        static void StableSortByDescendingPriority(Transition<TContext>[] transitions)
        {
            for (int i = 1; i < transitions.Length; i++)
            {
                Transition<TContext> value = transitions[i];
                int destination = i;

                while (destination > 0 &&
                       transitions[destination - 1].Priority < value.Priority)
                {
                    transitions[destination] = transitions[destination - 1];
                    destination--;
                }

                transitions[destination] = value;
            }
        }
    }
}
