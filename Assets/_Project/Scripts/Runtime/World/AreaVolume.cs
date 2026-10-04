using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Marks one room of the level and the chapter that takes place in it, so the
    /// chapter flow and the HUD can tell which room a position is in.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class AreaVolume : MonoBehaviour
    {
        static readonly List<AreaVolume> s_active = new List<AreaVolume>();

        [SerializeField, Range(1, 4)] int chapter = 1;
        [SerializeField] string displayName;

        public int Chapter => chapter;
        public string DisplayName => displayName;

        BoxCollider Volume => GetComponent<BoxCollider>();

        void OnEnable() => s_active.Add(this);
        void OnDisable() => s_active.Remove(this);

        // Domain reload is off, so the static list survives between play sessions unless cleared.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_active.Clear();

        /// <summary>True if the world position lies inside this room's box.</summary>
        public bool Contains(Vector3 worldPosition)
        {
            BoxCollider volume = Volume;
            Vector3 local = transform.InverseTransformPoint(worldPosition) - volume.center;
            Vector3 half = volume.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y &&
                Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>Finds the enabled room that contains the position (null in walls and doorways).</summary>
        public static AreaVolume Find(Vector3 worldPosition)
        {
            for (int i = 0; i < s_active.Count; i++)
            {
                if (s_active[i].Contains(worldPosition))
                    return s_active[i];
            }

            return null;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            BoxCollider volume = Volume;
            if (volume == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawWireCube(volume.center, volume.size);
            Gizmos.matrix = Matrix4x4.identity;
            UnityEditor.Handles.Label(transform.TransformPoint(volume.center),
                $"Chapter {chapter}: {displayName}");
        }
#endif
    }
}
