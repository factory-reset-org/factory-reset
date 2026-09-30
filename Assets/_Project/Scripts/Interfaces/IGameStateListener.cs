namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Implemented by anything that must react when the game state changes, for
    /// example stopping agent brain ticks during a cutscene or pausing task timers.
    /// </summary>
    public interface IGameStateListener
    {
        /// <summary>Called once, right after GameManager switches state.</summary>
        void OnGameStateChanged(GameState previous, GameState current);
    }
}
