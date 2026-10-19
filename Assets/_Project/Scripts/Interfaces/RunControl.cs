using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// What the menus may do to a run: resume, go back to the last checkpoint, or start the
    /// game again. The pause menu implements it; the UI, which cannot see that assembly,
    /// reaches it through <see cref="RunControl"/>.
    /// </summary>
    /// <remarks>
    /// A checkpoint is the start of each chapter: after Room 1, Room 2 and Room 3 are done (and
    /// the start of the game before that). A game has a fixed number of restarts. Going back
    /// to the checkpoint uses one, whether the player was recalled or chose it from the pause
    /// menu. Restarting the game starts a new game with all of them again.
    /// </remarks>
    public interface IRunControl
    {
        /// <summary>Restarts a game starts with.</summary>
        int RestartsPerGame { get; }

        /// <summary>Restarts from the checkpoint still left in this game.</summary>
        int RestartsLeft { get; }

        /// <summary>The room the player goes back to: the one being played.</summary>
        string CheckpointName { get; }

        /// <summary>True while the player has been recalled and the game waits for their choice.</summary>
        bool IsRecalled { get; }

        /// <summary>Leaves the pause menu.</summary>
        void Resume();

        /// <summary>Goes back to the checkpoint, using one restart. False, and nothing happens, with none left.</summary>
        bool RestartFromCheckpoint();

        /// <summary>Starts the game again from the title, with every restart back.</summary>
        void RestartGame();

        /// <summary>Ends a recalled run: the game goes to the Results screen.</summary>
        void GiveUp();

        /// <summary>
        /// Called when the player's integrity reaches 0. True if a restart is left, so the
        /// game waits on the recall screen; false if the run should end now.
        /// </summary>
        bool OfferRestart();
    }

    /// <summary>Publishes the active <see cref="IRunControl"/>, like <see cref="GameClock"/>.</summary>
    public static class RunControl
    {
        /// <summary>The run control, or null when none is loaded (a test scene).</summary>
        public static IRunControl Current
        {
            get
            {
                // A destroyed component still compares equal to null through UnityEngine.Object.
                if (s_current is Object unityObject && unityObject == null)
                    s_current = null;
                return s_current;
            }
        }

        static IRunControl s_current;

        public static void Publish(IRunControl control) => s_current = control;

        // Domain reload is off in this project: clear a stale one at the start of each session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOnLoad() => s_current = null;
    }
}
