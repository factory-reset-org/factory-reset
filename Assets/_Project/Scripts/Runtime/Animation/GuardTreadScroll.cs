using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Runs the Guard's treads from how it really moves, by scrolling the tread texture along
    /// each belt. The pattern stays still against the floor, like a real track, so it slides
    /// back along the body at the speed that side of the body moves over the ground. Turning
    /// steers like a tank: the outer tread runs faster than the inner one, and turning on the
    /// spot runs the two in opposite directions.
    /// </summary>
    /// <remarks>
    /// <para>The treads are rigid (no road wheels or belt links), so motion is a texture scroll,
    /// as agreed with S3 (model contract): the tread zone has its own material with a seamless
    /// texture, and its UVs are projected along the belt, U along the tread's Z. Measured on the
    /// model, U grows by exactly 4 per metre on every face, so one tile is 0.25 m of belt and a
    /// side moving <c>v</c> m/s scrolls <c>4 · v</c> tiles a second.</para>
    /// <para>A side's ground speed is the body's speed plus or minus the turn: with the yaw rate
    /// ω (radians per second, positive turning right) and the treads 2·h apart, the left tread
    /// moves at <c>v + ω·h</c> and the right at <c>v − ω·h</c>. The same rule as the wheels
    /// (<see cref="WheelSpinner"/>): distance over the ground, so the pattern never slips.</para>
    /// <para>Each tread gets its own copy of the material once, so the two can run at different
    /// speeds and stay SRP Batcher compatible (a property block would not).</para>
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class GuardTreadScroll : MonoBehaviour
    {
        [SerializeField] Renderer treadLeft;
        [SerializeField] Renderer treadRight;
        [Tooltip("Material slot of the tread pattern on each tread.")]
        [SerializeField, Min(0)] int slot;
        [Tooltip("Texture tiles per metre along the belt (U along the tread's Z): 4 on the Guard, one tile every 0.25 m.")]
        [SerializeField, Min(0.01f)] float tilesPerMetre = 4f;
        [Tooltip("Half the distance between the centres of the two treads (m).")]
        [SerializeField, Min(0f)] float halfTrack = 0.335f;

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        AgentController _agent;
        Material _left, _right;

        /// <summary>The left tread's texture offset along the belt (tiles, 0 to 1).</summary>
        public float LeftOffset { get; private set; }

        /// <summary>The right tread's texture offset along the belt (tiles, 0 to 1).</summary>
        public float RightOffset { get; private set; }

        /// <summary>
        /// How fast one side of a tracked body moves over the ground: its speed plus the turn's
        /// share at that side (<paramref name="turnRate"/> in degrees per second, positive right).
        /// </summary>
        public static float SideSpeed(float speed, float turnRate, float halfTrack, bool left) =>
            speed + (left ? 1f : -1f) * turnRate * Mathf.Deg2Rad * halfTrack;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            _left = Copy(treadLeft);
            _right = Copy(treadRight);
        }

        void OnDestroy()
        {
            if (_left != null)
                Destroy(_left);
            if (_right != null)
                Destroy(_right);
        }

        Material Copy(Renderer tread)
        {
            if (tread == null)
                return null;
            Material[] materials = tread.sharedMaterials;
            if (slot >= materials.Length || materials[slot] == null)
                return null;
            var copy = new Material(materials[slot]) { name = materials[slot].name + " (" + name + ")" };
            materials[slot] = copy;
            tread.sharedMaterials = materials;
            return copy;
        }

        void LateUpdate()
        {
            if (_agent.IsFrozen || _agent.IsDisabled || _agent.IsDead)
                return;
            float dt = Time.deltaTime;
            float speed = _agent.Speed, turn = _agent.TurnRate;
            LeftOffset = Mathf.Repeat(LeftOffset + tilesPerMetre * SideSpeed(speed, turn, halfTrack, true) * dt, 1f);
            RightOffset = Mathf.Repeat(RightOffset + tilesPerMetre * SideSpeed(speed, turn, halfTrack, false) * dt, 1f);
            if (_left != null)
                _left.SetTextureOffset(BaseMap, new Vector2(LeftOffset, 0f));
            if (_right != null)
                _right.SetTextureOffset(BaseMap, new Vector2(RightOffset, 0f));
        }
    }
}
