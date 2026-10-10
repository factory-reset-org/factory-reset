using System;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A control switch, the last step of a chapter. It stays sealed until the chapter
    /// manager says every task of its chapter is done; only then does using it restore it.
    /// Its colour shows which: red while sealed, amber once it can be used, green once restored.
    /// </summary>
    public sealed class ControlSwitch : TaskProp, IInteractable
    {
        // The chapter data refers to switches as "switch.1" to "switch.3".
        const string IdPrefix = "switch.";

        static readonly Color SealedColour = new Color(1f, 0.19f, 0.25f);
        static readonly Color ReadyColour = new Color(1f, 0.67f, 0f);
        static readonly Color RestoredColour = new Color(0.24f, 0.86f, 0.69f);

        [SerializeField, Range(1, ChapterEvents.SwitchCount)] int switchNumber = 1;

        // Not serialized: after a script reload in the Editor, Unity would otherwise bring
        // this back as an empty string, which is not null, and the id would stay empty.
        [NonSerialized] string _id;

        Renderer _body;
        PropGlow _glow;

        public override string Id => _id ??= IdPrefix + switchNumber;

        public bool IsUnsealed { get; private set; }

        /// <summary>
        /// Raised, with the switch's number, when the player uses a switch that is still sealed.
        /// The HUD shows "Sealed. Finish the room's tasks first."
        /// </summary>
        public static event Action<int> OnSealedUseAttempted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearListeners() => OnSealedUseAttempted = null;   // domain reload is off

        void Awake()
        {
            _body = GetComponentInChildren<Renderer>();
            Show(SealedColour);
        }

        void Start() => _glow = PropGlow.Attach(gameObject, SealedColour, 2.2f, 0.4f, 0.3f, 3f, new Vector3(0f, 0.6f, 0f));

        void OnEnable() => ChapterEvents.OnSwitchUnsealed += HandleUnsealed;

        void OnDisable() => ChapterEvents.OnSwitchUnsealed -= HandleUnsealed;

        void HandleUnsealed(int number)
        {
            if (number != switchNumber)
                return;

            IsUnsealed = true;
            Show(ReadyColour);
        }

        public void Interact()
        {
            if (IsCompleted)
                return;

            if (!IsUnsealed)
            {
                GameSfx.Play(Sfx.Empty);
                OnSealedUseAttempted?.Invoke(switchNumber);
                return;
            }

            Complete();
            Show(RestoredColour);
            Vector3 at = transform.position;
            PropEffects.Spark(at + Vector3.up * 0.6f, RestoredColour);
            PropEffects.Word("clunk", at + Vector3.up * 1.6f, 1.2f);
            GameSfx.Play(Sfx.Switch);
        }

        void Show(Color colour)
        {
            PropTint.Set(_body, colour);
            if (_glow != null)
                _glow.Colour = colour;
        }
    }
}
