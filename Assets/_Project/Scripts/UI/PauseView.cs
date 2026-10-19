using System;
using UnityEngine;
using UnityEngine.UI;
using ToyFactory.Interfaces;

namespace ToyFactory.UI
{
    /// <summary>
    /// The pause screen, and the recall screen when the player's integrity runs out with a
    /// restart left. Paused: Resume, Restart from checkpoint, Restart game, and the Sound and
    /// Music sliders. Recalled: Restart from checkpoint, Restart game and Give up. It only draws
    /// and reports the choice; the screens presenter passes it to <see cref="IRunControl"/>.
    /// </summary>
    /// <remarks>
    /// The sliders write <see cref="GameAudio"/> directly, so a change is heard at once and kept
    /// for the next run. Restart from checkpoint is greyed out with no restart left.
    /// </remarks>
    public sealed class PauseView : MonoBehaviour
    {
        const float ButtonWidth = 620f;
        const float ButtonHeight = 72f;

        Canvas _canvas;
        RectTransform _plate;
        Text _heading;
        Text _info;
        Button _resume;
        Button _checkpoint;
        Text _checkpointLabel;
        Button _restart;
        Button _giveUp;
        GameObject _sliders;
        Slider _sound;
        Slider _music;
        Text _soundValue;
        Text _musicValue;
        bool _recalled;
        string _shownInfo;

        /// <summary>True while the screen is up.</summary>
        public bool IsShowing => _canvas != null && _canvas.gameObject.activeSelf;

        /// <summary>True when it shows the recall screen rather than the pause screen.</summary>
        public bool IsRecallScreen => IsShowing && _recalled;

        /// <summary>The Restart from checkpoint button, for tests.</summary>
        public Button CheckpointButton => _checkpoint;

        /// <summary>The sound slider, for tests.</summary>
        public Slider SoundSlider => _sound;

        /// <summary>The music slider, for tests.</summary>
        public Slider MusicSlider => _music;

        public event Action ResumeRequested;
        public event Action CheckpointRequested;
        public event Action RestartRequested;
        public event Action GiveUpRequested;

        /// <summary>Builds the screen, hidden, under this view's object.</summary>
        internal void Build(int sortingOrder)
        {
            _canvas = HudWidgets.ScreenCanvas("Pause Canvas", transform, sortingOrder);
            HudWidgets.AllowClicks(_canvas);
            Transform root = _canvas.transform;

            Image backdrop = HudWidgets.Panel("Backdrop", root, new Color(HudWidgets.Plum.r, HudWidgets.Plum.g, HudWidgets.Plum.b, 0.72f));
            backdrop.raycastTarget = true;

            Image plate = HudWidgets.Plate("Panel", root);
            _plate = plate.rectTransform;
            HudWidgets.Place(_plate, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 700f));
            Transform panel = plate.transform;

            _heading = HudWidgets.Label("Heading", panel, "PAUSED", 72, HudWidgets.Sun, TextAnchor.MiddleCenter);
            HudWidgets.Place(_heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(700f, 90f));

            _info = HudWidgets.Label("Info", panel, string.Empty, 26, HudWidgets.InkDim, TextAnchor.MiddleCenter, FontStyle.Normal);
            HudWidgets.Place(_info.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -136f), new Vector2(700f, 70f));

            _resume = Button("Resume", panel, "RESUME", HudWidgets.Mint, 0, () => ResumeRequested?.Invoke());
            _checkpoint = Button("Checkpoint", panel, "RESTART FROM CHECKPOINT", HudWidgets.Sun, 1, () => CheckpointRequested?.Invoke());
            _checkpointLabel = _checkpoint.GetComponentInChildren<Text>();
            _restart = Button("Restart", panel, "RESTART GAME", HudWidgets.Cobalt, 2, () => RestartRequested?.Invoke());
            _giveUp = Button("Give Up", panel, "GIVE UP", HudWidgets.Tomato, 3, () => GiveUpRequested?.Invoke());

            _sliders = HudWidgets.Rect("Sliders", panel).gameObject;
            _sound = VolumeRow("Sound", _sliders.transform, "SOUND", -540f, out _soundValue, v => GameAudio.SoundVolume = v);
            _music = VolumeRow("Music", _sliders.transform, "MUSIC", -616f, out _musicValue, v => GameAudio.MusicVolume = v);

            _canvas.gameObject.SetActive(false);
        }

        /// <summary>Shows the pause screen, or the recall screen when <paramref name="recalled"/>.</summary>
        public void Show(bool recalled, IRunControl run)
        {
            if (_canvas == null)
                return;
            _recalled = recalled;
            _heading.text = recalled ? "RECALLED" : "PAUSED";
            _heading.color = recalled ? HudWidgets.Tomato : HudWidgets.Sun;

            // Paused: Resume, Checkpoint, Restart, then the sliders. Recalled: Checkpoint, Restart, Give up.
            _resume.gameObject.SetActive(!recalled);
            _giveUp.gameObject.SetActive(recalled);
            _sliders.SetActive(!recalled);
            _plate.sizeDelta = new Vector2(760f, recalled ? 540f : 700f);
            Slot(_checkpoint, recalled ? 0 : 1);
            Slot(_restart, recalled ? 1 : 2);
            Slot(_giveUp, 2);

            _sound.SetValueWithoutNotify(GameAudio.SoundVolume);
            _music.SetValueWithoutNotify(GameAudio.MusicVolume);
            ShowPercent(_soundValue, _sound.value);
            ShowPercent(_musicValue, _music.value);
            _shownInfo = null;
            Refresh(run);
            _canvas.gameObject.SetActive(true);
        }

        /// <summary>Updates the restarts left and the checkpoint's room while the screen is up.</summary>
        public void Refresh(IRunControl run)
        {
            int left = run != null ? run.RestartsLeft : 0;
            int total = run != null ? run.RestartsPerGame : 0;
            string room = run != null ? run.CheckpointName : "the start";
            string info = (_recalled ? "Unit 047's integrity ran out.\n" : "Checkpoint: " + room + "\n")
                          + (left == 1 ? "1 restart" : left + " restarts") + " left of " + total;
            if (info == _shownInfo)
                return;
            _shownInfo = info;
            _info.text = info;
            _checkpoint.interactable = left > 0;
            _checkpointLabel.text = left > 0 ? "RESTART FROM CHECKPOINT (" + left + ")" : "NO RESTARTS LEFT";
        }

        /// <summary>Hides the screen.</summary>
        public void Hide()
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(false);
        }

        Button Button(string name, Transform panel, string text, Color fill, int slot, Action onClick)
        {
            Button button = HudWidgets.MakeButton(name, panel, text, fill, HudWidgets.Plum, 32);
            Slot(button, slot);
            button.onClick.AddListener(() => onClick());
            return button;
        }

        static void Slot(Button button, int slot)
        {
            HudWidgets.Place((RectTransform)button.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -226f - slot * (ButtonHeight + 18f)), new Vector2(ButtonWidth, ButtonHeight));
        }

        // A name, a slider and its value in percent, on one row.
        static Slider VolumeRow(string name, Transform parent, string text, float y, out Text value, Action<float> changed)
        {
            Text label = HudWidgets.Label(name + " Label", parent, text, 28, HudWidgets.Ink, TextAnchor.MiddleLeft);
            HudWidgets.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-235f, y), new Vector2(150f, 48f));

            Image track = HudWidgets.Panel(name + " Slider", parent, HudWidgets.PlumLight);
            track.raycastTarget = true;
            HudWidgets.Place(track.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(30f, y), new Vector2(360f, 22f));

            RectTransform fillArea = HudWidgets.Rect("Fill Area", track.transform);
            Image fill = HudWidgets.Panel("Fill", fillArea, HudWidgets.Mint);
            RectTransform handleArea = HudWidgets.Rect("Handle Area", track.transform);
            handleArea.offsetMin = new Vector2(12f, 0f);
            handleArea.offsetMax = new Vector2(-12f, 0f);
            Image handle = HudWidgets.Panel("Handle", handleArea, HudWidgets.Ink);
            handle.raycastTarget = true;

            Slider slider = track.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            // The slider moves the handle's anchors to the value; give it a size of its own.
            handle.rectTransform.sizeDelta = new Vector2(24f, 22f);

            Text shown = HudWidgets.Label(name + " Value", parent, "100%", 26, HudWidgets.InkDim, TextAnchor.MiddleRight, FontStyle.Normal);
            HudWidgets.Place(shown.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(285f, y), new Vector2(110f, 48f));
            value = shown;

            slider.onValueChanged.AddListener(v =>
            {
                changed(v);
                ShowPercent(shown, v);
            });
            return slider;
        }

        static void ShowPercent(Text text, float value) => text.text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
