using System;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>One piece of music: its clip and the gain that brings it to the shared loudness.</summary>
    [Serializable]
    public struct MusicCue
    {
        public MusicTrack track;
        public AudioClip clip;
        [Tooltip("Multiplies this clip's volume so every track plays about as loud as the others.")]
        [Range(0f, 2f)] public float gain;

        public MusicCue(MusicTrack track, AudioClip clip, float gain)
        {
            this.track = track;
            this.clip = clip;
            this.gain = gain;
        }
    }

    /// <summary>
    /// Plays the music to fit where the run is: the title track on the title screen, each
    /// room's own track while its chapter is played, and a short sting on the Results screen
    /// for a won or a lost run, then the title track again. Tracks crossfade; the music dips
    /// under a cutscene's dialogue and while paused.
    /// </summary>
    /// <remarks>
    /// <para>The tracks are recorded loops by other composers (`Assets/_ThirdParty/Music`,
    /// credited in its README and on the title screen), set as <see cref="MusicCue"/>s on the
    /// director in the Agents scene. Each cue's gain was measured so every track plays at about
    /// the same loudness (RMS 0.12 at a gain of 1).</para>
    /// <para>It listens to the same signals the cutscene director does and reads the game
    /// clock's state; it never changes either. The chapter, and so the room, comes from
    /// <see cref="ChapterEvents.OnChapterStarted"/>, the factory's shutdown (a won run) from
    /// <see cref="CutsceneEvents.OnCriticalSignal"/>. A chapter's cutscene keeps the room's
    /// track until the next chapter starts. Going back to the title starts a new run.</para>
    /// <para>The play volume of 0.22 puts the music at about 0.026 RMS, about level with the
    /// sound effects' peaks (0.03 to 0.07): present, and the pause menu's music slider
    /// (<see cref="GameAudio.MusicVolume"/>) turns it down. The title and results screens,
    /// where nothing else plays, get 1.5 times that. The M key mutes through
    /// <see cref="AudioListener.volume"/>, which the music follows. The sources ignore the
    /// listener's pause so the music keeps playing, quieter, in the pause menu. Fades use
    /// unscaled time.</para>
    /// </remarks>
    public sealed class MusicDirector : MonoBehaviour
    {
        /// <summary>The default music volume during play.</summary>
        public const float PlayVolume = 0.22f;

        [Tooltip("The clip and loudness gain of every track.")]
        [SerializeField] MusicCue[] cues = Array.Empty<MusicCue>();

        [Tooltip("Music volume during play, before the pause menu's music slider.")]
        [SerializeField, Range(0f, 1f)] float volume = PlayVolume;
        [Tooltip("Share of the volume kept while a cutscene plays, so the dialogue reads.")]
        [SerializeField, Range(0f, 1f)] float cutsceneShare = 0.5f;
        [Tooltip("Share of the volume kept while paused.")]
        [SerializeField, Range(0f, 1f)] float pausedShare = 0.5f;
        [Tooltip("Multiple of the volume on the title and results screens, where nothing else plays.")]
        [SerializeField, Range(0f, 3f)] float menuShare = 1.5f;
        [Tooltip("Seconds to crossfade from one track to the next.")]
        [SerializeField, Min(0.05f)] float crossfadeSeconds = 1.5f;

        AudioSource _current, _previous;
        float _currentGain = 1f, _previousGain = 1f;
        int _chapter;
        bool _won, _jingleDone, _warnedMissing;
        float _fade = 1f;             // 0 to 1 through a crossfade
        float _fadeSeconds = 1.5f;    // this crossfade's length: short for a sting, so its start is heard
        float _level;                 // the volume, eased towards TargetVolume

        /// <summary>The track now playing (or fading in).</summary>
        public MusicTrack Playing { get; private set; } = MusicTrack.None;

        /// <summary>The volume the music is heading for now, before the listener's mute.</summary>
        public float TargetVolume { get; private set; }

        /// <summary>True for the two stings, which play once; every other track loops.</summary>
        public static bool IsJingle(MusicTrack track) => track == MusicTrack.WinJingle || track == MusicTrack.LoseJingle;

        /// <summary>
        /// Which track fits: the title on the title screen; on the Results screen the won or lost
        /// sting, then the title again; otherwise the track of the chapter's room (the Assembly
        /// Floor before the first chapter has started).
        /// </summary>
        public static MusicTrack Choose(GameState state, int chapter, bool won, bool jingleDone)
        {
            switch (state)
            {
                case GameState.Title:
                    return MusicTrack.Title;
                case GameState.Results:
                    return jingleDone ? MusicTrack.Title : won ? MusicTrack.WinJingle : MusicTrack.LoseJingle;
                default:
                    return RoomTrack(chapter);
            }
        }

        /// <summary>The track for a chapter's room.</summary>
        public static MusicTrack RoomTrack(int chapter)
        {
            switch (chapter)
            {
                case 2: return MusicTrack.PaintingRoom;
                case 3: return MusicTrack.StorageArea;
                default: return chapter >= 4 ? MusicTrack.ControlRoom : MusicTrack.AssemblyFloor;
            }
        }

        /// <summary>The volume for a state: lower under cutscenes and while paused.</summary>
        public static float VolumeFor(GameState state, float volume, float cutsceneShare, float pausedShare, float menuShare)
        {
            switch (state)
            {
                case GameState.Cutscene: return volume * cutsceneShare;
                case GameState.Paused: return volume * pausedShare;
                case GameState.Title:
                case GameState.Results: return volume * menuShare;
                default: return volume;
            }
        }

        /// <summary>The cue set for <paramref name="track"/>, if there is one with a clip.</summary>
        public bool TryGetCue(MusicTrack track, out MusicCue cue)
        {
            foreach (MusicCue c in cues)
            {
                if (c.track == track && c.clip != null)
                {
                    cue = c;
                    return true;
                }
            }
            cue = default;
            return false;
        }

        /// <summary>Replaces the cues, for tests.</summary>
        public void UseCues(params MusicCue[] replacement) => cues = replacement ?? Array.Empty<MusicCue>();

        void Awake()
        {
            _current = MakeSource();
            _previous = MakeSource();
        }

        AudioSource MakeSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = 0;
            source.ignoreListenerPause = true;
            source.volume = 0f;
            return source;
        }

        void OnEnable()
        {
            ChapterEvents.OnChapterStarted += OnChapterStarted;
            CutsceneEvents.OnCriticalSignal += OnCriticalSignal;
        }

        void OnDisable()
        {
            ChapterEvents.OnChapterStarted -= OnChapterStarted;
            CutsceneEvents.OnCriticalSignal -= OnCriticalSignal;
        }

        void OnChapterStarted(int chapter) => _chapter = chapter;

        void OnCriticalSignal(string signal)
        {
            if (signal == CutsceneSignals.FactoryShutdown)
                _won = true;
        }

        void Update()
        {
            IGameClock clock = GameClock.Current;
            if (clock == null)
                return;
            GameState state = clock.State;
            if (state == GameState.Title)
            {
                // Back at the title: a new run.
                _chapter = 0;
                _won = false;
            }
            if (state != GameState.Results)
                _jingleDone = false;
            else if (IsJingle(Playing) && _fade >= 1f && !_current.isPlaying)
                _jingleDone = true;

            MusicTrack want = Choose(state, _chapter, _won, _jingleDone);
            if (want != Playing)
                Switch(want);

            TargetVolume = VolumeFor(state, volume, cutsceneShare, pausedShare, menuShare);
            float dt = Time.unscaledDeltaTime;
            _level = Mathf.MoveTowards(_level, TargetVolume, dt * 0.6f);   // ducks over about half a second
            _fade = Mathf.MoveTowards(_fade, 1f, dt / _fadeSeconds);
            float setting = GameAudio.MusicVolume;   // the pause menu's music slider
            _current.volume = Mathf.Clamp01(_level * _fade * setting * _currentGain);
            _previous.volume = Mathf.Clamp01(_level * (1f - _fade) * setting * _previousGain);
            if (_fade >= 1f && _previous.isPlaying)
                _previous.Stop();
        }

        void Switch(MusicTrack track)
        {
            // The old track fades out on the other source while the new one fades in.
            AudioSource old = _current;
            _current = _previous;
            _previous = old;
            _previousGain = _currentGain;
            Playing = track;
            _fade = 0f;
            _fadeSeconds = IsJingle(track) ? 0.25f : crossfadeSeconds;
            _current.Stop();
            _current.volume = 0f;
            _current.loop = !IsJingle(track);

            if (TryGetCue(track, out MusicCue cue))
            {
                _current.clip = cue.clip;
                _currentGain = cue.gain;
                _current.Play();
            }
            else
            {
                _current.clip = null;
                _currentGain = 1f;
                if (track != MusicTrack.None && !_warnedMissing && cues.Length > 0)
                {
                    _warnedMissing = true;
                    Debug.LogWarning($"MusicDirector: no clip for {track}, so it is silent.", this);
                }
            }
        }
    }
}
