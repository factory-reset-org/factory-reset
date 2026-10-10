using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ToyFactory.UI
{
    /// <summary>
    /// The title screen: the game's name, the four chapters, the controls and a Start button.
    /// It only draws and reports the request to start; when it shows and what Start does is the
    /// screens presenter's job.
    /// </summary>
    public sealed class TitleView : MonoBehaviour
    {
        /// <summary>The controls shown, as key and action. The player scripts read these inputs.</summary>
        public static readonly string[][] Controls =
        {
            new[] { "W A S D", "Move" },
            new[] { "Mouse", "Look" },
            new[] { "Left click", "Fire the blaster" },
            new[] { "R", "Reload" },
            new[] { "Q / Right click", "Throw a wind-up toy" },
            new[] { "E", "Interact" },
            new[] { "Shift", "Sprint" },
            new[] { "Esc", "Pause" },
            new[] { "M", "Mute" }
        };

        Canvas _canvas;

        /// <summary>True while the screen is up.</summary>
        public bool IsShowing => _canvas != null && _canvas.gameObject.activeSelf;

        /// <summary>Raised when the player asks to start: the Start button, Enter or Space.</summary>
        public event Action StartRequested;

        /// <summary>Builds the screen, hidden, under this view's object.</summary>
        internal void Build(int sortingOrder)
        {
            _canvas = HudWidgets.ScreenCanvas("Title Canvas", transform, sortingOrder);
            HudWidgets.AllowClicks(_canvas);
            Transform root = _canvas.transform;

            Image backdrop = HudWidgets.Panel("Backdrop", root, new Color(HudWidgets.Plum.r, HudWidgets.Plum.g, HudWidgets.Plum.b, 0.94f));
            backdrop.raycastTarget = true;

            Text title = HudWidgets.Label("Title", root, "FACTORY RESET", 128, HudWidgets.Sun, TextAnchor.MiddleCenter);
            HudWidgets.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1500f, 160f));

            Text route = HudWidgets.Label("Route", root, "Assembly Floor   >   Painting Room   >   Storage Area   >   Control Room",
                32, HudWidgets.InkDim, TextAnchor.MiddleCenter, FontStyle.Normal);
            HudWidgets.Place(route.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -285f), new Vector2(1500f, 44f));

            BuildControls(root);

            Button start = HudWidgets.MakeButton("Start", root, "START", HudWidgets.Mint, HudWidgets.Plum, 56);
            HudWidgets.Place((RectTransform)start.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(440f, 100f));
            start.onClick.AddListener(RequestStart);

            Text hint = HudWidgets.Label("Hint", root, "or press Enter", 24, HudWidgets.InkDim, TextAnchor.MiddleCenter, FontStyle.Normal);
            HudWidgets.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(440f, 34f));

            _canvas.gameObject.SetActive(false);
        }

        /// <summary>Shows the screen.</summary>
        public void Show()
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(true);
        }

        /// <summary>Hides the screen.</summary>
        public void Hide()
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(false);
        }

        /// <summary>Asks to start, as the button does.</summary>
        public void RequestStart() => StartRequested?.Invoke();

        void Update()
        {
            if (!IsShowing)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
                                     || keyboard.spaceKey.wasPressedThisFrame))
                RequestStart();
        }

        // Two columns of keys and what they do, on one panel.
        static void BuildControls(Transform root)
        {
            Image panel = HudWidgets.Panel("Controls", root, HudWidgets.PlumPanel);
            HudWidgets.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1000f, 470f));

            Text header = HudWidgets.Label("Header", panel.transform, "CONTROLS", 28, HudWidgets.Mint, TextAnchor.MiddleLeft);
            HudWidgets.Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -22f), new Vector2(400f, 36f));

            const int perColumn = 5;
            for (int i = 0; i < Controls.Length; i++)
            {
                int column = i / perColumn;
                int row = i % perColumn;
                float x = 40f + column * 480f;
                float y = -(86f + row * 66f);

                Image chip = HudWidgets.Panel("Key " + (i + 1), panel.transform, HudWidgets.PlumLight);
                HudWidgets.Place(chip.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(210f, 48f));
                Text key = HudWidgets.Label("Text", chip.transform, Controls[i][0], 24, HudWidgets.Ink, TextAnchor.MiddleCenter);
                key.resizeTextForBestFit = true;
                key.resizeTextMinSize = 16;
                key.resizeTextMaxSize = 24;

                Text action = HudWidgets.Label("Action " + (i + 1), panel.transform, Controls[i][1], 26, HudWidgets.Ink, TextAnchor.MiddleLeft, FontStyle.Normal);
                HudWidgets.Place(action.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x + 230f, y), new Vector2(230f, 48f));
            }
        }
    }
}
