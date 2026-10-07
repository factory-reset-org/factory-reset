using UnityEngine;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// An item the player picks up by walking into it (a spare fuse). Needs a trigger
    /// collider on the Pickup layer, which only collides with the Player layer.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Collectible : TaskProp
    {
        [Tooltip("Must equal the task id in the chapter data, e.g. \"ch1.fuse.1\".")]
        [SerializeField] string taskId;

        [SerializeField] float spinDegreesPerSecond = 90f;

        int _playerLayer;

        public override string Id => taskId;

        void Awake() => _playerLayer = LayerMask.NameToLayer("Player");

        void Update() => transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);

        void OnTriggerEnter(Collider other)
        {
            if (IsCompleted || other.gameObject.layer != _playerLayer)
                return;

            Complete();
            gameObject.SetActive(false);
        }
    }
}
