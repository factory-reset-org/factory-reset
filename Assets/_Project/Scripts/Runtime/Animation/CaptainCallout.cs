using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Shows the player that the Captain has read them: a red flash on the goal it has just
    /// committed to (a ring and a short beam, S1's beacon look in red), and a comic speech
    /// bubble above its head, such as "THE CONSOLE. OF COURSE." When to do which is
    /// <see cref="CaptainCallouts"/> (pure, tested); this only shows it.
    /// </summary>
    /// <remarks>
    /// <para>The line is only said where the player can see the Captain: on screen and not
    /// behind a wall. Out of sight there is only the flash. The bubble faces the camera, pops
    /// in like the cutscene comic words, and grows with distance, so it stays readable from
    /// across a room.</para>
    /// <para>Each line is a pre-drawn bubble in the cutscene comic words' style (dark outline,
    /// the Captain's orange, white lettering), found by <see cref="CaptainCallouts.BubbleName"/>.
    /// Nothing shows while the game is frozen (cutscene, pause) or the Captain is down.</para>
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class CaptainCallout : MonoBehaviour
    {
        [Header("Line")]
        [Tooltip("The comic speech bubbles, one per line (Textures/FX/CaptainSays), found by name.")]
        [SerializeField] Sprite[] bubbles = new Sprite[0];

        [Tooltip("Height of the bubble's tail tip above the Captain's feet, in metres (above the \"!\" icon).")]
        [SerializeField, Min(0f)] float lineHeight = 4.3f;

        [Tooltip("Size of the bubble up close, as a multiple of the sprite's own size.")]
        [SerializeField, Min(0.01f)] float bubbleScale = 1.25f;

        [Tooltip("Seconds the line shows, including its fade.")]
        [SerializeField, Min(0.5f)] float lineSeconds = 2.8f;

        [Tooltip("Beyond this distance (m) the bubble grows with distance, so it keeps its size on screen.")]
        [SerializeField, Min(0.1f)] float growFrom = 7f;

        [Tooltip("Largest growth, as a multiple of the close-up size.")]
        [SerializeField, Min(1f)] float maxGrow = 3f;

        [Tooltip("What hides the Captain from the camera: walls, not agents or props.")]
        [SerializeField] LayerMask sightBlockers = 1;   // Default

        [Header("Prediction marker")]
        [SerializeField] Mesh ringMesh;
        [SerializeField] Mesh beamMesh;
        [SerializeField] Material ringMaterial;
        [SerializeField] Material beamMaterial;

        [Tooltip("Seconds of the flash on the predicted goal.")]
        [SerializeField, Min(0.2f)] float markSeconds = 2f;

        [Tooltip("Height of the beam as a share of the mesh's (S1's beam is 5 m).")]
        [SerializeField, Min(0.05f)] float beamScale = 0.6f;

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        readonly CaptainCallouts _callouts = new CaptainCallouts();
        AgentController _agent;

        readonly System.Collections.Generic.Dictionary<string, Sprite> _bubbleByName =
            new System.Collections.Generic.Dictionary<string, Sprite>();
        Transform _line;
        SpriteRenderer _bubble;
        string _lineText;
        float _lineSince = float.PositiveInfinity;

        Transform _marker;
        Transform _ring;
        MeshRenderer _ringRenderer;
        MeshRenderer _beamRenderer;
        MaterialPropertyBlock _block;
        float _ringIntensity;
        float _beamIntensity;
        Vector3 _markPoint;
        float _markSince = float.PositiveInfinity;

        /// <summary>The line showing now, or null.</summary>
        public string Line => _line != null && _line.gameObject.activeSelf ? _lineText : null;

        /// <summary>True while the red marker is flashing.</summary>
        public bool IsMarking => _marker != null && _marker.gameObject.activeSelf;

        /// <summary>Where the marker stands (the committed goal), while it flashes.</summary>
        public Vector3 MarkPoint => _markPoint;

        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            BuildLine();
            BuildMarker();
        }

        void BuildLine()
        {
            var line = new GameObject("CalloutLine");
            _line = line.transform;
            _line.SetParent(transform, false);
            _line.localPosition = Vector3.up * lineHeight;
            // The bubbles' pivot is the bottom centre (the tail), so the tail points at the Captain.
            _bubble = line.AddComponent<SpriteRenderer>();
            _bubble.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _bubble.receiveShadows = false;
            foreach (Sprite bubble in bubbles)
                if (bubble != null)
                    _bubbleByName[bubble.name] = bubble;
            line.SetActive(false);
        }

        void BuildMarker()
        {
            var marker = new GameObject("PredictionMarker");
            _marker = marker.transform;
            // A child of the Captain so it goes when the Captain does; its world position is
            // pinned to the goal every frame.
            _marker.SetParent(transform, false);
            _block = new MaterialPropertyBlock();
            if (ringMesh != null && ringMaterial != null)
            {
                _ringRenderer = AddPart(_marker, "Ring", ringMesh, ringMaterial, new Vector3(0f, 0.05f, 0f));
                _ring = _ringRenderer.transform;
                _ringIntensity = ringMaterial.HasProperty(IntensityId) ? ringMaterial.GetFloat(IntensityId) : 1f;
            }
            if (beamMesh != null && beamMaterial != null)
            {
                _beamRenderer = AddPart(_marker, "Beam", beamMesh, beamMaterial, Vector3.zero);
                _beamRenderer.transform.localScale = new Vector3(1f, beamScale, 1f);
                _beamIntensity = beamMaterial.HasProperty(IntensityId) ? beamMaterial.GetFloat(IntensityId) : 1f;
            }
            marker.SetActive(false);
        }

        static MeshRenderer AddPart(Transform parent, string name, Mesh mesh, Material material, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        void LateUpdate()
        {
            // Frozen: game time stands still, so the schedule waits too, and nothing shows
            // over a cutscene or the pause menu.
            if (_agent.IsFrozen)
            {
                HideAll();
                return;
            }
            // Down: its plan is gone, so the commitment ends (a new one after the reboot is
            // fresh, not a change of mind).
            if (_agent.IsDisabled || _agent.IsDead)
            {
                HideAll();
                _callouts.Update(Now, false, default, false, false, false);
                return;
            }
            if (!(_agent.Brain is CaptainBrain brain))
                return;

            Camera camera = Camera.main;
            bool visible = camera != null && CanBeSeen(camera);
            bool committed = brain.TryGetCommitment(out CandidateGoal goal, out Vector3 goalPosition, out bool guarding);
            bool finalChapter = _agent.World != null && _agent.World.IsFinalChapter;

            Callout callout = _callouts.Update(Now, committed, goal, guarding, finalChapter, visible);
            if (callout.Mark)
                Mark(goalPosition);
            if (callout.Line != null)
                Say(callout.Line);

            UpdateLine(camera, visible);
            UpdateMarker();
        }

        // On screen and not behind a wall. The line of sight is to the Captain's chest; props
        // and agents do not hide a 3.25 m robot.
        bool CanBeSeen(Camera camera)
        {
            Vector3 chest = transform.position + Vector3.up * 2f;
            Vector3 viewport = camera.WorldToViewportPoint(chest);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                return false;
            // The first thing hit being the Captain itself means nothing nearer is in the way.
            return !Physics.Linecast(camera.transform.position, chest, out RaycastHit hit, sightBlockers, QueryTriggerInteraction.Ignore) ||
                hit.transform.IsChildOf(transform);
        }

        void Say(string line)
        {
            if (!_bubbleByName.TryGetValue(CaptainCallouts.BubbleName(line), out Sprite bubble))
            {
                Debug.LogWarning($"{nameof(CaptainCallout)}: no bubble for \"{line}\" ({CaptainCallouts.BubbleName(line)}).", this);
                return;
            }
            _bubble.sprite = bubble;
            _lineText = line;
            _lineSince = 0f;
            _line.gameObject.SetActive(true);
        }

        void UpdateLine(Camera camera, bool visible)
        {
            if (!_line.gameObject.activeSelf)
                return;
            _lineSince += Time.deltaTime;
            // Ends early if the Captain goes out of sight: a line through a wall would give it away.
            if (_lineSince >= lineSeconds || !visible || camera == null)
            {
                _line.gameObject.SetActive(false);
                return;
            }

            Vector3 toLine = _line.position - camera.transform.position;
            _line.rotation = Quaternion.LookRotation(toLine, camera.transform.up);
            float grow = Mathf.Clamp(toLine.magnitude / growFrom, 1f, maxGrow);
            // Pops in with an overshoot like the cutscene comic words, fades out over the last 0.5 s.
            float pop = _lineSince < 0.12f ? Mathf.Lerp(0f, 1.15f, _lineSince / 0.12f) : Mathf.Lerp(1.15f, 1f, (_lineSince - 0.12f) / 0.1f);
            _line.localScale = Vector3.one * (bubbleScale * grow * pop);
            float alpha = Mathf.Clamp01((lineSeconds - _lineSince) / 0.5f);
            _bubble.color = new Color(1f, 1f, 1f, alpha);
        }

        void Mark(Vector3 point)
        {
            if (_ringRenderer == null && _beamRenderer == null)
                return;
            _markPoint = point;
            _markSince = 0f;
            _marker.gameObject.SetActive(true);
        }

        void UpdateMarker()
        {
            if (!_marker.gameObject.activeSelf)
                return;
            _markSince += Time.deltaTime;
            if (_markSince >= markSeconds)
            {
                _marker.gameObject.SetActive(false);
                return;
            }

            _marker.SetPositionAndRotation(_markPoint, Quaternion.identity);
            float t = _markSince / markSeconds;
            // Flashes up in 0.15 s, holds, fades over the last 40%; the ring spreads like a ping.
            float fadeIn = Mathf.Clamp01(_markSince / 0.15f);
            float fadeOut = Mathf.Clamp01((1f - t) / 0.4f);
            float strength = fadeIn * fadeOut;
            if (_ring != null)
            {
                float spread = Mathf.Lerp(0.5f, 1.3f, 1f - (1f - t) * (1f - t));
                _ring.localScale = new Vector3(spread, 1f, spread);
            }
            SetIntensity(_ringRenderer, _ringIntensity * strength);
            SetIntensity(_beamRenderer, _beamIntensity * strength);
        }

        void SetIntensity(MeshRenderer renderer, float intensity)
        {
            if (renderer == null)
                return;
            renderer.GetPropertyBlock(_block);
            _block.SetFloat(IntensityId, intensity);
            renderer.SetPropertyBlock(_block);
        }

        void HideAll()
        {
            if (_line != null && _line.gameObject.activeSelf)
                _line.gameObject.SetActive(false);
            if (_marker != null && _marker.gameObject.activeSelf)
                _marker.gameObject.SetActive(false);
        }
    }
}
