using UnityEngine;
using ToyFactory.Player;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A slippery patch of floor (spilt paint). While the player is inside its trigger,
    /// their speed only slowly follows their input, so they slide on and overshoot.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SlipperyFloor : MonoBehaviour
    {
        [Tooltip("How quickly the player's speed follows their input, per second. Lower is slipperier.")]
        [SerializeField, Min(0.1f)] float traction = 1.5f;

        int _playerLayer;
        Collider _zone;

        public float Traction => traction;

        void Awake()
        {
            _playerLayer = LayerMask.NameToLayer("Player");
            _zone = GetComponent<Collider>();
        }

        /// <summary>
        /// True if <paramref name="point"/> is inside the patch. The player checks this, because
        /// no exit is reported when their collider is switched off inside the patch (a respawn
        /// or a move by a cutscene), and they would otherwise stay slippery everywhere.
        /// </summary>
        public bool Contains(Vector3 point) => _zone != null && _zone.enabled && _zone.bounds.Contains(point);

        void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == _playerLayer && other.TryGetComponent(out PlayerController player))
                player.EnterSlipperyFloor(this);
        }

        void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer == _playerLayer && other.TryGetComponent(out PlayerController player))
                player.ExitSlipperyFloor(this);
        }
    }
}
