using System.Collections.Generic;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Plays the cutscenes' cues: the factory alarm, and comic words ("?!", "ATTEN-HUT!",
    /// "DEFECTIVE!", "SHUTDOWN") that pop up in the scene, face the camera, drift up and fade.
    /// The director hands it each <see cref="CutsceneCueMarker"/> its Timeline passes.
    /// </summary>
    /// <remarks>
    /// <para>The words are pre-rendered sprites in the same style as the agents' knock-out
    /// word, shown on a camera-facing sprite, so no font asset or canvas is needed. They pop
    /// to 115% and settle like the knock-out word, but stay up longer (1.6 s) so they can be
    /// read in a cutscene. Every word still showing is cleared when the cutscene ends, so a
    /// skip never leaves one hanging.</para>
    /// <para>The alarm is generated in code like the voice blips: three rising "whoops"
    /// (520 to 920 Hz, 0.4 s each), a square wave softened with a sine, so no audio files
    /// are needed.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CutsceneCuePlayer : MonoBehaviour
    {
        const int SampleRate = 44100;

        [Tooltip("One sprite per ComicWord, in its order: Alert, Attention, Defective, Shutdown.")]
        [SerializeField] Sprite[] words = new Sprite[4];

        [Tooltip("Plays the alarm. A 2D source is added if left empty.")]
        [SerializeField] AudioSource alarmSource;

        [SerializeField, Range(0f, 1f)] float alarmVolume = 0.45f;

        [Tooltip("Width of a long word (DEFECTIVE!, SHUTDOWN), in metres.")]
        [SerializeField, Min(0.1f)] float wordWidth = 1.8f;

        [Tooltip("Width of the short \"?!\", in metres.")]
        [SerializeField, Min(0.1f)] float shortWordWidth = 0.9f;

        [Tooltip("Seconds a word stays up.")]
        [SerializeField, Min(0.2f)] float wordSeconds = 1.6f;

        sealed class Pop
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector3 Anchor;
            public float Width;
            public float Time = float.PositiveInfinity;
            public bool Active => Transform.gameObject.activeSelf;
        }

        readonly List<Pop> _pops = new List<Pop>();
        CutsceneActor _actor;
        AudioClip _alarm;

        /// <summary>Alarms played so far, for tests.</summary>
        public int AlarmsPlayed { get; private set; }

        /// <summary>The word that popped up last, for tests.</summary>
        public ComicWord? LastWord { get; private set; }

        /// <summary>Words on screen now.</summary>
        public int WordsShowing
        {
            get
            {
                int count = 0;
                foreach (Pop pop in _pops)
                    if (pop.Active)
                        count++;
                return count;
            }
        }

        void Awake()
        {
            _actor = GetComponentInChildren<CutsceneActor>(true);
            if (alarmSource == null)
            {
                alarmSource = gameObject.AddComponent<AudioSource>();
                alarmSource.playOnAwake = false;
                alarmSource.spatialBlend = 0f;
            }
            _alarm = BuildAlarm();
        }

        void OnEnable() => CutsceneEvents.OnCutsceneEnded += HandleEnded;

        void OnDisable()
        {
            CutsceneEvents.OnCutsceneEnded -= HandleEnded;
            HideAll();
        }

        void OnDestroy()
        {
            if (_alarm != null)
                Destroy(_alarm);
        }

        void HandleEnded(string cutsceneId) => HideAll();

        /// <summary>Plays the cue a Timeline marker describes.</summary>
        public void Play(CutsceneCueMarker marker) => Play(marker.Kind, marker.Word, marker.OnActor, marker.Point);

        /// <summary>Sounds the alarm, or pops <paramref name="word"/> up at the point (world, or above the stand-in).</summary>
        public void Play(CutsceneCueKind kind, ComicWord word, bool onActor, Vector3 point)
        {
            if (kind == CutsceneCueKind.Alarm)
            {
                AlarmsPlayed++;
                if (alarmSource != null && _alarm != null)
                    alarmSource.PlayOneShot(_alarm, alarmVolume);
                return;
            }

            int index = (int)word;
            Sprite sprite = index >= 0 && index < words.Length ? words[index] : null;
            Vector3 anchor = onActor && _actor != null ? _actor.transform.position + _actor.transform.rotation * point : point;
            Pop pop = FreePop();
            pop.Renderer.sprite = sprite;
            pop.Renderer.color = Color.white;
            pop.Anchor = anchor;
            pop.Width = word == ComicWord.Alert ? shortWordWidth : wordWidth;
            pop.Time = 0f;
            pop.Transform.gameObject.SetActive(true);
            LastWord = word;
            Animate(pop, 0f);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (Pop pop in _pops)
            {
                if (!pop.Active)
                    continue;
                pop.Time += dt;
                if (pop.Time >= wordSeconds)
                    pop.Transform.gameObject.SetActive(false);
                else
                    Animate(pop, pop.Time);
            }
        }

        // Pops in with an overshoot, drifts up, faces the camera and fades out at the end.
        void Animate(Pop pop, float time)
        {
            float t = time / wordSeconds;
            float scale = time < 0.12f ? Mathf.Lerp(0f, 1.15f, time / 0.12f) : Mathf.Lerp(1.15f, 1f, (time - 0.12f) / 0.1f);
            float spriteWidth = pop.Renderer.sprite != null ? pop.Renderer.sprite.bounds.size.x : 1f;
            pop.Transform.localScale = Vector3.one * (pop.Width / spriteWidth * scale);
            pop.Transform.position = pop.Anchor + Vector3.up * (0.35f * t);
            Camera camera = Camera.main;
            if (camera != null)
                pop.Transform.rotation = Quaternion.LookRotation(pop.Transform.position - camera.transform.position, camera.transform.up);
            pop.Renderer.color = new Color(1f, 1f, 1f, t > 0.75f ? 1f - (t - 0.75f) / 0.25f : 1f);
        }

        Pop FreePop()
        {
            foreach (Pop pop in _pops)
                if (!pop.Active)
                    return pop;
            var holder = new GameObject("CutsceneWord");
            holder.transform.SetParent(transform, false);
            var renderer = holder.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 60;
            holder.SetActive(false);
            var created = new Pop { Transform = holder.transform, Renderer = renderer };
            _pops.Add(created);
            return created;
        }

        void HideAll()
        {
            foreach (Pop pop in _pops)
                if (pop.Transform != null)
                    pop.Transform.gameObject.SetActive(false);
        }

        /// <summary>
        /// The factory alarm: three rising whoops (520 to 920 Hz over 0.4 s each), a square wave
        /// softened with a sine so it is urgent without being harsh. Public for tests.
        /// </summary>
        public static AudioClip BuildAlarm()
        {
            const float whoop = 0.4f;
            const int whoops = 3;
            int perWhoop = Mathf.RoundToInt(SampleRate * whoop);
            var data = new float[perWhoop * whoops];
            float phase = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float u = (i % perWhoop) / (float)perWhoop;          // 0 to 1 through this whoop
                float frequency = 520f * Mathf.Pow(920f / 520f, u);    // an even rise in pitch
                phase += frequency / SampleRate;
                float wave = 0.6f * VoiceBlips.Sample(BlipWave.Square, phase) + 0.4f * VoiceBlips.Sample(BlipWave.Sine, phase);
                float envelope = Mathf.Min(1f, (i % perWhoop) / 400f) * (1f - 0.35f * u);   // quick attack, slight fall
                data[i] = wave * envelope * 0.5f;
            }
            AudioClip clip = AudioClip.Create("FactoryAlarm", data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
