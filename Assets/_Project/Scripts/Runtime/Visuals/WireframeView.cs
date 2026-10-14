using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Runtime.Visuals
{
    /// <summary>
    /// Draws a model as a wireframe. For every mesh under this object it adds a sibling made of line
    /// segments (<see cref="WireMesh"/>), and while the wireframe is on it hides the solid renderers and
    /// shows the lines. Nothing about the model changes: the lines are extra child objects, they follow
    /// whatever the parent does (animation included), and they join the same <see cref="LODGroup"/> levels
    /// as the solid mesh they outline, so a level switch changes the wireframe too.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WireframeView : MonoBehaviour
    {
        /// <summary>The end of the name of every line object this component adds.</summary>
        public const string WireSuffix = "_Wire";

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly List<(Renderer solid, Renderer wire)> _pairs = new List<(Renderer, Renderer)>();
        Material _material;
        bool _built;

        /// <summary>True while the wireframe is showing.</summary>
        public bool IsOn { get; private set; }

        /// <summary>How many line objects were built. Zero until the wireframe is first turned on.</summary>
        public int WireCount => _pairs.Count;

        /// <summary>Destroys an object from play mode or edit mode, where <c>Destroy</c> is not allowed.</summary>
        public static void Dispose(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        /// <summary>True for a renderer this component added, so counts and bounds can leave it out.</summary>
        public static bool IsWire(Component component) => component != null && component.name.EndsWith(WireSuffix);

        /// <summary>Turns the wireframe on or off, building the lines the first time.</summary>
        /// <param name="on">Show the lines instead of the solid model.</param>
        /// <param name="colour">The colour of the lines.</param>
        public void Set(bool on, Color colour)
        {
            if (on && !_built)
                Build();

            IsOn = on;
            if (_material != null)
            {
                _material.SetColor(BaseColorId, colour);
                _material.SetColor(ColorId, colour);
            }

            foreach ((Renderer solid, Renderer wire) in _pairs)
            {
                if (solid != null)
                    solid.forceRenderingOff = on;
                if (wire != null)
                    wire.enabled = on;
            }
        }

        void OnDestroy()
        {
            Dispose(_material);
        }

        void Build()
        {
            _built = true;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            _material = new Material(shader) { name = "Showcase_Wire" };

            var meshes = new Dictionary<Mesh, Mesh>();
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (IsWire(filter) || !filter.TryGetComponent(out MeshRenderer solid) || filter.sharedMesh == null)
                    continue;

                if (!meshes.TryGetValue(filter.sharedMesh, out Mesh lines))
                {
                    lines = WireMesh.Build(filter.sharedMesh);
                    meshes.Add(filter.sharedMesh, lines);
                }

                if (lines == null)
                    continue;

                var child = new GameObject(filter.name + WireSuffix);
                child.transform.SetParent(filter.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = lines;
                MeshRenderer wire = child.AddComponent<MeshRenderer>();
                wire.sharedMaterial = _material;
                wire.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                wire.receiveShadows = false;
                wire.enabled = false;
                _pairs.Add((solid, wire));
            }

            JoinLodGroups();
        }

        // A line object belongs to the same level as the solid mesh it outlines.
        void JoinLodGroups()
        {
            foreach (LODGroup group in GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] levels = group.GetLODs();
                for (int i = 0; i < levels.Length; i++)
                {
                    var renderers = new List<Renderer>(levels[i].renderers);
                    foreach ((Renderer solid, Renderer wire) in _pairs)
                    {
                        if (System.Array.IndexOf(levels[i].renderers, solid) >= 0)
                            renderers.Add(wire);
                    }

                    levels[i].renderers = renderers.ToArray();
                }

                group.SetLODs(levels);
            }
        }
    }
}
