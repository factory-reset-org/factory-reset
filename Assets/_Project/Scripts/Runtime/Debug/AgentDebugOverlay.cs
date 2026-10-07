using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// The in-game debug overlay: press F3 to show what every agent is doing and thinking,
    /// drawn over the level. Each frame it asks every registered <see cref="IAgentOverlayLayer"/>
    /// to draw the agents it handles into an <see cref="OverlayCanvas"/>, then renders the lines
    /// (through walls, so routes behind cover stay visible) and the labels.
    /// </summary>
    /// <remarks>
    /// <para><b>Layers:</b> the base layer (every agent: state, alert, hit points, route) and the
    /// Captain's are built in. Other owners add theirs from their own file with
    /// <see cref="Register"/> (see <see cref="IAgentOverlayLayer"/>), so the overlay never
    /// needs editing to show a new agent.</para>
    /// <para><b>Cost:</b> nothing while hidden. It is shown only in the Editor and development
    /// builds, so it can never appear in the submitted build.</para>
    /// </remarks>
    public sealed class AgentDebugOverlay : MonoBehaviour
    {
        static readonly List<IAgentOverlayLayer> Registered = new List<IAgentOverlayLayer>();

        [Tooltip("Show the overlay as soon as the scene starts (F3 toggles it either way).")]
        [SerializeField] bool showOnStart;

        [Tooltip("Labels further than this from the camera are hidden, so distant agents do not pile up on the horizon. Lines are always drawn.")]
        [SerializeField, Min(1f)] float maxLabelDistance = 25f;

        readonly OverlayCanvas _canvas = new OverlayCanvas();
        readonly List<IAgentOverlayLayer> _layers = new List<IAgentOverlayLayer>();
        Material _lineMaterial;
        GUIStyle _labelStyle;
        GUIStyle _headerStyle;

        /// <summary>True while the overlay is drawn.</summary>
        public bool Shown { get; set; }

        /// <summary>What was drawn this frame, for tests.</summary>
        public OverlayCanvas Canvas => _canvas;

        /// <summary>The active layers: the built-in ones, then every registered one.</summary>
        public IReadOnlyList<IAgentOverlayLayer> Layers => _layers;

        /// <summary>Adds a layer to every overlay. Registering the same layer type twice is ignored.</summary>
        public static void Register(IAgentOverlayLayer layer)
        {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));
            foreach (IAgentOverlayLayer existing in Registered)
                if (existing.GetType() == layer.GetType())
                    return;
            Registered.Add(layer);
        }

        // Domain reload is off, so registrations from the last play session must not linger.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearRegistrations() => Registered.Clear();

        static bool Allowed => Application.isEditor || Debug.isDebugBuild;

        void Awake()
        {
            Shown = showOnStart && Allowed;
        }

        void OnEnable() => RenderPipelineManager.endCameraRendering += DrawLines;

        void OnDisable() => RenderPipelineManager.endCameraRendering -= DrawLines;

        void OnDestroy()
        {
            if (_lineMaterial != null)
                Destroy(_lineMaterial);
        }

        void Update()
        {
            if (Allowed && Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
                Shown = !Shown;
        }

        void LateUpdate()
        {
            _canvas.Clear();
            if (!Shown)
                return;
            AgentSpawner spawner = AgentSpawner.Instance;
            DrawFrame(spawner != null ? spawner.SpawnedAgents : null, GridManager.Current);
        }

        /// <summary>
        /// Draws one frame of the overlay for <paramref name="agents"/>: every layer, for every
        /// active agent it handles. Called every frame while shown; public so tests can drive it.
        /// </summary>
        public void DrawFrame(IReadOnlyList<AgentController> agents, GridGraph grid)
        {
            _canvas.Clear();
            _layers.Clear();
            _layers.Add(BuiltIn.Agents);
            _layers.Add(BuiltIn.Captain);
            _layers.AddRange(Registered);
            _canvas.Grid = grid;
            if (agents == null)
                return;

            foreach (AgentController agent in agents)
            {
                if (agent == null || !agent.isActiveAndEnabled)
                    continue;
                foreach (IAgentOverlayLayer layer in _layers)
                    if (layer.Handles(agent))
                        layer.Draw(agent, _canvas);
            }
        }

        static class BuiltIn
        {
            public static readonly IAgentOverlayLayer Agents = new AgentOverlayLayer();
            public static readonly IAgentOverlayLayer Captain = new CaptainOverlayLayer();
        }

        // Lines after each game camera has rendered, through walls (no depth test).
        void DrawLines(ScriptableRenderContext context, Camera camera)
        {
            if (!Shown || _canvas.Lines.Count == 0 || camera.cameraType != CameraType.Game)
                return;

            if (_lineMaterial == null)
            {
                _lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                _lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
                _lineMaterial.SetInt("_ZWrite", 0);
                _lineMaterial.SetInt("_Cull", (int)CullMode.Off);
            }

            GL.PushMatrix();
            GL.LoadProjectionMatrix(camera.projectionMatrix);
            GL.modelview = camera.worldToCameraMatrix;
            _lineMaterial.SetPass(0);
            GL.Begin(GL.LINES);
            foreach (OverlayCanvas.Line line in _canvas.Lines)
            {
                GL.Color(line.Colour);
                GL.Vertex(line.From);
                GL.Vertex(line.To);
            }
            GL.End();
            GL.PopMatrix();
        }

        void OnGUI()
        {
            if (!Shown)
                return;
            Camera camera = Camera.main;

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 13, fontStyle = FontStyle.Bold };
                _headerStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = false };
            }

            var names = new List<string>(_layers.Count);
            foreach (IAgentOverlayLayer layer in _layers)
                names.Add(layer.Name);
            GUI.Box(new Rect(10, 10, 420, 24), "Debug overlay (F3)   layers: " + string.Join(", ", names), _headerStyle);

            if (camera == null)
                return;
            foreach (OverlayCanvas.Label label in _canvas.Labels)
            {
                Vector3 screen = camera.WorldToScreenPoint(label.Position);
                if (screen.z <= 0f || screen.z > maxLabelDistance)
                    continue;   // behind the camera, or too far away to read
                var rect = new Rect(screen.x - 150f, Screen.height - screen.y - 40f, 300f, 40f);

                // A one-pixel shadow keeps the text readable on any background.
                _labelStyle.normal.textColor = Color.black;
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label.Text, _labelStyle);
                _labelStyle.normal.textColor = label.Colour;
                GUI.Label(rect, label.Text, _labelStyle);
            }
        }
    }
}
