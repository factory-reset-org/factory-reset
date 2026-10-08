using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Player;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// The charging station. Using it fills the blaster's battery and every spare cell.
    /// It stays where it is and can be used as often as the player likes.
    /// </summary>
    public sealed class Charger : MonoBehaviour, IInteractable
    {
        public void Interact()
        {
            if (PlayerState.Current is Component player && player.TryGetComponent(out PlayerBattery battery))
                battery.RefillAll();
        }
    }
}
