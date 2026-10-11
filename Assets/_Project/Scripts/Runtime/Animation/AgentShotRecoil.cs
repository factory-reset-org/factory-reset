using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The kick of a shot: when <see cref="AgentWeapon"/> fires, the arm holding that barrel
    /// snaps up and back, the torso jolts back and the head nods with it, then everything
    /// settles over <see cref="duration"/>. The Captain alternates its cannons, so its kick
    /// alternates arms with them. Without it the aim pose stood frozen while shots left the
    /// cannon, which did not read as firing.
    /// </summary>
    /// <remarks>
    /// Added in LateUpdate on top of the pose the Animator has just written (the aim or
    /// locomotion pose), after <see cref="AgentCannonAim"/> has pointed the barrel at the
    /// target. Every Guard and Captain clip keys the arms, torso and head, so the Animator
    /// resets them next frame and nothing builds up. The kick is fastest at the start: it
    /// peaks at 15% of the duration and eases back. The arm's kick is 8°: at 28° the barrel
    /// pointed 47° up while the tracer from that same shot went out level.
    /// </remarks>
    [RequireComponent(typeof(AgentWeapon))]
    public sealed class AgentShotRecoil : MonoBehaviour
    {
        [Tooltip("The arm pivot for each barrel, in the weapon's barrel order.")]
        [SerializeField] Transform[] arms = new Transform[0];

        [SerializeField] Transform torso;
        [SerializeField] Transform head;

        [Tooltip("Seconds from the shot until the pose has settled.")]
        [SerializeField, Min(0.05f)] float duration = 0.28f;

        [Tooltip("Degrees the firing arm kicks up (about its pivot's X axis). Small, so the shot still leaves along the barrel.")]
        [SerializeField] float armKick = 8f;

        [Tooltip("Degrees the torso jolts back.")]
        [SerializeField] float torsoKick = 6f;

        [Tooltip("Degrees the head nods back.")]
        [SerializeField] float headKick = 8f;

        [Tooltip("Shape of the kick over normalised time: 0 at rest, 1 at the peak.")]
        [SerializeField] AnimationCurve shape = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 12f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f));

        AgentWeapon _weapon;
        float _time = -1f;
        int _barrel;

        /// <summary>True while a kick is playing.</summary>
        public bool IsKicking => _time >= 0f;

        /// <summary>The barrel whose arm kicked last.</summary>
        public int LastBarrel => _barrel;

        void Awake() => _weapon = GetComponent<AgentWeapon>();

        void OnEnable() => _weapon.Fired += Kick;

        void OnDisable()
        {
            _weapon.Fired -= Kick;
            _time = -1f;
        }

        void Kick(int barrel)
        {
            _barrel = barrel;
            _time = 0f;
        }

        void LateUpdate()
        {
            if (!IsKicking)
                return;
            _time += Time.deltaTime;
            if (_time >= duration)
            {
                _time = -1f;
                return;
            }

            float k = shape.Evaluate(_time / duration);
            if (_barrel < arms.Length && arms[_barrel] != null)
                arms[_barrel].localRotation *= Quaternion.Euler(-armKick * k, 0f, 0f);
            if (torso != null)
                torso.localRotation *= Quaternion.Euler(-torsoKick * k, 0f, 0f);
            if (head != null)
                head.localRotation *= Quaternion.Euler(-headKick * k, 0f, 0f);
        }
    }
}
