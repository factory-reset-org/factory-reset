using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Player;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A power core of the last chapter. It is shielded, and takes no damage, until the
    /// chapter 4 cutscene drops the shields; after that a set number of hits destroys it.
    /// It turns, flashes on every hit and, when it goes, blows up in a burst of pink and gold.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PowerCore : TaskProp, IDamageable
    {
        static readonly Color CoreColour = new Color(1f, 0.24f, 0.78f);
        static readonly Color DebrisColour = new Color(1f, 0.79f, 0.2f);
        static readonly Color ShieldColour = new Color(0.38f, 0.85f, 1f);

        [Tooltip("Must equal the task id in the chapter data.")]
        [SerializeField] string taskId = "ch4.core.1";

        [Tooltip("The chapter this task belongs to. Before it, hits do nothing.")]
        [SerializeField, Range(1, ChapterEvents.ChapterCount)] int chapter = 4;

        [SerializeField, Min(1)] int hitsToDestroy = 8;

        [Tooltip("The shield visual, hidden when the shields drop. Optional.")]
        [SerializeField] GameObject shield;

        [SerializeField] float spinDegreesPerSecond = 80f;

        [Tooltip("How long a hit lights the core up.")]
        [SerializeField, Min(0.02f)] float flashSeconds = 0.12f;

        int _hits;
        Renderer _body;
        Renderer _shieldRenderer;
        float _flashUntil;
        bool _flashing;

        public override string Id => taskId;

        public bool IsShielded { get; private set; } = true;

        void Awake()
        {
            _shieldRenderer = shield != null ? shield.GetComponent<Renderer>() : null;
            foreach (Renderer each in GetComponentsInChildren<Renderer>())
            {
                if (each != _shieldRenderer)
                {
                    _body = each;
                    break;
                }
            }
        }

        void Start() => PropGlow.Attach(gameObject, CoreColour, 3f, 0.6f, 0.25f, 4f);

        void OnEnable()
        {
            CutsceneEvents.OnCriticalSignal += HandleSignal;
            ChapterEvents.OnChapterStarted += HandleChapterStarted;
        }

        void OnDisable()
        {
            CutsceneEvents.OnCriticalSignal -= HandleSignal;
            ChapterEvents.OnChapterStarted -= HandleChapterStarted;
        }

        void HandleSignal(string signalId)
        {
            if (signalId == CutsceneSignals.CoreShieldsDown)
                DropShield();
        }

        // The signal is what normally drops the shield. The chapter starting does it too,
        // so a core can never stay shielded in its own chapter.
        void HandleChapterStarted(int started)
        {
            if (started >= chapter)
                DropShield();
        }

        [ContextMenu("Drop Shield")]
        void DropShield()
        {
            if (!IsShielded)
                return;

            IsShielded = false;
            if (shield != null)
                shield.SetActive(false);
        }

        void Update()
        {
            transform.Rotate(0f, spinDegreesPerSecond * (IsCompleted ? 0.15f : 1f) * Time.deltaTime, 0f, Space.World);

            // Only touches the renderer when the flash starts or ends.
            bool flashing = Time.time < _flashUntil;
            if (_body != null && flashing != _flashing)
            {
                if (flashing)
                    PropTint.Set(_body, Color.white);
                else
                    PropTint.Clear(_body);
            }
            _flashing = flashing;

            // The shield breathes, a little see-through field of light.
            if (IsShielded && _shieldRenderer != null)
            {
                Color field = ShieldColour;
                field.a = 0.2f + 0.06f * Mathf.Sin(Time.time * 3f);
                PropTint.Set(_shieldRenderer, field);
            }
        }

        [ContextMenu("Take Hit")]
        public void TakeHit()
        {
            if (IsCompleted || !ChapterIsActive(chapter))
                return;

            Vector3 at = transform.position;
            if (IsShielded)
            {
                PropEffects.Spark(at, ShieldColour);
                if (Random.value < 0.3f)
                    PropEffects.Word("shielded", at + Vector3.up * 1f);
                GameSfx.Play(Sfx.Hit);
                return;
            }

            _hits++;
            _flashUntil = Time.time + flashSeconds;
            PropEffects.Spark(at, CoreColour);
            GameSfx.Play(Sfx.Hit);
            if (_hits < hitsToDestroy)
            {
                ReportProgress(_hits / (float)hitsToDestroy);
                return;
            }

            EmitNoise(NoiseLoudness.CoreExplosion);
            PropEffects.Explosion(at, CoreColour);
            PropEffects.Explosion(at, DebrisColour);
            PropEffects.Word("kaboom", at + Vector3.up * 0.8f, 1.4f);
            GameSfx.Play(Sfx.Boom);
            PlayerCameraEffects.Shake(0.3f);
            Complete();
            gameObject.SetActive(false);
        }
    }
}
