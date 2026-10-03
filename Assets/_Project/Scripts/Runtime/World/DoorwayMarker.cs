using UnityEngine;

namespace ToyFactory.Runtime.World
{
    /// <summary>Identifies the grid cells and initial state owned by one runtime door.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class DoorwayMarker : MonoBehaviour
    {
        [SerializeField, Min(0)] int doorId;
        [SerializeField] bool initiallyClosed = true;

        public int DoorId => doorId;
        public bool InitiallyClosed => initiallyClosed;

        internal BoxCollider Volume => GetComponent<BoxCollider>();

        internal bool Contains(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition) - Volume.center;
            Vector3 half = Volume.size * 0.5f;
            const float epsilon = 0.00001f;
            return Mathf.Abs(local.x) <= half.x + epsilon &&
                Mathf.Abs(local.y) <= half.y + epsilon &&
                Mathf.Abs(local.z) <= half.z + epsilon;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            BoxCollider volume = Volume;
            if (volume == null) return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = initiallyClosed
                ? new Color(1f, 0.55f, 0.05f, 0.9f)
                : new Color(0.1f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(volume.center, volume.size);
            Gizmos.matrix = Matrix4x4.identity;

            UnityEditor.Handles.Label(transform.TransformPoint(volume.center),
                $"Door {doorId} ({(initiallyClosed ? "initially closed" : "initially open")})");
        }
#endif
    }
}
