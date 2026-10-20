using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The "?" or "!" above an agent's head, from <see cref="AgentController.Alert"/>: "?" in
    /// yellow when it is suspicious, "!" in red when it has the player, nothing otherwise. The
    /// icon faces the camera, pops in with a short overshoot whenever the level rises, and
    /// hides while the agent is down, scrapped or frozen in a cutscene.
    /// </summary>
    /// <remarks>
    /// A plain 3D <see cref="TextMesh"/>, so it needs no UI canvas and no font asset of its own.
    /// It only touches the text when the level changes, so it costs nothing while steady.
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class AlertIcon : MonoBehaviour
    {
        [Tooltip("Height of the icon above the agent's feet, in metres.")]
        [SerializeField, Min(0f)] float height = 2f;

        [Tooltip("Size of the icon.")]
        [SerializeField, Min(0.01f)] float size = 0.12f;

        [Tooltip("Seconds of the pop-in when the level rises.")]
        [SerializeField, Min(0.01f)] float popSeconds = 0.25f;

        [SerializeField] Color suspiciousColour = new Color(1f, 0.85f, 0.2f);
        [SerializeField] Color alertColour = new Color(1f, 0.25f, 0.2f);

        AgentController _agent;
        Transform _icon;
        TextMesh _text;
        AlertLevel _shown;
        float _popTime = float.PositiveInfinity;

        /// <summary>The level the icon is showing now.</summary>
        public AlertLevel Shown => _shown;

        void Awake()
        {
            _agent = GetComponent<AgentController>();

            var icon = new GameObject("AlertIcon");
            _icon = icon.transform;
            _icon.SetParent(transform, false);
            _icon.localPosition = Vector3.up * height;
            _text = icon.AddComponent<TextMesh>();
            // Unity's built-in font, so the icon needs no font asset of its own.
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.font = font;
            icon.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontStyle = FontStyle.Bold;
            _text.fontSize = 64;
            _text.characterSize = size;
            _text.text = string.Empty;
            icon.SetActive(false);
        }

        void LateUpdate()
        {
            AlertLevel level = _agent.Alert;
            if (level != _shown)
                Show(level);

            if (_shown == AlertLevel.None)
                return;

            // Face the camera, and pop: overshoot to 130% then settle.
            Camera camera = Camera.main;
            if (camera != null)
                _icon.rotation = Quaternion.LookRotation(_icon.position - camera.transform.position, camera.transform.up);

            _popTime += Time.deltaTime;
            float t = Mathf.Clamp01(_popTime / popSeconds);
            float scale = t < 0.6f ? Mathf.Lerp(0f, 1.3f, t / 0.6f) : Mathf.Lerp(1.3f, 1f, (t - 0.6f) / 0.4f);
            _icon.localScale = Vector3.one * scale;
        }

        void Show(AlertLevel level)
        {
            bool rose = level > _shown;
            _shown = level;
            _icon.gameObject.SetActive(level != AlertLevel.None);
            if (level == AlertLevel.None)
                return;

            _text.text = level == AlertLevel.Alert ? "!" : "?";
            _text.color = level == AlertLevel.Alert ? alertColour : suspiciousColour;
            if (rose)
                _popTime = 0f;
        }
    }
}
