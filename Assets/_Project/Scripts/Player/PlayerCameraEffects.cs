using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Player
{
    /// <summary>
    /// The little movements of the player's view that make it feel held by a body: a bob as
    /// they walk or run, and a shake when they are hurt or something blows up close by. The
    /// camera only moves in its own space, a few centimetres, so aim and shots are untouched.
    /// </summary>
    public sealed class PlayerCameraEffects : MonoBehaviour
    {
        // The prototype's numbers: bob 0.05 m, shake decays 1.8 a second, capped at 0.6.
        const float MaxShake = 0.6f;
        const float ShakeDecayPerSecond = 1.8f;
        const float WalkBobRate = 9f;
        const float RunBobRate = 13f;

        static float s_shake;

        [Tooltip("Height of the walking bob, in metres.")]
        [SerializeField, Min(0f)] float bobHeight = 0.05f;

        [Tooltip("Speed at which the bob is at full size, metres per second.")]
        [SerializeField, Min(0.1f)] float fullBobSpeed = 6f;

        [Tooltip("Metres the view jumps sideways and forwards at full shake.")]
        [SerializeField, Min(0f)] float shakeSideways = 0.15f;

        [SerializeField, Min(0f)] float shakeUp = 0.1f;

        Transform _camera;
        Vector3 _rest;
        PlayerHealth _health;
        float _bobPhase;

        /// <summary>
        /// Shakes the view, for something near and violent. Amounts add up and the total is
        /// capped. It is static so a prop can call it without finding the player.
        /// </summary>
        public static void Shake(float amount) => s_shake = Mathf.Min(MaxShake, s_shake + Mathf.Max(0f, amount));

        /// <summary>How much shake is left, 0 to 0.6.</summary>
        public static float ShakeLeft => s_shake;

        // Domain reload is off, so a shake left over from the last session must not carry on.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => s_shake = 0f;

        void Awake()
        {
            Camera camera = GetComponentInChildren<Camera>();
            if (camera != null)
            {
                _camera = camera.transform;
                _rest = _camera.localPosition;
            }

            _health = GetComponent<PlayerHealth>();
            if (_health != null)
                _health.OnDamaged += HandleDamaged;
        }

        void OnDestroy()
        {
            if (_health != null)
                _health.OnDamaged -= HandleDamaged;
        }

        // A hit of 10 shakes a quarter of the way up; the prototype's n / 40.
        void HandleDamaged(float amount, int sourceAgentId) => Shake(amount / 40f);

        void LateUpdate()
        {
            if (_camera == null)
                return;

            float dt = Time.deltaTime;
            s_shake = Mathf.Max(0f, s_shake - dt * ShakeDecayPerSecond);

            // Cutscenes and the title are filmed through this camera: leave it alone there.
            IGameClock clock = GameClock.Current;
            bool playing = clock == null || clock.State == GameState.Playing;
            if (!playing)
            {
                _camera.localPosition = _rest;
                return;
            }

            float speed = 0f;
            IPlayerState player = PlayerState.Current;
            if (player != null)
            {
                Vector3 velocity = player.Velocity;
                speed = Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);
            }

            float weight = Mathf.Clamp01(speed / fullBobSpeed);
            _bobPhase += dt * (speed > 5.5f ? RunBobRate : WalkBobRate) * weight;
            float bob = Mathf.Sin(_bobPhase) * bobHeight * weight;

            Vector3 jitter = new Vector3(
                Random.Range(-1f, 1f) * s_shake * shakeSideways,
                Random.Range(-1f, 1f) * s_shake * shakeUp,
                Random.Range(-1f, 1f) * s_shake * shakeSideways);
            _camera.localPosition = _rest + new Vector3(0f, bob, 0f) + jitter;
        }
    }
}
