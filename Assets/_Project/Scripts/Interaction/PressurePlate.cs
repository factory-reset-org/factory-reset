using UnityEngine;
using ToyFactory.Interfaces;

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

        readonly Collider[] _overlaps = new Collider[4];
        float _nextCheckTime;

        public override string Id => taskId;

        void Awake()
        {
            if (crateMask.value == 0)
                crateMask = LayerMask.GetMask("Pushable");
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
            float time = GameClock.Current != null ? GameClock.Current.GameTime : Time.time;
            NoiseEvents.Emit(new NoiseEvent(transform.position, NoiseLoudness.PlateClick, GetHashCode(), time));
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
