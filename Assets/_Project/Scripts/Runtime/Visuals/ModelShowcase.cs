using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ToyFactory.Runtime.Visuals
{
    /// <summary>
    /// The controls of <c>ModelShowcase.unity</c>, S3's presentation scene (never in a build). It shows every
    /// S3 model two ways: the fixed line-up of the characters, and a turntable that spins one model at a time
    /// from the whole list. In both, the wireframe can be switched on, and a model with a
    /// <see cref="LODGroup"/> can be held on any level to compare triangle counts.
    /// </summary>
    /// <remarks>
    /// Nothing here plays an animation clip or reads a game system: the models only turn about the vertical
    /// axis, so the scene works however the clips change. <c>Tab</c> switches view, <c>Left</c> and
    /// <c>Right</c> pick the model, <c>Space</c> starts and stops the spin, <c>W</c> toggles the wireframe,
    /// <c>L</c> cycles the level (automatic, each level, culled) and <c>Up</c> and <c>Down</c> move the
    /// turntable camera, which is how the automatic level is made to change.
    /// </remarks>
    public sealed class ModelShowcase : MonoBehaviour
    {
        /// <summary>The two views.</summary>
        public enum View
        {
            /// <summary>The characters side by side at neutral rotation, seen by the orthographic camera.</summary>
            Lineup,

            /// <summary>One model at a time on the turntable, seen by the perspective camera.</summary>
            Turntable
        }

        /// <summary>One model the turntable can show.</summary>
        [Serializable]
        public struct Entry
        {
            /// <summary>The name shown on screen.</summary>
            public string label;

            /// <summary>The prefab to put on the turntable.</summary>
            public GameObject prefab;
        }

        const float MinZoom = 0.15f;
        const float MaxZoom = 12f;
        const float ZoomStep = 1.35f;

        [Tooltip("Every model the turntable can show: the five characters and the task props.")]
        [SerializeField] Entry[] entries = Array.Empty<Entry>();

        [Tooltip("The models already standing in the scene, left to right. They keep a neutral rotation unless the spin is on.")]
        [SerializeField] Transform[] lineup = Array.Empty<Transform>();

        [SerializeField] Camera lineupCamera;
        [SerializeField] Camera turntableCamera;

        [Tooltip("Where the turntable model stands. Far from the line-up so the two never share a view.")]
        [SerializeField] Transform pedestal;

        [SerializeField, Min(0f)] float spinDegreesPerSecond = 40f;
        [SerializeField] Color wireColour = new Color(0.24f, 0.86f, 0.69f);

        readonly List<WireframeView> _lineupWire = new List<WireframeView>();
        GameObject _current;
        WireframeView _currentWire;
        LODGroup _currentGroup;
        int[] _levelTriangles = Array.Empty<int>();
        int _wholeTriangles;
        float _angle;
        float _zoom = 1f;
        GUIStyle _style;

        /// <summary>The view on screen.</summary>
        public View CurrentView { get; private set; } = View.Lineup;

        /// <summary>The index in the entries of the model on the turntable.</summary>
        public int Index { get; private set; }

        /// <summary>How many models the turntable can show.</summary>
        public int EntryCount => entries.Length;

        /// <summary>True while the wireframe is on.</summary>
        public bool Wireframe { get; private set; }

        /// <summary>True while the models turn.</summary>
        public bool Spin { get; private set; }

        /// <summary>
        /// The level the turntable model is held on: -1 for automatic, 0 and up for a level, and the number of
        /// levels for culled. Always -1 for a model with no <see cref="LODGroup"/>.
        /// </summary>
        public int ForcedLevel { get; private set; } = -1;

        /// <summary>The model on the turntable, or null in the line-up view.</summary>
        public GameObject Current => _current;

        /// <summary>The levels of the turntable model's LOD group. Zero when it has none.</summary>
        public int LevelCount => _currentGroup == null ? 0 : _currentGroup.lodCount;

        /// <summary>The triangles of each level of the turntable model. Empty when it has no LOD group.</summary>
        public IReadOnlyList<int> LevelTriangles => _levelTriangles;

        /// <summary>The triangles the turntable model has when no level is held, or the one level that is.</summary>
        public int CurrentTriangles
        {
            get
            {
                if (_currentGroup == null)
                    return _wholeTriangles;

                int level = ShownLevel();
                return level >= 0 && level < _levelTriangles.Length ? _levelTriangles[level] : 0;
            }
        }

        /// <summary>The level the turntable model is drawing now: -1 when culled, 0 when it has no LOD group.</summary>
        public int ShownLevel()
        {
            if (_currentGroup == null)
                return 0;
            if (ForcedLevel >= 0)
                return ForcedLevel >= _currentGroup.lodCount ? LodMath.Culled : ForcedLevel;

            LOD[] levels = _currentGroup.GetLODs();
            var heights = new float[levels.Length];
            for (int i = 0; i < levels.Length; i++)
                heights[i] = levels[i].screenRelativeTransitionHeight;

            Vector3 centre = _currentGroup.transform.TransformPoint(_currentGroup.localReferencePoint);
            Vector3 scale = _currentGroup.transform.lossyScale;
            float size = _currentGroup.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float height = LodMath.ScreenRelativeHeight(size, Vector3.Distance(turntableCamera.transform.position, centre), turntableCamera.fieldOfView);
            return LodMath.ActiveLevel(height, heights);
        }

        void Start()
        {
            lineupCamera.gameObject.SetActive(true);
            turntableCamera.gameObject.SetActive(false);
            ShowLineup();
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
                Read(keyboard);

            Advance(Time.unscaledDeltaTime);
        }

        void Read(Keyboard keyboard)
        {
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                if (CurrentView == View.Lineup)
                    ShowTurntable(Index);
                else
                    ShowLineup();
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
                ToggleSpin();
            if (keyboard.wKey.wasPressedThisFrame)
                SetWireframe(!Wireframe);
            if (keyboard.lKey.wasPressedThisFrame)
                CycleLevel();

            if (CurrentView == View.Turntable)
            {
                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                    ShowTurntable(Index + 1);
                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                    ShowTurntable(Index - 1);
                if (keyboard.upArrowKey.wasPressedThisFrame)
                    Zoom(1f / ZoomStep);
                if (keyboard.downArrowKey.wasPressedThisFrame)
                    Zoom(ZoomStep);
            }
        }

        /// <summary>Turns whatever is spinning by <paramref name="seconds"/> of presentation time.</summary>
        public void Advance(float seconds)
        {
            if (!Spin)
                return;

            _angle = Mathf.Repeat(_angle + spinDegreesPerSecond * seconds, 360f);
            ApplyRotation();
        }

        /// <summary>Shows the fixed line-up at neutral rotation and stops the spin.</summary>
        public void ShowLineup()
        {
            CurrentView = View.Lineup;
            Spin = false;
            _angle = 0f;
            Release();
            lineupCamera.gameObject.SetActive(true);
            turntableCamera.gameObject.SetActive(false);
            foreach (Transform each in lineup)
                each.gameObject.SetActive(true);
            ApplyRotation();
            ApplyWireframe();
        }

        /// <summary>Shows the model at <paramref name="index"/> (wrapping round the list) turning on the turntable.</summary>
        public void ShowTurntable(int index)
        {
            if (entries.Length == 0)
                return;

            CurrentView = View.Turntable;
            Index = ((index % entries.Length) + entries.Length) % entries.Length;
            Release();

            foreach (Transform each in lineup)
                each.gameObject.SetActive(false);
            lineupCamera.gameObject.SetActive(false);
            turntableCamera.gameObject.SetActive(true);

            _current = Instantiate(entries[Index].prefab, pedestal);
            _current.name = entries[Index].label;
            _current.transform.localRotation = Quaternion.identity;
            _current.transform.localPosition = Vector3.zero;
            Measure(_current);
            Spin = true;
            _angle = 0f;
            _zoom = 1f;
            ForcedLevel = -1;
            FrameCamera();
            ApplyWireframe();
        }

        /// <summary>Starts or stops the spin. Stopping in the line-up puts the models back at neutral rotation.</summary>
        public void ToggleSpin()
        {
            Spin = !Spin;
            if (!Spin && CurrentView == View.Lineup)
            {
                _angle = 0f;
                ApplyRotation();
            }
        }

        /// <summary>Turns the wireframe on or off for everything on show.</summary>
        public void SetWireframe(bool on)
        {
            Wireframe = on;
            ApplyWireframe();
        }

        /// <summary>Moves to the next way of holding the LOD group: automatic, level 0, level 1 and so on, culled.</summary>
        public void CycleLevel()
        {
            if (_currentGroup == null)
                return;

            ForcedLevel = ForcedLevel >= _currentGroup.lodCount ? -1 : ForcedLevel + 1;
            _currentGroup.ForceLOD(ForcedLevel);
        }

        /// <summary>Moves the turntable camera by <paramref name="factor"/> times its distance.</summary>
        public void Zoom(float factor)
        {
            _zoom = Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom);
            FrameCamera();
        }

        void Release()
        {
            WireframeView.Dispose(_current);
            _current = null;
            _currentWire = null;
            _currentGroup = null;
            _levelTriangles = Array.Empty<int>();
            _wholeTriangles = 0;
            ForcedLevel = -1;
        }

        void Measure(GameObject model)
        {
            _currentWire = WireOn(model);
            _currentGroup = model.GetComponentInChildren<LODGroup>();
            _wholeTriangles = CountTriangles(model.GetComponentsInChildren<MeshFilter>(true));
            if (_currentGroup == null)
                return;

            LOD[] levels = _currentGroup.GetLODs();
            _levelTriangles = new int[levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                var filters = new List<MeshFilter>();
                foreach (Renderer renderer in levels[i].renderers)
                {
                    if (renderer != null && renderer.TryGetComponent(out MeshFilter filter))
                        filters.Add(filter);
                }

                _levelTriangles[i] = CountTriangles(filters);
            }
        }

        /// <summary>The triangles in the meshes of <paramref name="filters"/>, leaving out the wireframe's lines.</summary>
        public static int CountTriangles(IEnumerable<MeshFilter> filters)
        {
            int total = 0;
            foreach (MeshFilter filter in filters)
            {
                if (filter == null || filter.sharedMesh == null || WireframeView.IsWire(filter))
                    continue;

                Mesh mesh = filter.sharedMesh;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) == MeshTopology.Triangles)
                        total += (int)mesh.GetIndexCount(sub) / 3;
                }
            }

            return total;
        }

        void ApplyRotation()
        {
            if (_current != null)
                _current.transform.localRotation = Quaternion.Euler(0f, _angle, 0f);

            foreach (Transform each in lineup)
                each.localRotation = Quaternion.Euler(0f, _angle, 0f);
        }

        void ApplyWireframe()
        {
            if (CurrentView == View.Turntable)
            {
                if (_currentWire != null)
                    _currentWire.Set(Wireframe, wireColour);
                return;
            }

            if (_lineupWire.Count == 0)
            {
                foreach (Transform each in lineup)
                    _lineupWire.Add(WireOn(each.gameObject));
            }

            foreach (WireframeView each in _lineupWire)
                each.Set(Wireframe, wireColour);
        }

        static WireframeView WireOn(GameObject model) =>
            model.TryGetComponent(out WireframeView view) ? view : model.AddComponent<WireframeView>();

        // Stands the model on the pedestal and puts the camera in front of it, far enough to take it all in.
        void FrameCamera()
        {
            if (_current == null)
                return;

            Bounds bounds = ModelBounds(_current);
            _current.transform.position += Vector3.up * (pedestal.position.y - bounds.min.y);
            bounds = ModelBounds(_current);

            float fov = turntableCamera.fieldOfView;
            float distance = bounds.extents.magnitude / Mathf.Sin(Mathf.Deg2Rad * fov * 0.5f) * 1.05f * _zoom;
            Vector3 centre = bounds.center;
            turntableCamera.transform.position = new Vector3(centre.x, centre.y, centre.z + distance);
            turntableCamera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        }

        static Bounds ModelBounds(GameObject model)
        {
            bool any = false;
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (WireframeView.IsWire(renderer))
                    continue;

                if (!any)
                    bounds = renderer.bounds;
                else
                    bounds.Encapsulate(renderer.bounds);
                any = true;
            }

            return bounds;
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, normal = { textColor = Color.white } };
            var text = new System.Text.StringBuilder();
            text.AppendLine("<b>MODEL SHOWCASE</b>   Tab: " + (CurrentView == View.Lineup ? "line-up (press for turntable)" : "turntable (press for line-up)"));
            text.AppendLine("W wireframe: " + (Wireframe ? "on" : "off") + "    Space spin: " + (Spin ? "on" : "off"));

            if (CurrentView == View.Turntable && _current != null)
            {
                text.AppendLine("<b>" + entries[Index].label + "</b>  (" + (Index + 1) + " of " + entries.Length + ")    Left and Right: model");
                if (_currentGroup == null)
                {
                    text.AppendLine("Triangles: " + _wholeTriangles.ToString("N0") + "    no LOD group");
                }
                else
                {
                    int shown = ShownLevel();
                    string level = shown < 0 ? "culled" : "LOD" + shown;
                    string held = ForcedLevel < 0 ? "automatic" : "held";
                    text.AppendLine("Triangles: " + CurrentTriangles.ToString("N0") + " (" + level + ", " + held + ")    L: level    Up and Down: distance");
                    for (int i = 0; i < _levelTriangles.Length; i++)
                        text.AppendLine("   LOD" + i + ": " + _levelTriangles[i].ToString("N0") + " triangles");
                }
            }
            else
            {
                foreach (Transform each in lineup)
                    text.AppendLine(each.name + ": " + CountTriangles(each.GetComponentsInChildren<MeshFilter>(true)).ToString("N0") + " triangles");
            }

            GUI.Label(new Rect(16f, 12f, 900f, 400f), text.ToString(), _style);
        }
    }
}
