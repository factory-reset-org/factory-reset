using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// Latches when a crate comes to rest on it. Only a settled crate counts: one still
    /// riding the belt or being pushed does not, and neither does the player's weight.
    /// </summary>
    public sealed class PressurePlate : TaskProp
    {
        [Tooltip("Must equal the task id in the chapter data.")]
        [SerializeField] string taskId = "ch1.plate";

        [Header("Detection zone, in metres from the plate's position")]
        [SerializeField] Vector3 zoneCenter = new Vector3(0f, 0.5f, 0f);
        [SerializeField] Vector3 zoneHalfExtents = new Vector3(0.3f, 0.4f, 0.3f);

        [Tooltip("Layers a crate can be on. Left empty, the Pushable layer is used.")]
        [SerializeField] LayerMask crateMask;

        [SerializeField, Min(0.05f)] float checkInterval = 0.2f;

        static readonly Color WaitingColour = new Color(1f, 0.19f, 0.25f);
        static readonly Color LatchedColour = new Color(0.24f, 0.86f, 0.69f);

        readonly Collider[] _overlaps = new Collider[4];
        float _nextCheckTime;
        Renderer _body;

        public override string Id => taskId;

        void Awake()
        {
            if (crateMask.value == 0)
                crateMask = LayerMask.GetMask("Pushable");

            // Red until a crate is on it, green after.
            _body = GetComponentInChildren<Renderer>();
            PropTint.Set(_body, WaitingColour);
        }

        void Update()
        {
            if (IsCompleted || Time.time < _nextCheckTime)
                return;
            _nextCheckTime = Time.time + checkInterval;

            int count = Physics.OverlapBoxNonAlloc(ZoneWorldCenter(), zoneHalfExtents, _overlaps,
                transform.rotation, crateMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (_overlaps[i].TryGetComponent(out PushableBox crate) && crate.IsSettled)
                {
                    Latch();
                    return;
                }
            }
        }

        void Latch()
        {
            EmitNoise(NoiseLoudness.PlateClick);
            PropTint.Set(_body, LatchedColour);
            PropEffects.Word("click", transform.position + Vector3.up * 1.6f);
            PropEffects.Spark(transform.position + Vector3.up * 0.2f, LatchedColour);
            GameSfx.Play(Sfx.Switch);
            Complete();
        }

        Vector3 ZoneWorldCenter() => transform.position + transform.rotation * zoneCenter;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.matrix = Matrix4x4.TRS(ZoneWorldCenter(), transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, zoneHalfExtents * 2f);
        }
#endif
    }
}
