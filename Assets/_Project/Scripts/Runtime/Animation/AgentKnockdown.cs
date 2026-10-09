using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// What a hit and a knock-out look like. Each hit throws a few sparks and squashes the
    /// body for a moment. When the agent goes down: a small explosion, one comic word above
    /// it ("KRZZT!"), and it tips over onto its side and lies still; when it reboots it gets
    /// back up. A scrapped agent (a Saboteur) goes down the same way with "SCRAPPED!", lies
    /// there for a moment, then sinks into the floor and is switched off.
    /// </summary>
    /// <remarks>
    /// <para><b>Kept small on purpose:</b> the prototype put a comic word on every hit, which
    /// cluttered fights. Here words only mark the two moments that matter, going down and
    /// being scrapped; ordinary hits get sparks only. <see cref="AgentLights"/> dims the eyes
    /// at the same time.</para>
    /// <para><b>Tipping over</b> rotates the model about the body's feet and lifts it so its
    /// side rests on the floor, and pauses the Animator so the pose holds. The model root is
    /// not animated by any clip, so nothing fights the tilt. The capsule stays upright: a
    /// downed agent does not move, so it does not matter. A body with an
    /// <see cref="AgentKneel"/> (the Captain) kneels and steps back up instead.</para>
    /// <para><b>Particles:</b> one world-space system per agent, emitting only on demand, so
    /// sparks stay where they were thrown when the body moves or tips.</para>
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class AgentKnockdown : MonoBehaviour
    {
        [Tooltip("The model to tip over (the nested S3 prefab).")]
        [SerializeField] Transform model;

        [Tooltip("Additive particle material for the sparks and the explosion.")]
        [SerializeField] Material sparkMaterial;

        [Tooltip("Comic word shown when the agent is knocked out.")]
        [SerializeField] Sprite knockOutWord;

        [Tooltip("Comic word shown when the agent is scrapped for good.")]
        [SerializeField] Sprite scrapWord;

        [Tooltip("Degrees the body tips over.")]
        [SerializeField, Range(0f, 90f)] float tipAngle = 78f;

        [Tooltip("Seconds to tip over (with a small bounce at the end).")]
        [SerializeField, Min(0.05f)] float tipSeconds = 0.4f;

        [Tooltip("Seconds to get back up at the reboot.")]
        [SerializeField, Min(0.05f)] float standSeconds = 0.5f;

        [Tooltip("Scrapped agents: seconds they lie there before sinking away.")]
        [SerializeField, Min(0f)] float scrapLingerSeconds = 1.2f;

        [Tooltip("Scrapped agents: seconds to sink into the floor.")]
        [SerializeField, Min(0.05f)] float scrapSinkSeconds = 0.7f;

        [Tooltip("Width of the comic word, in metres.")]
        [SerializeField, Min(0.1f)] float wordWidth = 1.1f;

        [Tooltip("Seconds the comic word stays up.")]
        [SerializeField, Min(0.1f)] float wordSeconds = 1f;

        static readonly Color SparkColour = new Color(1f, 0.85f, 0.35f);
        static readonly Color FireColour = new Color(1f, 0.55f, 0.15f);
        static readonly Color SmokeColour = new Color(0.45f, 0.42f, 0.5f, 0.8f);

        enum Phase { Up, Down, StandingUp, Scrapped }

        AgentController _agent;
        AgentKneel _kneel;
        Animator _animator;
        ParticleSystem _particles;
        Transform _word;
        SpriteRenderer _wordRenderer;
        float _wordTime = float.PositiveInfinity;
        Vector3 _restPosition;
        Quaternion _restRotation;
        Vector3 _restScale;
        float _height = 1.5f;
        float _radius = 0.5f;
        Phase _phase;
        float _phaseTime;
        float _tip;         // 0 upright, 1 lying down
        float _side = 1f;   // which way it falls
        float _punchTime = float.PositiveInfinity;
        bool _sinking;

        /// <summary>True while the body is down or getting back up.</summary>
        public bool IsDown => _phase != Phase.Up;

        /// <summary>How far over the body is: 0 upright, 1 on its side.</summary>
        public float Tip => _tip;

        /// <summary>Particles emitted so far, for tests.</summary>
        public int ParticlesEmitted { get; private set; }

        /// <summary>True while a comic word is showing.</summary>
        public bool WordShowing => _word != null && _word.gameObject.activeSelf;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            _kneel = GetComponent<AgentKneel>();
            if (model == null)
                model = transform;
            _animator = model.GetComponentInChildren<Animator>();
            _restPosition = model.localPosition;
            _restRotation = model.localRotation;
            _restScale = model.localScale;
            var capsule = GetComponent<CharacterController>();
            if (capsule != null)
            {
                _height = capsule.height;
                _radius = capsule.radius;
            }
            _particles = CreateParticles();
            CreateWord();
        }

        void OnEnable() => _agent.Hit += OnHit;

        void OnDisable() => _agent.Hit -= OnHit;

        Vector3 Chest => transform.position + Vector3.up * (_height * 0.55f);

        void OnHit()
        {
            Sparks(Chest, 6, 3.5f, SparkColour, 0.06f, 0.14f, 0.35f);
            _punchTime = 0f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _phaseTime += dt;
            switch (_phase)
            {
                case Phase.Up:
                    if (_agent.IsDead)
                        GoDown(Phase.Scrapped, scrapWord);
                    else if (_agent.IsDisabled)
                        GoDown(Phase.Down, knockOutWord);
                    break;
                case Phase.Down:
                    if (_agent.IsDead)
                        _phase = Phase.Scrapped;
                    else if (!_agent.IsDisabled)
                    {
                        _phase = Phase.StandingUp;
                        _phaseTime = 0f;
                        if (_animator != null)
                            _animator.speed = 1f;
                    }
                    break;
                case Phase.StandingUp:
                    if (_agent.IsDead)
                        GoDown(Phase.Scrapped, scrapWord);
                    else if (_agent.IsDisabled)
                        GoDown(Phase.Down, knockOutWord);
                    else if (_phaseTime >= StandSeconds)
                        _phase = Phase.Up;
                    break;
            }

            UpdateTip();
            UpdatePunch(dt);
            UpdateWord(dt);
            if (_phase == Phase.Scrapped)
                UpdateScrap();
        }

        void GoDown(Phase next, Sprite word)
        {
            _phase = next;
            _phaseTime = 0f;
            _side = Random.value < 0.5f ? -1f : 1f;
            _sinking = false;
            if (_animator != null)
                _animator.speed = 0f;   // hold the pose while it lies there
            Explode(Chest);
            if (word != null)
                ShowWord(word);
        }

        float StandSeconds => _kneel != null ? _kneel.StandSeconds : standSeconds;

        // The tip follows the phase: falls with a small overshoot, stands up smoothly.
        void UpdateTip()
        {
            if (_kneel != null)
            {
                UpdateKneel();
                return;
            }

            switch (_phase)
            {
                case Phase.Up:
                    _tip = 0f;
                    break;
                case Phase.StandingUp:
                {
                    float t = Mathf.Clamp01(_phaseTime / standSeconds);
                    _tip = 1f - t * t * (3f - 2f * t);
                    break;
                }
                default:
                {
                    float t = Mathf.Clamp01(_phaseTime / tipSeconds);
                    _tip = BackOut(t);
                    break;
                }
            }

            float angle = tipAngle * _tip;
            model.localRotation = _restRotation * Quaternion.Euler(0f, 0f, _side * angle);
            // Lift by the half-width so the side rests on the floor instead of sinking into it.
            float lift = _radius * 0.7f * Mathf.Sin(Mathf.Clamp01(_tip) * tipAngle * Mathf.Deg2Rad);
            model.localPosition = _restPosition + Vector3.up * lift;
        }

        // The Captain: kneels where it stands instead of tipping, and steps back up.
        void UpdateKneel()
        {
            _tip = 0f;
            model.localRotation = _restRotation;
            model.localPosition = _restPosition;
            switch (_phase)
            {
                case Phase.Up:
                    if (_kneel.Amount != 0f)
                        _kneel.Release();
                    break;
                case Phase.StandingUp:
                    _kneel.PoseStandUp(_phaseTime);
                    break;
                default:
                    _kneel.PoseDown(_phaseTime);
                    break;
            }
        }

        // Ease-out with a small overshoot: lands, rocks past and settles.
        static float BackOut(float t)
        {
            const float s = 1.4f;
            float u = t - 1f;
            return 1f + (s + 1f) * u * u * u + s * u * u;
        }

        // A short squash on each hit: wider and shorter, then back.
        void UpdatePunch(float dt)
        {
            if (_phase == Phase.Scrapped)
                return;
            _punchTime += dt;
            const float punchSeconds = 0.15f;
            float k = _punchTime < punchSeconds ? Mathf.Sin(_punchTime / punchSeconds * Mathf.PI) * 0.1f : 0f;
            model.localScale = Vector3.Scale(_restScale, new Vector3(1f + k, 1f - k, 1f + k));
        }

        void UpdateScrap()
        {
            float sink = Mathf.Clamp01((_phaseTime - tipSeconds - scrapLingerSeconds) / scrapSinkSeconds);
            if (sink <= 0f)
                return;
            if (!_sinking)
            {
                // A last puff as it goes, short enough to finish before the body is switched off.
                _sinking = true;
                Sparks(transform.position + Vector3.up * 0.2f, 8, 1.2f, SmokeColour, 0.3f, 0.5f, scrapSinkSeconds * 0.8f);
            }
            float e = sink * sink;
            model.localPosition += Vector3.down * (_height * 0.6f * e);
            model.localScale = _restScale * (1f - 0.7f * e);
            if (sink >= 1f)
                gameObject.SetActive(false);   // switched off, not destroyed: the spawner still lists it
        }

        // ---- Particles -----------------------------------------------------------------

        ParticleSystem CreateParticles()
        {
            var holder = new GameObject("HitEffects");
            holder.transform.SetParent(transform, false);
            var system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            main.gravityModifier = 0.6f;
            main.startSpeed = 0f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            // Fade and shrink out over each particle's life.
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ParticleSystem.SizeOverLifetimeModule shrink = system.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (sparkMaterial != null)
                renderer.sharedMaterial = sparkMaterial;

            system.Play();
            return system;
        }

        void Sparks(Vector3 at, int count, float speed, Color colour, float minSize, float maxSize, float life)
        {
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) + 0.3f;   // mostly up and out
                emit.position = at;
                emit.velocity = direction.normalized * (speed * Random.Range(0.6f, 1.2f));
                emit.startSize = Random.Range(minSize, maxSize);
                emit.startLifetime = life * Random.Range(0.7f, 1.2f);
                emit.startColor = colour;
                _particles.Emit(emit, 1);
            }
            ParticlesEmitted += count;
        }

        // A small explosion: a bright flash, hot sparks and a puff of grey smoke.
        void Explode(Vector3 at)
        {
            Sparks(at, 1, 0f, Color.white, 1.4f, 1.4f, 0.12f);
            Sparks(at, 18, 5f, FireColour, 0.08f, 0.2f, 0.5f);
            Sparks(at, 10, 1.5f, SmokeColour, 0.35f, 0.6f, 0.9f);
        }

        // ---- Comic word ----------------------------------------------------------------

        void CreateWord()
        {
            var word = new GameObject("ComicWord");
            _word = word.transform;
            _word.SetParent(transform, false);
            _wordRenderer = word.AddComponent<SpriteRenderer>();
            _wordRenderer.sortingOrder = 50;
            word.SetActive(false);
        }

        void ShowWord(Sprite sprite)
        {
            _wordRenderer.sprite = sprite;
            _wordRenderer.color = Color.white;
            _wordTime = 0f;
            _word.gameObject.SetActive(true);
        }

        // Pops in with an overshoot, drifts up, faces the camera and fades out at the end.
        void UpdateWord(float dt)
        {
            if (!_word.gameObject.activeSelf)
                return;
            _wordTime += dt;
            if (_wordTime >= wordSeconds)
            {
                _word.gameObject.SetActive(false);
                return;
            }

            float t = _wordTime / wordSeconds;
            // Pop: grows to 115% in 0.12 s, settles to 100% over the next 0.1 s.
            float pop = _wordTime < 0.12f ? Mathf.Lerp(0f, 1.15f, _wordTime / 0.12f)
                : Mathf.Lerp(1.15f, 1f, (_wordTime - 0.12f) / 0.1f);
            float width = _wordRenderer.sprite != null ? _wordRenderer.sprite.bounds.size.x : 1f;
            _word.localScale = Vector3.one * (wordWidth / width * pop);
            _word.position = transform.position + Vector3.up * (_height + 0.5f + 0.4f * t);

            Camera camera = Camera.main;
            if (camera != null)
                _word.rotation = Quaternion.LookRotation(_word.position - camera.transform.position, camera.transform.up);
            _wordRenderer.color = new Color(1f, 1f, 1f, t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f);
        }
    }
}
