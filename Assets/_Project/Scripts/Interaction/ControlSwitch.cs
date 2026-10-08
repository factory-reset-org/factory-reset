using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A control switch, the last step of a chapter. It stays sealed until the chapter
    /// manager says every task of its chapter is done; only then does using it restore it.
    /// </summary>
    public sealed class ControlSwitch : TaskProp, IInteractable
    {
        // The chapter data refers to switches as "switch.1" to "switch.3".
        const string IdPrefix = "switch.";

        [SerializeField, Range(1, ChapterEvents.SwitchCount)] int switchNumber = 1;

        // Not serialized: after a script reload in the Editor, Unity would otherwise bring
        // this back as an empty string, which is not null, and the id would stay empty.
        [System.NonSerialized] string _id;

        public override string Id => _id ??= IdPrefix + switchNumber;

        public bool IsUnsealed { get; private set; }

        void OnEnable() => ChapterEvents.OnSwitchUnsealed += HandleUnsealed;

        void OnDisable() => ChapterEvents.OnSwitchUnsealed -= HandleUnsealed;

        void HandleUnsealed(int number)
        {
            if (number == switchNumber)
                IsUnsealed = true;
        }

        public void Interact()
        {
            if (IsUnsealed)
                Complete();
        }
    }
}
