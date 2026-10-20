namespace ToyFactory.AI.Core.FSM
{
    /// <summary>One state in a data-driven finite-state machine.</summary>
    public interface IState<TContext>
    {
        /// <summary>Called once when this state becomes current.</summary>
        void Enter(TContext ctx);

        /// <summary>Runs this state's behaviour for the current decision tick.</summary>
        void Tick(TContext ctx);

        /// <summary>Called once before this state stops being current.</summary>
        void Exit(TContext ctx);
    }
}
