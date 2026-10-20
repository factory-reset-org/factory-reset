namespace ToyFactory.Interfaces
{
    /// <summary>
    /// The subset of GameManager other assemblies need: the current state, the game
    /// clock, and listener registration. Exists so Runtime-assembly code can react to
    /// state changes without referencing GameManager directly, it lives in the default
    /// assembly, which a custom asmdef can never reference.
    /// </summary>
    public interface IGameClock
    {
        /// <summary>The current game state.</summary>
        GameState State { get; }

        /// <summary>Seconds elapsed while Playing. Frozen during Cutscene and Paused.</summary>
        float GameTime { get; }

        /// <summary>Adds a listener notified on every future state change.</summary>
        void AddListener(IGameStateListener listener);

        /// <summary>Removes a previously added listener.</summary>
        void RemoveListener(IGameStateListener listener);

        /// <summary>
        /// Asks the game to switch state (for example Playing to Cutscene and back).
        /// Listeners are notified as with any change; no effect if already in that state.
        /// </summary>
        void RequestState(GameState state);
    }
}
