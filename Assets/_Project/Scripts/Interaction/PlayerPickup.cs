using UnityEngine;
using ToyFactory.Player;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// Base for something the player picks up by walking into it and that acts on the
    /// blaster's battery. It spins in place, and disappears once it has been taken.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public abstract class PlayerPickup : MonoBehaviour
    {
        [SerializeField] float spinDegreesPerSecond = 90f;

        int _playerLayer;
        PlayerBattery _battery;

        void Awake() => _playerLayer = LayerMask.NameToLayer("Player");

        void Update() => transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);

        // Stay, not Enter: a pickup the player could not take (already carrying the most)
        // is taken as soon as there is room, without them having to step off and on again.
        void OnTriggerStay(Collider other)
        {
            if (other.gameObject.layer != _playerLayer)
                return;
            if (_battery == null)
                _battery = other.GetComponentInParent<PlayerBattery>();
            if (_battery != null && Collect(_battery))
                gameObject.SetActive(false);
        }

        /// <summary>Gives the pickup to the player. False if they cannot take it right now.</summary>
        protected abstract bool Collect(PlayerBattery battery);
    }
}
