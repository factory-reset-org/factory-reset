using UnityEngine;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Shows the player that the Captain has read them: a red flash on the goal it has just
    /// committed to (a ring and a short beam, S1's beacon look in red). When to flash is
    /// <see cref="CaptainCallouts"/> (pure, tested); this only shows it.
    /// </summary>
    /// <remarks>
    /// The flash shows wherever the Captain is, so the player learns it has been read even
    /// when it is out of sight. Nothing shows while the game is frozen (cutscene, pause) or the
    /// Captain is down. (The comic speech bubbles it used to say above its head were removed
    /// on 15 October; the cutscenes keep their own comic words.)
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class CaptainCallout : MonoBehaviour
    {
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

        Transform _marker;
        Transform _ring;
        MeshRenderer _ringRenderer;
        MeshRenderer _beamRenderer;
        MaterialPropertyBlock _block;
        float _ringIntensity;
        float _beamIntensity;
        Vector3 _markPoint;
        float _markSince = float.PositiveInfinity;

        /// <summary>True while the red marker is flashing.</summary>
        public bool IsMarking => _marker != null && _marker.gameObject.activeSelf;

        /// <summary>Where the marker stands (the committed goal), while it flashes.</summary>
        public Vector3 MarkPoint => _markPoint;

        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            BuildMarker();
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
                Hide();
                return;
            }
            // Down: its plan is gone, so the commitment ends (a new one after the reboot is
            // fresh, not a change of mind).
            if (_agent.IsDisabled || _agent.IsDead)
            {
                Hide();
                _callouts.Update(Now, false, default);
                return;
            }
            if (!(_agent.Brain is CaptainBrain brain))
                return;

            bool committed = brain.TryGetCommitment(out CandidateGoal goal, out Vector3 goalPosition, out _);
            if (_callouts.Update(Now, committed, goal))
                Mark(goalPosition);
            UpdateMarker();
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

        void Hide()
        {
            if (_marker != null && _marker.gameObject.activeSelf)
                _marker.gameObject.SetActive(false);
        }
    }
}
