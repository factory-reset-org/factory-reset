using UnityEngine;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// One relay of the <see cref="RelayBoard"/> task. Using it tells the board, which
    /// decides whether it was the right one; the relay only shows whether it is on.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Relay : MonoBehaviour, IInteractable
    {
        [Tooltip("Which relay this is, 1-based. Matches its task anchor (ch3.relay.1 is relay 1).")]
        [SerializeField, Min(1)] int number = 1;

        [Tooltip("Its own colour, which the board uses to show the order.")]
        [SerializeField] Color colour = Color.red;

        [Tooltip("Recoloured to show on and off. Left empty, the first renderer on this object or its children.")]
        [SerializeField] Renderer body;

        [Tooltip("How bright the colour is while the relay is off.")]
        [SerializeField, Range(0f, 1f)] float offBrightness = 0.3f;

        RelayBoard _board;

        public int Number => number;

        public Color Colour => colour;

        public bool IsOn { get; private set; }

        void Awake()
        {
            if (body == null)
                body = GetComponentInChildren<Renderer>();
            Show();
        }

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

        void Show()
        {
            Color shown = IsOn ? colour : colour * offBrightness;
            shown.a = 1f;
            PropTint.Set(body, shown);
        }
    }
}
