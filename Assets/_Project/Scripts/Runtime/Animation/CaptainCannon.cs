using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// The Captain's arm cannon comes alive when it shoots. Through the aim its power cell
    /// charges, glowing brighter and flickering faster (the telegraph the player reads), and
    /// the barrel creeps out. The shot leaves with a flash and a burst of sparks from the
    /// muzzle, the barrel slams back along the forearm and springs out again, and the spent
    /// cell goes dim and recharges.
    /// </summary>
    /// <remarks>
    /// <para>Nothing else moves the barrel or lights the cell. The barrel is placed from its
    /// rest position every frame, after the aim and the kick have turned the arm, so nothing
    /// builds up. The slam is a fast ease-out, then a damped spring: it overshoots a little
    /// past rest and settles, like a heavy part on a spring.</para>
    /// <para>The cell has its own copy of the agents' light material and goes out with the
    /// visor: its glow is multiplied by <see cref="AgentLights.Level"/>, so it is dark while
    /// the Captain is dormant or knocked out and flickers back on with it.</para>
    /// </remarks>
    [RequireComponent(typeof(AgentWeapon))]
    [DefaultExecutionOrder(50)]   // after the aim (−50) and the kick have posed the arm
    public sealed class CaptainCannon : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly Color SparkColour = new Color(1f, 0.75f, 0.3f);
        static readonly Color SmokeColour = new Color(0.35f, 0.33f, 0.32f);

        [Tooltip("Barrel_Pivot: slides back along the forearm when it fires.")]
        [SerializeField] Transform barrel;

        [Tooltip("Cannon_Cell: the power cell that charges and glows.")]
        [SerializeField] Renderer cell;

        [Tooltip("Muzzle_Point: where the flash and the sparks come from.")]
        [SerializeField] Transform muzzle;

        [Tooltip("Emissive material for the cell (AgentLight); copied once.")]
        [SerializeField] Material lightMaterial;

        [Tooltip("Additive particle material for the flash and sparks (AgentSpark).")]
        [SerializeField] Material sparkMaterial;

        [Tooltip("The cell's colour: the Captain's red, like its visor.")]
        [SerializeField] Color colour = new Color(1f, 0.16f, 0.24f);

        [Tooltip("Emission strength of the cell at rest.")]
        [SerializeField, Min(0f)] float intensity = 2.5f;

        [Header("Charge")]
        [Tooltip("Seconds to charge fully: the weapon's aim time.")]
        [SerializeField, Min(0.05f)] float chargeSeconds = 0.3f;

        [Tooltip("The cell's glow at full charge, times its resting glow.")]
        [SerializeField, Min(1f)] float chargedGlow = 4f;

        [Tooltip("Metres the barrel creeps out while charging.")]
        [SerializeField, Min(0f)] float chargeOut = 0.03f;

        [Header("Shot")]
        [Tooltip("Metres the barrel slams back along the forearm.")]
        [SerializeField, Min(0f)] float slideBack = 0.14f;

        [Tooltip("Seconds until the barrel has sprung back out and settled.")]
        [SerializeField, Min(0.1f)] float returnSeconds = 0.4f;

        [Tooltip("Seconds the spent cell takes to recharge.")]
        [SerializeField, Min(0.05f)] float rechargeSeconds = 0.6f;

        [Tooltip("Seconds the muzzle light flashes.")]
        [SerializeField, Min(0.01f)] float flashSeconds = 0.08f;

        const float SlamSeconds = 0.04f;

        AgentWeapon _weapon;
        AgentController _agent;
        AgentLights _lights;
        Material _cellMaterial;
        ParticleSystem _particles;
        Light _flash;
        Vector3 _barrelRest;
        Vector3 _back;              // along the forearm towards the elbow, in the barrel's parent space
        float _aimTime;
        float _shotTime = float.PositiveInfinity;

        /// <summary>How charged the cell is, 0 to 1 (rises through the aim).</summary>
        public float Charge { get; private set; }

        /// <summary>Metres the barrel is back from rest now (negative: pushed out).</summary>
        public float BarrelOffset { get; private set; }

        /// <summary>The cell's glow now, times its resting glow (before the lights' level).</summary>
        public float CellGlow { get; private set; } = 1f;

        /// <summary>Muzzle flashes so far.</summary>
        public int Flashes { get; private set; }

        void Awake()
        {
            _weapon = GetComponent<AgentWeapon>();
            _agent = GetComponent<AgentController>();
            _lights = GetComponent<AgentLights>();
            if (barrel != null)
            {
                _barrelRest = barrel.localPosition;
                if (muzzle != null && barrel.parent != null)
                    _back = barrel.parent.InverseTransformDirection(barrel.position - muzzle.position).normalized;
            }
            if (cell != null && lightMaterial != null)
            {
                _cellMaterial = new Material(lightMaterial) { name = lightMaterial.name + " (cannon cell)" };
                cell.sharedMaterial = _cellMaterial;
            }
            _particles = CreateParticles();
            _flash = CreateFlash();
        }

        void OnEnable() => _weapon.Fired += OnFired;

        void OnDisable()
        {
            _weapon.Fired -= OnFired;
            _shotTime = float.PositiveInfinity;
            if (_flash != null)
                _flash.enabled = false;
        }

        void OnDestroy()
        {
            if (_cellMaterial != null)
                Destroy(_cellMaterial);
        }

        void OnFired(int barrelIndex)
        {
            _shotTime = 0f;
            Flashes++;
            if (muzzle == null || _particles == null)
                return;
            Vector3 at = muzzle.position;
            Vector3 forward = barrel != null ? (at - barrel.position).normalized : transform.forward;
            Burst(at, forward, 1, 0f, 0f, Color.white, 0.9f, 0.9f, 0.07f);          // the flash
            Burst(at, forward, 14, 7f, 0.5f, SparkColour, 0.05f, 0.12f, 0.25f);     // sparks, in a cone
            Burst(at, forward, 6, 1.2f, 0.9f, SmokeColour, 0.25f, 0.45f, 0.6f);     // a puff of smoke
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool down = _agent != null && (_agent.IsDisabled || _agent.IsDead);
            bool aiming = _weapon.IsAiming && !down;
            _aimTime = aiming ? _aimTime + dt : 0f;
            Charge = aiming ? Mathf.Clamp01(_aimTime / chargeSeconds) : 0f;
            if (!float.IsPositiveInfinity(_shotTime))
                _shotTime += dt;

            UpdateBarrel();
            UpdateCell();
            if (_flash != null)
                _flash.enabled = _shotTime < flashSeconds;
        }

        void UpdateBarrel()
        {
            float offset;
            if (_shotTime < returnSeconds)
            {
                // Slams back fast, then springs out: past rest a little, and settles.
                float t = _shotTime;
                if (t < SlamSeconds)
                {
                    float u = 1f - t / SlamSeconds;
                    offset = slideBack * (1f - u * u);
                }
                else
                {
                    float s = (t - SlamSeconds) / (returnSeconds - SlamSeconds);
                    offset = slideBack * Mathf.Exp(-4f * s) * Mathf.Cos(s * 1.4f * Mathf.PI) * (1f - s);
                }
            }
            else
            {
                // Creeps out as it charges, with a hum (a few millimetres of tremble).
                float c = Charge * Charge * (3f - 2f * Charge);
                offset = -chargeOut * c + (Charge > 0f ? 0.003f * c * Mathf.Sin(_aimTime * 90f) : 0f);
            }
            BarrelOffset = offset;
            if (barrel != null)
                barrel.localPosition = _barrelRest + _back * offset;
        }

        void UpdateCell()
        {
            float glow;
            if (_shotTime < rechargeSeconds)
            {
                // Spent: dim, then filling back up.
                float t = _shotTime / rechargeSeconds;
                glow = Mathf.Lerp(0.15f, 1f, t * t * (3f - 2f * t));
            }
            else
            {
                // Charging: brighter and brighter, flickering faster as it fills.
                float c = Charge;
                glow = 1f + (chargedGlow - 1f) * c * c;
                if (c > 0f)
                    glow *= 1f + 0.2f * c * Mathf.Sin(_aimTime * (25f + 60f * c));
            }
            CellGlow = glow;
            if (_cellMaterial == null)
                return;

            float level = _lights != null ? _lights.Level : 1f;
            Color tint = Color.Lerp(colour * 0.25f, colour, Mathf.Clamp01(level * glow));
            tint.a = 1f;
            Color emission = colour * (intensity * glow * level);
            emission.a = 1f;
            _cellMaterial.SetColor(BaseColorId, tint);
            _cellMaterial.SetColor(EmissionColorId, emission);
        }

        // Particles in a cone along the barrel: spread 0 is a straight line, 1 a hemisphere.
        void Burst(Vector3 at, Vector3 forward, int count, float speed, float spread, Color tint, float minSize,
            float maxSize, float life)
        {
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = (forward + Random.insideUnitSphere * spread).normalized;
                emit.position = at;
                emit.velocity = direction * (speed * Random.Range(0.6f, 1.2f));
                emit.startSize = Random.Range(minSize, maxSize);
                emit.startLifetime = life * Random.Range(0.7f, 1.2f);
                emit.startColor = tint;
                _particles.Emit(emit, 1);
            }
        }

        ParticleSystem CreateParticles()
        {
            var holder = new GameObject("CannonEffects");
            holder.transform.SetParent(transform, false);
            var system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.gravityModifier = -0.05f;   // smoke drifts up; sparks are too quick to care
            main.startSpeed = 0f;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ParticleSystem.SizeOverLifetimeModule grow = system.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.2f));

            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (sparkMaterial != null)
                renderer.sharedMaterial = sparkMaterial;
            system.Play();
            return system;
        }

        // A short warm point light at the muzzle, so the shot lights up the Captain and the room.
        Light CreateFlash()
        {
            if (muzzle == null)
                return null;
            var holder = new GameObject("MuzzleFlash");
            holder.transform.SetParent(muzzle, false);
            var flash = holder.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.6f, 0.35f);
            flash.intensity = 4f;
            flash.range = 4f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
            return flash;
        }
    }
}
