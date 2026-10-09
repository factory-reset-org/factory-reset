using System;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>What Unit 047 does at a cue.</summary>
    public enum Unit047Beat
    {
        /// <summary>Eyes off at once (a cue at the start of a cutscene) or sputtering out.</summary>
        EyesOff,

        /// <summary>Eyes flicker on.</summary>
        EyesOn,

        /// <summary>The head drifts slowly from side to side (the default).</summary>
        LookAround,

        /// <summary>The head turns to the right, as if listening to the radio.</summary>
        LookSide,

        /// <summary>The head looks straight ahead.</summary>
        LookAhead,

        /// <summary>The head tips back to look up.</summary>
        LookUp,
    }

    /// <summary>One beat: in this cutscene, at the start of this shot, do this.</summary>
    [Serializable]
    public struct Unit047Cue
    {
        [Tooltip("Cutscene id, e.g. intro.")]
        public string cutscene;

        [Tooltip("Shot index (0 = the first). -1 = as the cutscene starts.")]
        public int shot;

        public Unit047Beat beat;

        public Unit047Cue(string cutscene, int shot, Unit047Beat beat)
        {
            this.cutscene = cutscene;
            this.shot = shot;
            this.beat = beat;
        }
    }

    /// <summary>
    /// Unit 047's cutscene animation: an idle sway and breathing, the wind-up key turning, the
    /// head looking around, and the eyes switching on and off at story beats (the intro opens
    /// with its eyes off; they flicker on in the close-up when Factory OS checks it).
    /// </summary>
    /// <remarks>
    /// <para>Written in code on the frozen pivots, like the agents' wheels and kneel, because
    /// the beats follow the cutscene's shots, not a clip's clock: a cue fires when the
    /// director reaches a shot's dialogue marker (<see cref="CutsceneDirector.ShotStarted"/>),
    /// so it stays in step however long the player takes to read. No clip animates the model,
    /// so each pivot is set from its rest pose every frame and nothing builds up.</para>
    /// <para>The legs are not under <c>Body_Pivot</c>, so the body sways about the waist
    /// with the feet planted. The head turns with a critically damped spring
    /// (<see cref="Mathf.SmoothDampAngle(float,float,ref float,float)"/>), so it starts and
    /// stops softly whatever the distance.</para>
    /// <para>The eyes get one emissive material made from <see cref="eyeMaterial"/> (the
    /// agents' light material), coloured 047's cyan. They flicker on and sputter out with the
    /// same patterns as the agents' lights.</para>
    /// </remarks>
    [RequireComponent(typeof(CutsceneActor))]
    public sealed class Unit047Motion : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly float[] OffPattern = { 1f, 0.15f, 0.7f, 0.1f, 0f };
        static readonly float[] OnPattern = { 0f, 0.6f, 0f, 0.3f, 1f, 0.5f, 1f };

        [Tooltip("Emissive material for the eyes (AgentLight); copied once.")]
        [SerializeField] Material eyeMaterial;

        [Tooltip("Eye colour: Unit 047's speaker colour.")]
        [SerializeField] Color eyeColour = new Color(0.384f, 0.847f, 1f);   // #62D8FF

        [SerializeField, Min(0f)] float eyeIntensity = 2.5f;

        [Tooltip("Seconds the eyes take to flicker on.")]
        [SerializeField, Min(0.01f)] float eyesOnSeconds = 0.6f;

        [Tooltip("Story beats. Shot -1 fires as the cutscene starts.")]
        [SerializeField] Unit047Cue[] cues =
        {
            new Unit047Cue("intro", -1, Unit047Beat.EyesOff),    // a dead production line
            new Unit047Cue("intro", 4, Unit047Beat.EyesOn),      // the close-up: quality check
            new Unit047Cue("intro", 4, Unit047Beat.LookAhead),
            new Unit047Cue("intro", 5, Unit047Beat.LookSide),    // the radio crackles: Pip
            new Unit047Cue("ending", 1, Unit047Beat.LookUp),     // the camera cranes up: "Not defective."
        };

        [Header("Idle")]
        [Tooltip("Body sway side to side (degrees) and its period (s).")]
        [SerializeField] Vector2 sway = new Vector2(1.5f, 3.2f);

        [Tooltip("Body rock forward and back (degrees) and its period (s).")]
        [SerializeField] Vector2 rock = new Vector2(1f, 4.3f);

        [Tooltip("Breathing bob (m) and its period (s).")]
        [SerializeField] Vector2 bob = new Vector2(0.006f, 1.8f);

        [Tooltip("Degrees per second the wind-up key turns.")]
        [SerializeField] float keySpeed = 180f;

        [Tooltip("Look-around: head yaw (degrees) and its period (s).")]
        [SerializeField] Vector2 lookAround = new Vector2(12f, 7f);

        [Tooltip("Seconds the head takes to settle on a new look.")]
        [SerializeField, Min(0.01f)] float headSmoothing = 0.35f;

        const string BodyPath = "Unit047_Root/Body_Pivot";
        const string HeadPath = BodyPath + "/Head_Pivot";

        CutsceneActor _actor;
        CutsceneDirector _director;
        Transform _body, _head, _key, _armL, _armR;
        Vector3 _bodyRestPosition;
        Quaternion _bodyRest, _headRest, _keyRest, _armLRest, _armRRest;
        Renderer _eyes;
        Material _eyeInstance;
        string _cutscene;
        float _time;
        float _keyAngle;
        Unit047Beat _look = Unit047Beat.LookAround;
        float _yaw, _pitch, _yawSpeed, _pitchSpeed;
        float[] _eyePattern;
        float _eyeTime;
        bool _eyesWanted = true;

        /// <summary>Eye brightness, 0 (off) to 1 (on).</summary>
        public float EyeLevel { get; private set; } = 1f;

        /// <summary>The head's current look.</summary>
        public Unit047Beat Look => _look;

        /// <summary>The head's current yaw and pitch from rest, in degrees (for tests).</summary>
        public Vector2 HeadAngles => new Vector2(_yaw, _pitch);

        void Awake()
        {
            _actor = GetComponent<CutsceneActor>();
            _director = GetComponentInParent<CutsceneDirector>();
            Transform model = _actor.Model != null ? _actor.Model.transform : null;
            if (model == null)
                return;
            _body = Pivot(model, BodyPath, out _bodyRest);
            _head = Pivot(model, HeadPath, out _headRest);
            _key = Pivot(model, BodyPath + "/WindupKey_Pivot", out _keyRest);
            _armL = Pivot(model, BodyPath + "/Arm_L_Pivot", out _armLRest);
            _armR = Pivot(model, BodyPath + "/Arm_R_Pivot", out _armRRest);
            if (_body != null)
                _bodyRestPosition = _body.localPosition;

            Transform eyes = model.Find(HeadPath + "/Eyes");
            _eyes = eyes != null ? eyes.GetComponent<Renderer>() : null;
            if (_eyes != null && eyeMaterial != null)
            {
                _eyeInstance = new Material(eyeMaterial) { name = eyeMaterial.name + " (Unit 047 eyes)" };
                _eyes.sharedMaterial = _eyeInstance;
            }
            SetEyes(1f);
        }

        // The pivots are the frozen model contract, looked up once.
        static Transform Pivot(Transform model, string path, out Quaternion rest)
        {
            Transform pivot = model.Find(path);
            if (pivot == null)
                Debug.LogError($"Unit 047: no pivot at {path}.", model);
            rest = pivot != null ? pivot.localRotation : Quaternion.identity;
            return pivot;
        }

        void OnEnable()
        {
            CutsceneEvents.OnCutsceneStarted += HandleCutsceneStarted;
            if (_director == null)
                _director = CutsceneDirector.Current;
            if (_director != null)
                _director.ShotStarted += HandleShotStarted;
        }

        void OnDisable()
        {
            CutsceneEvents.OnCutsceneStarted -= HandleCutsceneStarted;
            if (_director != null)
                _director.ShotStarted -= HandleShotStarted;
        }

        void OnDestroy()
        {
            if (_eyeInstance != null)
                Destroy(_eyeInstance);
        }

        void HandleCutsceneStarted(string cutsceneId)
        {
            // Each cutscene starts from the default: eyes on, looking around.
            _cutscene = cutsceneId;
            _look = Unit047Beat.LookAround;
            _eyesWanted = true;
            _eyePattern = null;
            SetEyes(1f);
            Apply(cutsceneId, -1, instant: true);
        }

        void HandleShotStarted(string cutsceneId, int shot) => Apply(cutsceneId, shot, instant: false);

        /// <summary>Plays the cues for this cutscene and shot. Public for tests.</summary>
        public void Apply(string cutsceneId, int shot, bool instant)
        {
            foreach (Unit047Cue cue in cues)
                if (cue.shot == shot && cue.cutscene == cutsceneId)
                    Play(cue.beat, instant);
        }

        void Play(Unit047Beat beat, bool instant)
        {
            switch (beat)
            {
                case Unit047Beat.EyesOff:
                case Unit047Beat.EyesOn:
                    bool on = beat == Unit047Beat.EyesOn;
                    if (on == _eyesWanted && _eyePattern == null)
                        return;
                    _eyesWanted = on;
                    if (instant)
                    {
                        _eyePattern = null;
                        SetEyes(on ? 1f : 0f);
                    }
                    else
                    {
                        _eyePattern = on ? OnPattern : OffPattern;
                        _eyeTime = 0f;
                    }
                    break;
                default:
                    _look = beat;
                    break;
            }
        }

        void LateUpdate()
        {
            if (!_actor.IsShowing)
                return;
            float dt = Time.deltaTime;
            _time += dt;
            UpdateEyes(dt);

            const float tau = 2f * Mathf.PI;
            if (_body != null)
            {
                float side = sway.x * Mathf.Sin(tau * _time / sway.y);
                float forward = rock.x * Mathf.Sin(tau * _time / rock.y + 0.7f);
                _body.localRotation = _bodyRest * Quaternion.Euler(forward, 0f, side);
                _body.localPosition = _bodyRestPosition + Vector3.up * (bob.x * Mathf.Sin(tau * _time / bob.y));
            }

            // The arms hang and swing a little against the sway, so they look loose.
            float arm = 3f * Mathf.Sin(tau * _time / sway.y + Mathf.PI);
            if (_armL != null)
                _armL.localRotation = _armLRest * Quaternion.Euler(arm, 0f, 0f);
            if (_armR != null)
                _armR.localRotation = _armRRest * Quaternion.Euler(-arm, 0f, 0f);

            if (_key != null)
            {
                _keyAngle = Mathf.Repeat(_keyAngle + keySpeed * dt, 360f);
                _key.localRotation = _keyRest * Quaternion.Euler(0f, 0f, _keyAngle);
            }

            if (_head != null)
            {
                TargetLook(out float yaw, out float pitch);
                _yaw = Mathf.SmoothDampAngle(_yaw, yaw, ref _yawSpeed, headSmoothing);
                _pitch = Mathf.SmoothDampAngle(_pitch, pitch, ref _pitchSpeed, headSmoothing);
                _head.localRotation = _headRest * Quaternion.Euler(_pitch, _yaw, 0f);
            }
        }

        // Yaw positive turns to the character's right; pitch negative looks up.
        void TargetLook(out float yaw, out float pitch)
        {
            switch (_look)
            {
                case Unit047Beat.LookSide:
                    yaw = 35f;
                    pitch = 4f;
                    break;
                case Unit047Beat.LookAhead:
                    yaw = 0f;
                    pitch = 0f;
                    break;
                case Unit047Beat.LookUp:
                    yaw = 0f;
                    pitch = -18f;
                    break;
                default:
                    yaw = lookAround.x * Mathf.Sin(2f * Mathf.PI * _time / lookAround.y);
                    pitch = 0f;
                    break;
            }
        }

        void UpdateEyes(float dt)
        {
            if (_eyePattern == null)
                return;
            _eyeTime += dt;
            float t = Mathf.Clamp01(_eyeTime / eyesOnSeconds);
            int i = Mathf.Min(_eyePattern.Length - 1, Mathf.FloorToInt(t * _eyePattern.Length));
            SetEyes(_eyePattern[i]);
            if (t >= 1f)
                _eyePattern = null;
        }

        void SetEyes(float level)
        {
            EyeLevel = level;
            if (_eyeInstance == null)
                return;
            // Off, the eyes keep a dark tint of their colour, so they read as unlit lamps.
            Color tint = Color.Lerp(eyeColour * 0.2f, eyeColour, level);
            tint.a = 1f;
            Color glow = eyeColour * (eyeIntensity * level);
            glow.a = 1f;
            _eyeInstance.SetColor(BaseColorId, tint);
            _eyeInstance.SetColor(EmissionColorId, glow);
        }
    }
}
