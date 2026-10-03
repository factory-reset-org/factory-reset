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
    }
}
