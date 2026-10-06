using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Turns the wind-up key on the Tracker's back to show its energy. The key turns at
    /// <c>wound speed × energy</c>, so it visibly slows as the toy runs down, and spins fast
    /// the other way while the toy rewinds (plan: "Rewind spins it fast in reverse"). The
    /// energy comes from the brain through <see cref="AgentController.WindUp"/>; a brain
    /// without it gets a key that turns at the wound speed.
    /// </summary>
    [RequireComponent(typeof(AgentController))]
    public sealed class WindUpKeySpinner : MonoBehaviour
    {
        [Tooltip("The key's pivot.")]
        [SerializeField] Transform key;

        [Tooltip("Axis the key turns around, in the pivot's local space (the shaft, usually forward).")]
        [SerializeField] Vector3 localAxis = Vector3.forward;

        [Tooltip("Degrees per second when fully wound.")]
        [SerializeField] float woundSpeed = 180f;

        [Tooltip("Degrees per second while rewinding. Negative = the opposite direction.")]
        [SerializeField] float rewindSpeed = -720f;

        AgentController _agent;

        void Awake() => _agent = GetComponent<AgentController>();

        /// <summary>
        /// Degrees per second for the key: <paramref name="woundSpeed"/> scaled by the energy,
        /// <paramref name="rewindSpeed"/> while rewinding, or the wound speed if there is no energy.
        /// </summary>
        public static float RateFor(IWindUpState windUp, float woundSpeed, float rewindSpeed)
        {
            if (windUp == null)
                return woundSpeed;
            return windUp.IsRewinding ? rewindSpeed : woundSpeed * Mathf.Clamp01(windUp.Energy01);
        }

        void LateUpdate()
        {
            if (key == null || _agent.IsFrozen || _agent.IsDisabled || _agent.IsDead)
                return;

            key.Rotate(localAxis, RateFor(_agent.WindUp, woundSpeed, rewindSpeed) * Time.deltaTime, Space.Self);
        }
    }
}
