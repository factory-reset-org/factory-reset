using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Player;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// The charging station. Using it fills the blaster's battery and every spare cell.
    /// It stays where it is and can be used as often as the player likes.
    /// </summary>
    public sealed class Charger : MonoBehaviour, IInteractable
    {
        static readonly Color ChargeColour = new Color(0.24f, 0.86f, 0.69f);

        void Start() => PropGlow.Attach(gameObject, ChargeColour, 3f, 0.55f, 0.25f, 5f, new Vector3(0f, 1.2f, 0f));

        public void Interact()
        {
            if (!(PlayerState.Current is Component player) || !player.TryGetComponent(out PlayerBattery battery))
                return;

            battery.RefillAll();
            PropEffects.Spark(transform.position + Vector3.up * 1.2f, ChargeColour);
            GameSfx.Play(Sfx.Pickup);
        }
    }
}
