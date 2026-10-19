using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ToyFactory.UI
{
    /// <summary>How the credits were opened.</summary>
    public enum CreditsMode
    {
        /// <summary>From the title screen's Credits button: stays at the end until the player goes back.</summary>
        Menu,

        /// <summary>After a won run: rolls once, holds on the last line, then hands over to the results.</summary>
        Ending
    }

    /// <summary>
    /// The credits screen: <see cref="CreditsContent"/> rolling upwards behind soft top and bottom fades,
    /// in the same plum, mint and sun as the other screens. It draws and reports when it is closed; which
    /// screen comes next is the presenter's job.
    /// </summary>
    /// <remarks>
    /// <para><b>Smooth scrolling.</b> The text moves at a steady pace in unscaled time (the game may be
    /// paused or at time scale 0 when the ending arrives). The pace eases in over a quarter of a second after
    /// a short pause on the title, and Down or Up speeds it up or reverses it, again with easing. The mouse
    /// wheel adds a push that fades out. The roll stops with the last line in the middle of the screen.</para>
    /// <para><b>Keys.</b> Escape, Backspace or the button closes it. Space also skips the ending's roll. Enter
    /// is deliberately not used: the title starts the game on Enter and the results screen submits its name
    /// field on Enter, and a press that closed the credits must not reach either.</para>
    /// </remarks>
    public sealed class CreditsView : MonoBehaviour
    {
        /// <summary>Pixels per second (on the 1920 x 1080 layout) the text rises at.</summary>
        public const float BaseSpeed = 120f;

        /// <summary>How much faster Down is, and the same speed backwards for Up.</summary>
        public const float FastFactor = 4f;

        /// <summary>Seconds the screen fades in, and the roll waits before it starts moving.</summary>
        public const float FadeSeconds = 0.6f;

        /// <summary>Seconds the roll rests before it starts.</summary>
        public const float StartDelay = 1.2f;

        /// <summary>Seconds the ending holds on the last line before it closes itself.</summary>
        public const float EndHold = 3f;

        const float ColumnWidth = 1300f;
        const float FadeHeight = 300f;
        const float EaseSeconds = 0.25f;
        const float WheelPush = 420f;
        const float WheelFade = 1600f;

        struct Style
        {
            public int Size;
            public Color Colour;
            public FontStyle Font;
            public float Before;

            public Style(int size, Color colour, FontStyle font, float before)
            {
                Size = size;
                Colour = colour;
                Font = font;
                Before = before;
            }
        }

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _content;
        Text _hint;
        Text _buttonLabel;
        Sprite _fade;
        Texture2D _fadeTexture;

        readonly List<Text> _lines = new List<Text>();
        bool _laidOut;
        float _contentHeight;
        float _lastHeight;
        float _viewHeight = 1080f;
        float _scroll;
        float _velocity;
        float _velocityRate;
        float _wheel;
        float _age;
        float _endTimer;

        /// <summary>True while the screen is up.</summary>
        public bool IsShowing => _canvas != null && _canvas.gameObject.activeSelf;

        /// <summary>How the screen was opened.</summary>
        public CreditsMode Mode { get; private set; }

        /// <summary>How far the text has risen from its start, in layout pixels.</summary>
        public float ScrollOffset => _scroll;

        /// <summary>Where the roll stops: the last line in the middle of the screen.</summary>
        public float MaxScroll => Mathf.Max(0f, _contentHeight - _lastHeight * 0.5f + _viewHeight * 0.5f);

        /// <summary>The height of all the lines together.</summary>
        public float ContentHeight => _contentHeight;

        /// <summary>True once the roll has reached the last line.</summary>
        public bool AtEnd => _laidOut && _scroll >= MaxScroll - 0.01f;

        /// <summary>The text of every line, in order, for tests.</summary>
        public IReadOnlyList<string> Lines
        {
            get
            {
                var texts = new List<string>(_lines.Count);
                foreach (Text line in _lines)
                    texts.Add(line.text);
                return texts;
            }
        }

        /// <summary>The label of the button in the corner, for tests.</summary>
        public string ButtonText => _buttonLabel != null ? _buttonLabel.text : string.Empty;

        /// <summary>The line of keys at the bottom, for tests.</summary>
        public string HintText => _hint != null ? _hint.text : string.Empty;

        /// <summary>Raised when the screen closes itself or is closed by the player, with how it was opened.</summary>
        public event Action<CreditsMode> Closed;

        /// <summary>Builds the screen, hidden, under this view's object.</summary>
        internal void Build(int sortingOrder)
        {
            _canvas = HudWidgets.ScreenCanvas("Credits Canvas", transform, sortingOrder);
            HudWidgets.AllowClicks(_canvas);
            Transform root = _canvas.transform;
            _group = root.gameObject.AddComponent<CanvasGroup>();

            Image backdrop = HudWidgets.Panel("Backdrop", root, new Color(HudWidgets.Plum.r, HudWidgets.Plum.g, HudWidgets.Plum.b, 1f));
            backdrop.raycastTarget = true;

            RectTransform viewport = HudWidgets.Rect("Viewport", root);
            viewport.gameObject.AddComponent<RectMask2D>();

            _content = HudWidgets.Rect("Content", viewport);
            _content.anchorMin = new Vector2(0.5f, 1f);
            _content.anchorMax = new Vector2(0.5f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(ColumnWidth, 0f);
            _content.anchoredPosition = Vector2.zero;

            BuildLines();
            BuildFades(root);

            _hint = HudWidgets.Label("Hint", root, string.Empty, 24, HudWidgets.InkDim, TextAnchor.MiddleCenter, FontStyle.Normal);
            HudWidgets.Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(1000f, 34f));

            Button button = HudWidgets.MakeButton("Close", root, "BACK", HudWidgets.PlumLight, HudWidgets.Ink, 34);
            HudWidgets.Place((RectTransform)button.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 36f), new Vector2(260f, 76f));
            button.onClick.AddListener(Close);
            _buttonLabel = button.GetComponentInChildren<Text>();

            _canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_fade != null)
                Destroy(_fade);
            if (_fadeTexture != null)
                Destroy(_fadeTexture);
        }

        /// <summary>Shows the credits from the first line.</summary>
        public void Show(CreditsMode mode)
        {
            if (_canvas == null)
                return;

            Mode = mode;
            _scroll = 0f;
            _velocity = 0f;
            _velocityRate = 0f;
            _wheel = 0f;
            _age = 0f;
            _endTimer = 0f;
            _group.alpha = 0f;
            _hint.text = mode == CreditsMode.Ending
                ? "Space or Esc to skip"
                : "Esc to go back   |   Up and Down arrows or the mouse wheel to scroll";
            _buttonLabel.text = mode == CreditsMode.Ending ? "SKIP" : "BACK";

            _canvas.gameObject.SetActive(true);
            if (!_laidOut)
                LayOut();
            ApplyScroll();
        }

        /// <summary>Takes the screen away without telling anyone.</summary>
        public void Hide()
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(false);
        }

        /// <summary>Closes the screen and says so: the button, a key, or the end of the ending's roll.</summary>
        public void Close()
        {
            if (!IsShowing)
                return;

            Hide();
            Closed?.Invoke(Mode);
        }

        /// <summary>
        /// Moves the roll on by <paramref name="seconds"/> of unscaled time. <paramref name="manual"/> is
        /// 1 while Down is held, -1 while Up is held and 0 otherwise. The screen calls this every frame; tests
        /// call it to step the roll without waiting.
        /// </summary>
        public void Advance(float seconds, float manual = 0f)
        {
            if (!IsShowing || !_laidOut)
                return;

            RectTransform canvasRect = (RectTransform)_canvas.transform;
            if (canvasRect.rect.height > 1f)
                _viewHeight = canvasRect.rect.height;

            _age += seconds;
            _group.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_age / FadeSeconds));

            float target;
            if (manual < -0.5f)
                target = -BaseSpeed * FastFactor;
            else if (_age < StartDelay && manual < 0.5f)
                target = 0f;
            else if (AtEnd)
                target = 0f;
            else
                target = BaseSpeed * (manual > 0.5f ? FastFactor : 1f);
            _velocity = Mathf.SmoothDamp(_velocity, target, ref _velocityRate, EaseSeconds, Mathf.Infinity, seconds);
            _wheel = Mathf.MoveTowards(_wheel, 0f, WheelFade * seconds);

            _scroll = Mathf.Clamp(_scroll + (_velocity + _wheel) * seconds, 0f, MaxScroll);
            ApplyScroll();

            if (Mode == CreditsMode.Ending && AtEnd)
            {
                _endTimer += seconds;
                if (_endTimer >= EndHold)
                    Close();
            }
            else
            {
                _endTimer = 0f;
            }
        }

        /// <summary>Pushes the roll as a turn of the mouse wheel does; the push fades out.</summary>
        public void Nudge(float notches) => _wheel = Mathf.Clamp(_wheel + notches * WheelPush, -WheelPush * 3f, WheelPush * 3f);

        void Update()
        {
            if (!IsShowing)
                return;

            Keyboard keyboard = Keyboard.current;
            float manual = 0f;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame
                    || (Mode == CreditsMode.Ending && keyboard.spaceKey.wasPressedThisFrame))
                {
                    Close();
                    return;
                }

                if (keyboard.downArrowKey.isPressed)
                    manual += 1f;
                if (keyboard.upArrowKey.isPressed)
                    manual -= 1f;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f)
                    Nudge(-Mathf.Clamp(wheel / 120f, -3f, 3f));
            }

            Advance(Mathf.Min(Time.unscaledDeltaTime, 0.1f), manual);
        }

        void ApplyScroll() => _content.anchoredPosition = new Vector2(0f, -_viewHeight + _scroll);

        // ---- Building

        // Sizes are on the 1920 x 1080 layout. "Before" is the gap above the line.
        static Style StyleOf(CreditsKind kind)
        {
            switch (kind)
            {
                case CreditsKind.Title: return new Style(132, HudWidgets.Sun, FontStyle.Bold, 0f);
                case CreditsKind.Tagline: return new Style(34, HudWidgets.InkDim, FontStyle.Normal, 14f);
                case CreditsKind.Module: return new Style(52, HudWidgets.Ink, FontStyle.Bold, 44f);
                case CreditsKind.Paragraph: return new Style(34, HudWidgets.Ink, FontStyle.Normal, 56f);
                case CreditsKind.Section: return new Style(50, HudWidgets.Mint, FontStyle.Bold, 190f);
                case CreditsKind.Subsection: return new Style(36, HudWidgets.Sun, FontStyle.Bold, 70f);
                case CreditsKind.Name: return new Style(42, HudWidgets.Ink, FontStyle.Bold, 44f);
                case CreditsKind.Role: return new Style(30, HudWidgets.InkDim, FontStyle.Normal, 4f);
                case CreditsKind.Detail: return new Style(26, HudWidgets.InkDim, FontStyle.Italic, 2f);
                case CreditsKind.Credit: return new Style(30, HudWidgets.Ink, FontStyle.Normal, 18f);
                case CreditsKind.Note: return new Style(26, HudWidgets.InkDim, FontStyle.Normal, 10f);
                default: return new Style(76, HudWidgets.Sun, FontStyle.Bold, 240f);
            }
        }

        void BuildLines()
        {
            foreach (CreditsEntry entry in CreditsContent.Build())
            {
                Style style = StyleOf(entry.Kind);
                Text text = HudWidgets.Label("Line", _content, entry.Text, style.Size, style.Colour, TextAnchor.UpperCenter, style.Font);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                text.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                text.rectTransform.pivot = new Vector2(0.5f, 1f);
                text.rectTransform.sizeDelta = new Vector2(ColumnWidth, style.Size * 1.4f);
                if (entry.Kind == CreditsKind.Section || entry.Kind == CreditsKind.Subsection)
                    text.text = entry.Text.ToUpperInvariant();
                _lines.Add(text);
            }
        }

        // Stacks the lines top to bottom. It waits for the first Show, when the canvas is up and the
        // font can say how tall a wrapped line is.
        void LayOut()
        {
            IReadOnlyList<CreditsEntry> entries = CreditsContent.Build();
            float y = 0f;
            for (int i = 0; i < _lines.Count; i++)
            {
                Style style = StyleOf(entries[i].Kind);
                Text text = _lines[i];
                y += style.Before;
                float height = Mathf.Max(text.preferredHeight, style.Size * 1.2f);
                text.rectTransform.sizeDelta = new Vector2(ColumnWidth, height);
                text.rectTransform.anchoredPosition = new Vector2(0f, -y);
                y += height;
                _lastHeight = height;
            }

            _contentHeight = y;
            _content.sizeDelta = new Vector2(ColumnWidth, y);
            _laidOut = true;
        }

        // Soft fades at the top and bottom, so the lines come out of the plum and go back into it.
        void BuildFades(Transform root)
        {
            const int steps = 64;
            _fadeTexture = new Texture2D(1, steps, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[steps];
            for (int y = 0; y < steps; y++)
            {
                float t = y / (float)(steps - 1);
                // Solid over the last 40% (behind the hint and the button), then a smooth ramp into the text.
                byte alpha = (byte)Mathf.RoundToInt(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.6f)) * 255f);
                pixels[y] = new Color32(255, 255, 255, alpha);
            }

            _fadeTexture.SetPixels32(pixels);
            _fadeTexture.Apply(false, true);
            _fade = Sprite.Create(_fadeTexture, new Rect(0f, 0f, 1f, steps), new Vector2(0.5f, 0.5f), 100f);
            _fade.hideFlags = HideFlags.HideAndDontSave;

            Color plum = HudWidgets.Plum;

            // Alpha rises with y in the texture, so the top fade is solid at the top edge. The bottom one is the
            // same image turned over about its own middle, so it stays where it is put.
            Image top = HudWidgets.Panel("Fade Top", root, plum, _fade);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            top.rectTransform.pivot = new Vector2(0.5f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, FadeHeight);
            top.rectTransform.anchoredPosition = Vector2.zero;

            Image bottom = HudWidgets.Panel("Fade Bottom", root, plum, _fade);
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bottom.rectTransform.sizeDelta = new Vector2(0f, FadeHeight);
            bottom.rectTransform.anchoredPosition = new Vector2(0f, FadeHeight * 0.5f);
            bottom.rectTransform.localScale = new Vector3(1f, -1f, 1f);
        }
    }
}
