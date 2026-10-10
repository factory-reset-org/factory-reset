using UnityEngine;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// One relay of the <see cref="RelayBoard"/> task. Using it tells the board, which
    /// decides whether it was the right one; the relay only shows whether it is on, with a
    /// glow in its colour, and flickers red when the board reports a wrong order.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Relay : MonoBehaviour, IInteractable
    {
        static readonly Color AlarmColour = new Color(1f, 0.13f, 0.2f);

        [Tooltip("Which relay this is, 1-based. Matches its task anchor (ch3.relay.1 is relay 1).")]
        [SerializeField, Min(1)] int number = 1;

        [Tooltip("Its own colour, which the board uses to show the order.")]
        [SerializeField] Color colour = Color.red;

        [Tooltip("Recoloured to show on and off. Left empty, the first renderer on this object or its children.")]
        [SerializeField] Renderer body;

        [Tooltip("How bright the colour is while the relay is off.")]
        [SerializeField, Range(0f, 1f)] float offBrightness = 0.3f;

        RelayBoard _board;
        PropGlow _glow;
        float _alarmUntil;

        public int Number => number;

        public Color Colour => colour;

        public bool IsOn { get; private set; }

        /// <summary>The comic word for this relay's colour: red, green or blue.</summary>
        public string ColourWord
        {
            get
            {
                if (colour.r >= colour.g && colour.r >= colour.b)
                    return "red";
                return colour.g >= colour.b ? "green" : "blue";
            }
        }

        void Awake()
        {
            if (body == null)
                body = GetComponentInChildren<Renderer>();
            Show();
        }

        void Start() => _glow = PropGlow.Attach(gameObject, colour, 2.6f, 0.5f, 0.3f, 3f, new Vector3(0f, 0.7f, 0f));

        internal void Bind(RelayBoard board) => _board = board;

        public void Interact()
        {
            if (_board != null)
                _board.Press(this);
        }

        internal void SetOn(bool on)
        {
            IsOn = on;
            Show();
        }

        /// <summary>Flickers red for <paramref name="seconds"/>, for a wrong order.</summary>
        internal void Alarm(float seconds) => _alarmUntil = Time.time + seconds;

        void Update()
        {
            bool alarm = Time.time < _alarmUntil;
            if (alarm)
            {
                // Fast red blinking, as the prototype's wrong-order flicker.
                bool bright = Mathf.Sin(Time.time * 30f) > 0f;
                PropTint.Set(body, AlarmColour * (bright ? 1f : 0.2f));
            }
            else if (_alarmUntil > 0f)
            {
                _alarmUntil = 0f;
                Show();
            }

            if (_glow != null)
            {
                _glow.Colour = alarm ? AlarmColour : colour;
                _glow.Intensity = alarm ? 1f : IsOn ? 1f : 0.3f;
            }
        }

        void Show()
        {
            Color shown = IsOn ? colour : colour * offBrightness;
            shown.a = 1f;
            PropTint.Set(body, shown);
        }
    }
}
