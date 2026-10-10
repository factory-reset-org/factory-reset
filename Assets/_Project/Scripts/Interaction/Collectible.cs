using UnityEngine;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A task item the player collects by walking into it (a spare fuse, the master keycard).
    /// It spins and bobs with a glow round it, and completes its task when taken. The chapter
    /// manager lets pickups count even before their chapter starts.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Collectible : TaskProp
    {
        static readonly Color FuseColour = new Color(1f, 0.62f, 0.11f);
        static readonly Color KeycardColour = new Color(1f, 0.79f, 0.2f);

        [Tooltip("Must equal the task id in the chapter data, e.g. \"ch1.fuse.1\".")]
        [SerializeField] string taskId;

        [SerializeField] float spinDegreesPerSecond = 115f;

        [SerializeField, Min(0f)] float bobHeight = 0.12f;
        [SerializeField, Min(0f)] float bobSpeed = 3f;

        int _playerLayer;
        Vector3 _home;
        float _phase;

        public override string Id => taskId;

        // The keycard is told from a fuse by its task id, so no prefab has to be edited.
        bool IsKeycard => taskId != null && taskId.Contains("keycard");

        Color Colour => IsKeycard ? KeycardColour : FuseColour;

        void Awake()
        {
            _playerLayer = LayerMask.NameToLayer("Player");
            _phase = Random.value * 6.28f;
        }

        void Start()
        {
            _home = transform.position;
            PropGlow.Attach(gameObject, Colour, IsKeycard ? 1.5f : 1.4f, IsKeycard ? 0.65f : 0.7f);
        }

        void Update()
        {
            transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
            transform.position = _home + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + _phase) * bobHeight);
        }

        void OnTriggerEnter(Collider other)
        {
            if (IsCompleted || other.gameObject.layer != _playerLayer)
                return;

            Vector3 at = transform.position;
            PropEffects.Spark(at, Colour);
            PropEffects.Word(IsKeycard ? "gotit" : "fuse", at + Vector3.up * 0.9f);
            GameSfx.Play(Sfx.Pickup);

            Complete();
            gameObject.SetActive(false);
        }
    }
}
