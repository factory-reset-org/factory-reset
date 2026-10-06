using System;
using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Turns the agent's wheels from how fast it is really moving, so they never look like
    /// they slide. Rolling without slipping: a wheel of radius r moving at speed v turns at
    /// <c>ω = v / r</c> radians per second, so each frame it turns by
    /// <c>v / r · dt</c> (in degrees, times 180/π).
    /// </summary>
    /// <remarks>
    /// Done in code rather than in a clip, because the right spin depends on the live speed:
    /// a clip would only match one speed and visibly slip at every other. Wheel pivots must
    /// not be keyed in the clips, or the Animator would fight this every frame.
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class WheelSpinner : MonoBehaviour
    {
        /// <summary>One wheel: its pivot, its radius, and the local axis it rolls around.</summary>
        [Serializable]
        public struct Wheel
        {
            public Transform pivot;
            [Min(0.01f)] public float radius;
            [Tooltip("Axle direction in the pivot's local space (usually right, (1,0,0)).")]
            public Vector3 localAxis;
        }

        [SerializeField] Wheel[] wheels = new Wheel[0];

        AgentController _agent;

        void Awake() => _agent = GetComponent<AgentController>();

        /// <summary>Degrees a wheel of <paramref name="radius"/> turns while rolling <paramref name="distance"/> metres without slipping.</summary>
        public static float DegreesFor(float distance, float radius) => distance / radius * Mathf.Rad2Deg;

        void LateUpdate()
        {
            if (_agent.IsFrozen || _agent.IsDisabled || _agent.IsDead)
                return;

            float distance = _agent.Speed * Time.deltaTime;
            if (distance <= 0f)
                return;

            for (int i = 0; i < wheels.Length; i++)
            {
                Wheel wheel = wheels[i];
                if (wheel.pivot == null)
                    continue;
                wheel.pivot.Rotate(wheel.localAxis, DegreesFor(distance, wheel.radius), Space.Self);
            }
        }
    }
}
