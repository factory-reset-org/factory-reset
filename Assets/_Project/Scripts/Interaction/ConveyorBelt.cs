using UnityEngine;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A conveyor belt: a kinematic body that carries what stands on it. Rigidbodies are carried
    /// by the "position reset" trick (step the body back by one frame of belt travel, then move
    /// it forward again, so physics sees it moving at belt speed without it going anywhere).
    /// The player reads <see cref="Velocity"/> and is carried by the character controller.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ConveyorBelt : MonoBehaviour
    {
        [Tooltip("Direction the belt carries things, in the belt's own space.")]
        [SerializeField] Vector3 localDirection = Vector3.forward;

        [Tooltip("Metres per second.")]
        [SerializeField, Min(0f)] float speed = 1.5f;

        [Tooltip("Chapter 1's belts start off and the lever starts them.")]
        [SerializeField] bool startRunning;

        [Tooltip("Add a sloped plate along every edge of the belt so a box can be pushed up onto it from the floor.")]
        [SerializeField] bool edgeRamps = true;

        [Tooltip("Slope of those plates, in degrees. Gentler is easier for a box to climb and takes more floor.")]
        [SerializeField, Range(5f, 45f)] float rampDegrees = 12f;

        [Tooltip("How much wider than its picture the belt carries, in metres, split over the two sides. A crate is wider than the belt, and one that overhangs it would rest on the ramp plate under its edge, off the belt, and not be carried.")]
        [SerializeField, Min(0f)] float extraWidth = 0.6f;

        [Tooltip("How far the top of the plates stands above the belt's top, in metres. A resting box sits about a millimetre into what it rests on, so the plates must be that much higher or the box is left behind a step it cannot climb.")]
        [SerializeField, Range(0f, 0.01f)] float rampLift = 0.0015f;

        Rigidbody _rb;
        GameObject _ramps;

        public bool IsRunning { get; private set; }

        /// <summary>The way the belt carries, in the belt's own space.</summary>
        public Vector3 LocalDirection => localDirection;

        /// <summary>The way the belt carries, in the world, running or not.</summary>
        public Vector3 Direction => transform.TransformDirection(localDirection).normalized;

        /// <summary>Metres per second when running.</summary>
        public float Speed => speed;

        /// <summary>The velocity of the belt's surface in the world: zero while it is off.</summary>
        public Vector3 Velocity => IsRunning ? Direction * speed : Vector3.zero;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            IsRunning = startRunning;
        }

        // Its chevrons, when the effects asset is there, and the ramps that let a box climb on.
        void Start()
        {
            BeltSurface.Attach(this);
            if (edgeRamps)
                BuildEdgeRamps();
        }

        void OnDestroy()
        {
            if (_ramps != null)
                Destroy(_ramps);
        }

        // A belt is a slab a few centimetres high with square edges, and a box cannot climb a square
        // edge: it only slides into it, however hard it is shoved. Along every edge, outside it, a thin
        // sloped plate rises from the floor to the belt's top. They are static, in world space, and stay
        // where the belt is: the belt only moves by the position reset trick. They have no friction, so
        // a box that has come up one keeps its speed and one that overhangs the belt is not dragged by
        // the plate under its edge.
        void BuildEdgeRamps()
        {
            Collider surface = GetComponent<Collider>();
            if (surface == null)
                return;

            WidenSurface(surface as BoxCollider);
            Bounds b = surface.bounds;
            float rise = b.max.y + rampLift - b.min.y;
            if (rise < 0.005f)
                return;

            _ramps = new GameObject(name + " edge ramps");
            _ramps.layer = gameObject.layer;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_ramps, gameObject.scene);

            float run = rise / Mathf.Tan(rampDegrees * Mathf.Deg2Rad);
            AddRamp(new Vector3(b.center.x, b.min.y, b.min.z), new Vector3(0f, 0f, -1f), b.size.x, rise, run);
            AddRamp(new Vector3(b.center.x, b.min.y, b.max.z), new Vector3(0f, 0f, 1f), b.size.x, rise, run);
            AddRamp(new Vector3(b.min.x, b.min.y, b.center.z), new Vector3(-1f, 0f, 0f), b.size.z, rise, run);
            AddRamp(new Vector3(b.max.x, b.min.y, b.center.z), new Vector3(1f, 0f, 0f), b.size.z, rise, run);
        }

        // Widens the block across the belt (the axis the belt does not carry along), so a crate wider
        // than the picture still rests on the belt's top and not on the ramp plate under its overhang.
        void WidenSurface(BoxCollider block)
        {
            if (block == null || extraWidth <= 0f)
                return;

            bool carriesAlongZ = Mathf.Abs(localDirection.z) >= Mathf.Abs(localDirection.x);
            Vector3 size = block.size;
            Vector3 scale = transform.lossyScale;
            if (carriesAlongZ)
                size.x += extraWidth / Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            else
                size.z += extraWidth / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            block.size = size;
            Physics.SyncTransforms();
        }

        static PhysicsMaterial s_rampMaterial;

        // No friction, whatever the box is made of (Minimum combine).
        static PhysicsMaterial RampMaterial()
        {
            if (s_rampMaterial == null)
            {
                s_rampMaterial = new PhysicsMaterial("Belt ramp")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }
            return s_rampMaterial;
        }

        // One plate along an edge. edgeFoot is the edge at floor level, outward points away from the
        // belt, length runs along the edge.
        void AddRamp(Vector3 edgeFoot, Vector3 outward, float length, float rise, float run)
        {
            const float Thickness = 0.02f;
            // The plate is carried on down past the floor, so its blunt lower end is buried in it and
            // there is no lip at the foot of the ramp for a box to catch on.
            const float Buried = 0.12f;
            Vector3 top = edgeFoot + Vector3.up * rise;
            Vector3 bottom = edgeFoot + outward * (run + Buried) - Vector3.up * (Buried * (rise / run));
            Vector3 slope = (top - bottom).normalized;
            Quaternion rotation = Quaternion.LookRotation(slope, Vector3.up);
            Vector3 centre = (top + bottom) * 0.5f - rotation * Vector3.up * (Thickness * 0.5f);

            var ramp = new GameObject("Ramp");
            ramp.layer = gameObject.layer;
            ramp.transform.SetParent(_ramps.transform, false);
            ramp.transform.SetPositionAndRotation(centre, rotation);
            var collider = ramp.AddComponent<BoxCollider>();
            collider.size = new Vector3(length, Thickness, (top - bottom).magnitude);
            collider.sharedMaterial = RampMaterial();
        }

        public void SetRunning(bool running) => IsRunning = running;

        void FixedUpdate()
        {
            if (!IsRunning)
                return;

            Vector3 position = _rb.position;
            _rb.position = position - Velocity * Time.fixedDeltaTime;
            _rb.MovePosition(position);
        }
    }
}
