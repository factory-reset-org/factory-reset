using UnityEngine;
using UnityEngine.InputSystem;
using ToyFactory.Interfaces;

namespace ToyFactory.Managers
{
    /// <summary>
    /// Escape pauses the game while it is being played and resumes it again. While paused,
    /// time stands still, the cursor is free and a small panel offers Resume.
    /// </summary>
    /// <remarks>
    /// Escape also skips a cutscene. A press that did that must not pause as well, so the
    /// game only pauses if it was already being played in the frame before the press.
    /// </remarks>
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] Vector2 panelSize = new Vector2(260f, 130f);

        bool _wasPlaying;
        bool _stoppedTime;

        void Update()
        {
            GameManager game = GameManager.Instance;
            if (game == null)
                return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (game.State == GameState.Paused)
                    Resume();
                else if (game.State == GameState.Playing && _wasPlaying)
                    Pause();
            }

            _wasPlaying = game.State == GameState.Playing;
        }

        public void Pause()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.State != GameState.Playing)
                return;

            game.SetState(GameState.Paused);
            Time.timeScale = 0f;
            _stoppedTime = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Resume()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.State != GameState.Paused)
                return;

            RestoreTime();
            game.Resume();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // Time must never be left stopped if the menu goes away while the game is paused.
        void OnDisable() => RestoreTime();

        void RestoreTime()
        {
            if (!_stoppedTime)
                return;
            _stoppedTime = false;
            Time.timeScale = 1f;
        }

        // A plain placeholder panel until the real pause screen exists.
        void OnGUI()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.State != GameState.Paused)
                return;

            var panel = new Rect((Screen.width - panelSize.x) * 0.5f, (Screen.height - panelSize.y) * 0.5f,
                panelSize.x, panelSize.y);
            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.Label("Paused");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Resume (Esc)", GUILayout.Height(36f)))
                Resume();
            GUILayout.EndArea();
        }
    }
}
