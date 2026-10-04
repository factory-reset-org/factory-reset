using UnityEngine;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// A named spot in Env where S2 places a task prop (lever, plate, terminal, relay...).
    /// Scenes cannot reference each other's objects, so props and chapter data find their
    /// place by <see cref="AnchorId"/> instead of by a scene reference.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TaskAnchor : MonoBehaviour
    {
        [Tooltip("Stable id, e.g. \"ch1.lever\". Chapter data and props refer to the anchor by this id.")]
        [SerializeField] string anchorId;

        [Tooltip("Chapter whose task uses this anchor, 1 to 4; 0 for anchors used in every chapter.")]
        [SerializeField, Range(0, 4)] int chapter;

        [Tooltip("Why the prop stands here: the sightline, exposure or travel it creates.")]
        [SerializeField, TextArea(2, 4)] string placementReason;

        public string AnchorId => anchorId;
        public int Chapter => chapter;
        public string PlacementReason => placementReason;
        public Vector3 Position => transform.position;

#if UNITY_EDITOR
        static readonly Color[] ChapterColors =
        {
            new Color(0.85f, 0.85f, 0.85f), // shared
            new Color(0.24f, 0.86f, 0.69f), // 1 Assembly
            new Color(1f, 0.36f, 0.66f),    // 2 Painting
            new Color(1f, 0.62f, 0.11f),    // 3 Storage
            new Color(0.56f, 0.42f, 1f)     // 4 Control
        };

        void OnDrawGizmos()
        {
            Gizmos.color = ChapterColors[Mathf.Clamp(chapter, 0, ChapterColors.Length - 1)];
            Vector3 position = transform.position;
            Gizmos.DrawWireSphere(position + Vector3.up * 0.25f, 0.25f);
            Gizmos.DrawLine(position, position + Vector3.up * 1.5f);
            UnityEditor.Handles.Label(position + Vector3.up * 1.7f, anchorId);
        }
#endif
    }
}
