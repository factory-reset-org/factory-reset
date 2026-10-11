using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// What a hit and a knock-out look like. Each hit throws a few sparks and squashes the
    /// body for a moment. When the agent goes down: a small explosion, one comic word above
    /// it ("KRZZT!"), it slumps like a powered-down toy (its <see cref="AgentDownPose"/>) and
    /// three dizzy stars circle above its head; when it reboots it gets back up. A scrapped
    /// agent (a Saboteur) goes down the same way with "SCRAPPED!" and no stars, lies there
    /// for a moment, then sinks into the floor and is switched off.
    /// </summary>
    /// <remarks>
    /// <para><b>Kept small on purpose:</b> the prototype put a comic word on every hit, which
    /// cluttered fights. Here words only mark the two moments that matter, going down and
    /// being scrapped; ordinary hits get sparks only. <see cref="AgentLights"/> dims the eyes
    /// at the same time.</para>
    /// <para><b>The pose:</b> going down pauses the Animator so its pose holds, and the body's
    /// <see cref="AgentDownPose"/> slumps it (the Captain kneels, <see cref="AgentKneel"/>)
    /// and gets it back up at the reboot. The slumps replaced a tip-over onto the side, which
    /// read as dead rather than switched off and pushed the body through walls; a body
    /// without a pose still tips over.</para>
    /// <para><b>Dizzy stars:</b> three comic stars circle above the head while the agent is
    /// knocked out (from just after the explosion until the reboot), never while it is
    /// scrapped, dormant or powered down. Built like the comic word: sprites that face the
    /// camera.</para>
    /// <para><b>Particles:</b> one world-space system per agent, emitting only on demand, so
    /// sparks stay where they were thrown when the body moves or tips.</para>
    /// <para><b>Cutscene states.</b> The Captain kneels, switched off, while it is dormant
    /// (<c>CaptainAwake</c> not yet set); when the Chapter 3 cutscene wakes it, its visor
    /// flickers on first and then it steps up. When the factory shuts down in the ending
    /// (<see cref="CutsceneSignals.FactoryShutdown"/>) every agent powers down for good, one
    /// after another in step with its lights: the Animator winds down to a stop and the body
    /// sinks slowly into its pose (the Captain onto its knee), or sags if it has none.</para>
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class AgentKnockdown : MonoBehaviour
    {
        [Tooltip("The model (the nested S3 prefab): squashed by hits, sunk when scrapped, tipped over if there is no pose.")]
        [SerializeField] Transform model;

        [Tooltip("Additive particle material for the sparks and the explosion.")]
        [SerializeField] Material sparkMaterial;

        [Tooltip("Comic word shown when the agent is knocked out.")]
        [SerializeField] Sprite knockOutWord;

        [Tooltip("Comic word shown when the agent is scrapped for good.")]
        [SerializeField] Sprite scrapWord;

        [Header("Dizzy stars")]
        [Tooltip("Sprite of one star. None: no stars.")]
        [SerializeField] Sprite starSprite;

        [Tooltip("What the stars circle above (the head). None: the top of the capsule.")]
        [SerializeField] Transform starsAnchor;

        [Tooltip("Centre of the ring in the anchor's space (m), so it follows the slump. The ring itself stays level.")]
        [SerializeField] Vector3 starsOffset = new Vector3(0f, 0.5f, 0f);

        [Tooltip("Radius of the ring, in metres.")]
        [SerializeField, Min(0.05f)] float starsRadius = 0.3f;

        [Tooltip("Width of one star, in metres.")]
        [SerializeField, Min(0.02f)] float starSize = 0.2f;

        [Tooltip("Seconds for one turn of the ring.")]
        [SerializeField, Min(0.1f)] float starsPeriod = 1.1f;

        [Tooltip("Where smoke puffs from as it goes down (the Guard's head). None: no puff.")]
        [SerializeField] Transform smokeFrom;

        [Header("Fallback without a pose")]
        [Tooltip("Degrees the body tips over.")]
        [SerializeField, Range(0f, 90f)] float tipAngle = 78f;

        [Tooltip("Seconds to tip over (with a small bounce at the end).")]
        [SerializeField, Min(0.05f)] float tipSeconds = 0.4f;

        [Tooltip("Seconds to get back up at the reboot.")]
        [SerializeField, Min(0.05f)] float standSeconds = 0.5f;

        [Header("Words and scrapping")]
        [Tooltip("Scrapped agents: seconds they lie there before sinking away.")]
        [SerializeField, Min(0f)] float scrapLingerSeconds = 1.2f;

        [Tooltip("Scrapped agents: seconds to sink into the floor.")]
        [SerializeField, Min(0.05f)] float scrapSinkSeconds = 0.7f;

        [Tooltip("Width of the comic word, in metres.")]
        [SerializeField, Min(0.1f)] float wordWidth = 1.1f;

        [Tooltip("Seconds the comic word stays up.")]
        [SerializeField, Min(0.1f)] float wordSeconds = 1f;

        [Header("Cutscenes")]
        [Tooltip("The Captain: kneel, switched off, until the blackboard says it is awake (needs an AgentKneel).")]
        [SerializeField] bool dormantUntilCaptainWakes;

        [Tooltip("Seconds between the wake and the stand-up: the visor flickers on first.")]
        [SerializeField, Min(0f)] float wakeStandDelay = 0.7f;

        [Tooltip("Seconds the power-down takes in the ending: the Animator winds down and the body sinks into its pose.")]
        [SerializeField, Min(0.05f)] float powerDownSeconds = 1.2f;

        [Tooltip("Degrees the body sags forward when it powers down (bodies without a pose).")]
        [SerializeField, Range(0f, 20f)] float sagAngle = 5f;

        static readonly Color SparkColour = new Color(1f, 0.85f, 0.35f);
        static readonly Color FireColour = new Color(1f, 0.55f, 0.15f);
        static readonly Color SmokeColour = new Color(0.45f, 0.42f, 0.5f, 0.8f);

        enum Phase { Up, Down, StandingUp, Scrapped, Dormant, Waking, PoweredDown }

        AgentController _agent;
        AgentDownPose _pose;
        Animator _animator;
        ParticleSystem _particles;
        ParticleSystem _smoke;
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
        bool _started;
        bool _alreadyKneeling;   // powered down from the dormant kneel
        float _shutdownAt = float.PositiveInfinity;
        Transform _stars;
        Transform[] _starSprites;
        float _starsAmount;      // 0 hidden, 1 fully shown
        float _starsTime;
        bool _smoked;

        /// <summary>True while the body is down or getting back up.</summary>
        public bool IsDown => _phase != Phase.Up;

        /// <summary>True while the Captain kneels dormant, or is between its wake and its stand-up.</summary>
        public bool IsDormant => _phase == Phase.Dormant || _phase == Phase.Waking;

        /// <summary>True once the body has powered down in the ending. Never false again.</summary>
        public bool IsPoweredDown => _phase == Phase.PoweredDown;

        /// <summary>How far over the body is: 0 upright, 1 on its side.</summary>
        public float Tip => _tip;

        /// <summary>Particles emitted so far, for tests.</summary>
        public int ParticlesEmitted { get; private set; }

        /// <summary>True while a comic word is showing.</summary>
        public bool WordShowing => _word != null && _word.gameObject.activeSelf;

        /// <summary>True while the dizzy stars are showing.</summary>
        public bool StarsShowing => _stars != null && _stars.gameObject.activeSelf;

        /// <summary>Where the ring of stars is centred.</summary>
        public Vector3 StarsCentre => starsAnchor != null
            ? starsAnchor.TransformPoint(starsOffset)
            : transform.position + Vector3.up * (_height + 0.2f);

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            _pose = GetComponent<AgentDownPose>();
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
            if (smokeFrom != null)
                _smoke = CreateParticles("HeadSmoke", -0.04f);   // drifts up
            CreateWord();
            CreateStars();
        }

        void OnEnable()
        {
            _agent.Hit += OnHit;
            CutsceneEvents.OnCriticalSignal += HandleSignal;
        }

        void OnDisable()
        {
            _agent.Hit -= OnHit;
            CutsceneEvents.OnCriticalSignal -= HandleSignal;
        }

        // Powers down in step with the lights, which use the same delay.
        void HandleSignal(string signalId)
        {
            if (signalId == CutsceneSignals.FactoryShutdown && float.IsPositiveInfinity(_shutdownAt))
                _shutdownAt = Time.time + AgentLights.ShutdownDelay(_agent);
        }

        // The Captain before its wake. Before the agent has a blackboard it counts as awake.
        bool Dormant() =>
            dormantUntilCaptainWakes && _pose != null && _agent.World != null && !_agent.World.CaptainAwake;

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
            if (!_started)
            {
                // A dormant Captain is already kneeling on its first frame, with no drop.
                _started = true;
                if (Dormant())
                    Enter(Phase.Dormant, 0f);
            }

            bool shutDown = _shutdownAt <= Time.time;
            switch (_phase)
            {
                case Phase.Up:
                    if (_agent.IsDead)
                        GoDown(Phase.Scrapped, scrapWord);
                    else if (_agent.IsDisabled)
                        GoDown(Phase.Down, knockOutWord);
                    else if (shutDown)
                        Enter(Phase.PoweredDown, -1f);
                    break;
                case Phase.Down:
                    // A body that is down when the factory shuts down simply stays down.
                    if (_agent.IsDead)
                        _phase = Phase.Scrapped;
                    else if (!_agent.IsDisabled && !shutDown)
                    {
                        if (Dormant())
                            Enter(Phase.Dormant, 0f);
                        else
                            Enter(Phase.StandingUp, 1f);
                    }
                    break;
                case Phase.StandingUp:
                    if (_agent.IsDead)
                        GoDown(Phase.Scrapped, scrapWord);
                    else if (_agent.IsDisabled)
                        GoDown(Phase.Down, knockOutWord);
                    else if (shutDown)
                        Enter(Phase.PoweredDown, -1f);
                    else if (_phaseTime >= StandSeconds)
                        _phase = Phase.Up;
                    break;
                case Phase.Dormant:
                case Phase.Waking:
                    if (_agent.IsDisabled)
                        GoDown(Phase.Down, knockOutWord);
                    else if (shutDown)
                    {
                        _alreadyKneeling = true;   // it simply stays on its knee
                        Enter(Phase.PoweredDown, 0f);
                    }
                    else if (_phase == Phase.Dormant && !Dormant())
                        Enter(Phase.Waking, 0f);   // the visor flickers on first
                    else if (_phase == Phase.Waking && _phaseTime >= wakeStandDelay)
                        Enter(Phase.StandingUp, 1f);
                    break;
            }

            UpdateTip();
            UpdatePunch(dt);
            UpdateWord(dt);
            UpdateStars(dt);
            UpdateSmoke();
            if (_phase == Phase.Scrapped)
                UpdateScrap();
        }

        void GoDown(Phase next, Sprite word)
        {
            _phase = next;
            _phaseTime = 0f;
            _side = Random.value < 0.5f ? -1f : 1f;
            _sinking = false;
            _smoked = false;
            if (_animator != null)
                _animator.speed = 0f;   // hold the pose while it lies there
            Explode(Chest);
            if (word != null)
                ShowWord(word);
        }

        // A quiet change of phase, without an explosion or a word. animatorSpeed: 1 runs,
        // 0 holds the pose, -1 leaves it to the power-down, which winds it down.
        void Enter(Phase next, float animatorSpeed)
        {
            _phase = next;
            _phaseTime = 0f;
            if (_animator != null && animatorSpeed >= 0f)
                _animator.speed = animatorSpeed;
        }

        // Far enough into a kneel that it is fully down: a dormant Captain shows no drop.
        const float FullyKneeling = 10f;

        float StandSeconds => _pose != null ? _pose.StandSeconds : standSeconds;

        // How far the power-down has got, 0 to 1, eased; the Animator winds down with it.
        float PowerDown()
        {
            float t = Mathf.Clamp01(_phaseTime / powerDownSeconds);
            float s = t * t * (3f - 2f * t);
            if (_animator != null)
                _animator.speed = 1f - s;
            return s;
        }

        // The tip follows the phase: falls with a small overshoot, stands up smoothly.
        void UpdateTip()
        {
            if (_pose != null)
            {
                UpdatePose();
                return;
            }

            switch (_phase)
            {
                case Phase.Up:
                    _tip = 0f;
                    break;
                case Phase.PoweredDown:
                {
                    // Sags forward a little about its feet and settles, as the Animator stops.
                    _tip = 0f;
                    float s = PowerDown();
                    model.localRotation = _restRotation * Quaternion.Euler(sagAngle * s, 0f, 0f);
                    model.localPosition = _restPosition;
                    return;
                }
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

        // The body's own pose: slumps (the Captain kneels) where it stands, and gets back up.
        // Dormant the Captain is already on its knee; powering down in the ending a body sinks
        // into its pose at half speed.
        void UpdatePose()
        {
            _tip = 0f;
            model.localRotation = _restRotation;
            model.localPosition = _restPosition;
            switch (_phase)
            {
                case Phase.Up:
                    if (_pose.Amount != 0f)
                        _pose.Release();
                    break;
                case Phase.StandingUp:
                    _pose.PoseStandUp(_phaseTime);
                    break;
                case Phase.Dormant:
                case Phase.Waking:
                    _pose.PoseDown(FullyKneeling);
                    break;
                case Phase.PoweredDown:
                    PowerDown();
                    _pose.PoseDown(_alreadyKneeling ? FullyKneeling : _phaseTime * 0.5f);
                    break;
                default:
                    _pose.PoseDown(_phaseTime);
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
            float downFor = _pose != null ? _pose.DownSeconds : tipSeconds;
            float sink = Mathf.Clamp01((_phaseTime - downFor - scrapLingerSeconds) / scrapSinkSeconds);
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

        ParticleSystem CreateParticles(string name = "HitEffects", float gravity = 0.6f)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(transform, false);
            var system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = gravity > 0f ? 160 : 40;
            main.gravityModifier = gravity;
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

        // A puff of smoke from the head as the slump lands, slow and drifting up: something
        // inside burnt out. Once per knock-out (and per scrap).
        void UpdateSmoke()
        {
            const float puffAt = 0.3f;
            if (_smoke == null || _smoked || (_phase != Phase.Down && _phase != Phase.Scrapped) || _phaseTime < puffAt)
                return;
            _smoked = true;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            const int count = 10;
            for (int i = 0; i < count; i++)
            {
                Vector2 spread = Random.insideUnitCircle * 0.25f;
                emit.position = smokeFrom.position + Vector3.up * 0.2f;
                emit.velocity = new Vector3(spread.x, Random.Range(0.5f, 0.9f), spread.y);
                emit.startSize = Random.Range(0.22f, 0.4f);
                emit.startLifetime = Random.Range(0.9f, 1.4f);
                emit.startColor = SmokeColour;
                _smoke.Emit(emit, 1);
            }
            ParticlesEmitted += count;
        }

        // ---- Dizzy stars ---------------------------------------------------------------

        void CreateStars()
        {
            if (starSprite == null)
                return;
            var ring = new GameObject("DizzyStars");
            _stars = ring.transform;
            _stars.SetParent(transform, false);
            _starSprites = new Transform[3];
            for (int i = 0; i < _starSprites.Length; i++)
            {
                var star = new GameObject("Star" + i);
                star.transform.SetParent(_stars, false);
                var sprite = star.AddComponent<SpriteRenderer>();
                sprite.sprite = starSprite;
                sprite.sortingOrder = 50;
                _starSprites[i] = star.transform;
            }
            ring.SetActive(false);
        }

        // Shown while knocked out, from just after the explosion; shrink away at the reboot;
        // gone at once in any other phase (a scrap, the dormant kneel, the power-down).
        void UpdateStars(float dt)
        {
            if (_stars == null)
                return;
            const float delay = 0.35f, popSeconds = 0.2f, outSeconds = 0.15f;
            bool show = _phase == Phase.Down && _phaseTime >= delay;
            if (show)
                _starsAmount = Mathf.Min(1f, _starsAmount + dt / popSeconds);
            else if (_phase == Phase.StandingUp)
                _starsAmount = Mathf.Max(0f, _starsAmount - dt / outSeconds);
            else
                _starsAmount = 0f;

            bool active = _starsAmount > 0f;
            if (_stars.gameObject.activeSelf != active)
                _stars.gameObject.SetActive(active);
            if (!active)
            {
                _starsTime = 0f;
                return;
            }

            _starsTime += dt;
            float size = show ? BackOut(_starsAmount) : _starsAmount;
            float width = starSprite.bounds.size.x;
            Vector3 centre = StarsCentre;
            Camera camera = Camera.main;
            float turn = _starsTime / starsPeriod * 2f * Mathf.PI;
            for (int i = 0; i < _starSprites.Length; i++)
            {
                float angle = turn + i * (2f * Mathf.PI / _starSprites.Length);
                Transform star = _starSprites[i];
                star.position = centre + new Vector3(Mathf.Cos(angle) * starsRadius,
                    0.04f * Mathf.Sin(angle * 2f), Mathf.Sin(angle) * starsRadius);
                star.localScale = Vector3.one * (starSize / width * size);
                // Faces the camera and spins a little on itself, like a cartoon star.
                Quaternion facing = camera != null
                    ? Quaternion.LookRotation(star.position - camera.transform.position, camera.transform.up)
                    : Quaternion.identity;
                star.rotation = facing * Quaternion.Euler(0f, 0f, _starsTime * 180f + i * 40f);
            }
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
