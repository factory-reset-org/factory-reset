using UnityEngine;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// The look of a conveyor belt: a flat surface with chevrons pointing the way it carries,
    /// sliding along while the belt runs, as the prototype's belts do. It is a separate quad
    /// laid on the belt's top, so the belt's own squashed scale cannot deform the chevrons.
    /// </summary>
    public sealed class BeltSurface : MonoBehaviour
    {
        static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");

        // One chevron is this share of the belt's width long.
        const float ChevronLengthPerWidth = 0.75f;

        // Lifts the surface off the belt's top so the two never fight for the pixels.
        const float Lift = 0.012f;

        ConveyorBelt _belt;
        Transform _quad;
        Renderer _renderer;
        MaterialPropertyBlock _block;
        Vector2 _tiling;
        float _chevronMetres;
        float _offset;

        /// <summary>The surface for <paramref name="belt"/>. Null if there is no belt material or no box on the belt.</summary>
        public static BeltSurface Attach(ConveyorBelt belt)
        {
            Material material = PropEffects.BeltMaterial;
            if (belt == null || material == null || !belt.TryGetComponent(out BoxCollider box))
                return null;

            if (!belt.TryGetComponent(out BeltSurface surface))
                surface = belt.gameObject.AddComponent<BeltSurface>();
            surface.Build(belt, box, material);
            return surface;
        }

        void Build(ConveyorBelt belt, BoxCollider box, Material material)
        {
            if (_quad != null)
                return;
            _belt = belt;

            // Along and across the belt, in metres, from its box and the way it carries.
            Vector3 scale = belt.transform.lossyScale;
            bool alongX = Mathf.Abs(belt.LocalDirection.x) >= Mathf.Abs(belt.LocalDirection.z);
            float along = alongX ? box.size.x * Mathf.Abs(scale.x) : box.size.z * Mathf.Abs(scale.z);
            float across = alongX ? box.size.z * Mathf.Abs(scale.z) : box.size.x * Mathf.Abs(scale.x);

            Vector3 direction = belt.Direction;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;

            Vector3 top = belt.transform.TransformPoint(box.center + new Vector3(0f, box.size.y * 0.5f, 0f));
            var quad = new GameObject(belt.name + "_Surface");
            _quad = quad.transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(quad, belt.gameObject.scene);

            // Face up, with the quad's right (the texture's u, the way the chevrons point) along the belt.
            _quad.SetPositionAndRotation(top + Vector3.up * Lift,
                Quaternion.LookRotation(Vector3.down, new Vector3(-direction.z, 0f, direction.x)));
            _quad.localScale = new Vector3(along, across, 1f);

            quad.AddComponent<MeshFilter>().sharedMesh = GlowQuad.Mesh;
            _renderer = quad.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _chevronMetres = Mathf.Max(0.1f, across * ChevronLengthPerWidth);
            _tiling = new Vector2(Mathf.Max(1f, along / _chevronMetres), 1f);
            _block = new MaterialPropertyBlock();
            Apply();
        }

        void Update()
        {
            if (_quad == null || _belt == null || !_belt.IsRunning)
                return;

            // The pattern slides the way the belt carries, at the belt's speed.
            _offset -= _belt.Speed * Time.deltaTime / _chevronMetres;
            _offset -= Mathf.Floor(_offset);
            Apply();
        }

        void Apply()
        {
            _renderer.GetPropertyBlock(_block);
            _block.SetVector(BaseMapSt, new Vector4(_tiling.x, _tiling.y, _offset, 0f));
            _renderer.SetPropertyBlock(_block);
        }

        void OnEnable()
        {
            if (_quad != null)
                _quad.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            if (_quad != null)
                _quad.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_quad != null)
                Destroy(_quad.gameObject);
        }
    }
}
