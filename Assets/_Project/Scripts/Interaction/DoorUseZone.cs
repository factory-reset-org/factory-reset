using UnityEngine;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// The spot in the doorway the player aims at to use a door. It stays where the panel
    /// stood when closed, so an open door can still be closed after its panel has slid or
    /// swung away. Created by <see cref="Door"/> at start-up, never placed by hand.
    /// </summary>
    public sealed class DoorUseZone : MonoBehaviour, IInteractable
    {
        Door _door;

        public void Bind(Door door) => _door = door;

        public void Interact()
        {
            if (_door != null)
                _door.Interact();
        }
    }
}
