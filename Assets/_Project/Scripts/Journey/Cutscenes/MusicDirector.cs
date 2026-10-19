using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Plays the music (<see cref="MusicComposer"/>) to fit where the run is: the theme on the
    /// title screen, each room's own piece while its chapter is played, and a short fanfare or
    /// a falling phrase on the Results screen, then the theme again. Tracks crossfade; the
    /// music sits under the sound effects and dips under a cutscene's dialogue and while paused.
    /// </summary>
    /// <remarks>
    /// <para>It listens to the same signals the cutscene director does and reads the game
    /// clock's state; it never changes either. The chapter, and so the room, comes from
    /// <see cref="ChapterEvents.OnChapterStarted"/>, the factory's shutdown (a won run) from
    /// <see cref="CutsceneEvents.OnCriticalSignal"/>. A chapter's cutscene keeps the room's
    /// piece until the next chapter starts. Going back to the title starts a new run.</para>
    /// <para>Every piece is composed to the same loudness (RMS 0.15), so the play volume of
    /// 0.08 puts the music at about 0.012 RMS, roughly a quarter of the peaks of the sound
    /// effects (about 0.03 to 0.07 after their master volume), so shots, hits and pickups
    /// stay on top. The title and results screens, where nothing else plays, get twice that.</para>
    /// <para>The M key mutes through <see cref="AudioListener.volume"/>, which the music follows.
    /// The sources ignore the listener's pause so the music keeps playing, quieter, in the
    /// pause menu. Fades use unscaled time.</para>
    /// <para>Composing a track takes a few hundred milliseconds, so all of them are composed
    /// once, on a background thread, when the first director wakes (title first, then the rooms
    /// in order), and kept for the rest of the session. A track change waits until its track
    /// is ready, so the game never stalls on the music.</para>
    /// </remarks>
    public sealed class MusicDirector : MonoBehaviour
    {
        /// <summary>The default music volume during play.</summary>
        public const float PlayVolume = 0.08f;

        static readonly MusicTrack[] ComposeOrder =
        {
            MusicTrack.Title, MusicTrack.AssemblyFloor, MusicTrack.PaintingRoom, MusicTrack.StorageArea,
            MusicTrack.ControlRoom, MusicTrack.WinJingle, MusicTrack.LoseJingle,
        };
        static readonly Dictionary<MusicTrack, float[]> Composed = new Dictionary<MusicTrack, float[]>();
        static Task _composing;

        [Tooltip("Music volume during play: well under the sound effects.")]
        [SerializeField, Range(0f, 1f)] float volume = PlayVolume;
        [Tooltip("Share of the volume kept while a cutscene plays, so the dialogue reads.")]
        [SerializeField, Range(0f, 1f)] float cutsceneShare = 0.5f;
        [Tooltip("Share of the volume kept while paused.")]
        [SerializeField, Range(0f, 1f)] float pausedShare = 0.5f;
        [Tooltip("Multiple of the volume on the title and results screens, where nothing else plays.")]
        [SerializeField, Range(0f, 3f)] float menuShare = 2f;
        [Tooltip("Seconds to crossfade from one track to the next.")]
        [SerializeField, Min(0.05f)] float crossfadeSeconds = 1.5f;

        readonly Dictionary<MusicTrack, AudioClip> _clips = new Dictionary<MusicTrack, AudioClip>();
        AudioSource _current, _previous;
        int _chapter;
        bool _won, _jingleDone;
        float _fade = 1f;             // 0 to 1 through a crossfade
        float _fadeSeconds = 1.5f;    // this crossfade's length: short for a jingle, so its start is heard
        float _level;                 // the volume, eased towards TargetVolume

        /// <summary>The track now playing (or fading in).</summary>
        public MusicTrack Playing { get; private set; } = MusicTrack.None;

        /// <summary>The volume the music is heading for now, before the listener's mute.</summary>
        public float TargetVolume { get; private set; }

        /// <summary>
        /// Which track fits: the title on the title screen; on the Results screen the won or lost
        /// jingle, then the title again; otherwise the piece of the chapter's room (the Assembly
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

        /// <summary>The piece for a chapter's room.</summary>
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

        void Awake()
        {
            _current = MakeSource();
            _previous = MakeSource();
            StartComposing();
        }

        // MusicComposer is pure math on arrays, so it is safe off the main thread.
        static void StartComposing()
        {
            if (_composing != null)
                return;
            _composing = Task.Run(() =>
            {
                foreach (MusicTrack track in ComposeOrder)
                {
                    float[] data;
                    try
                    {
                        data = MusicComposer.Compose(track);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        data = new float[0];
                    }
                    lock (Composed)
                        Composed[track] = data;
                }
            });
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
            else if (!MusicComposer.Loops(Playing) && Playing != MusicTrack.None && _fade >= 1f && !_current.isPlaying)
                _jingleDone = true;

            MusicTrack want = Choose(state, _chapter, _won, _jingleDone);
            if (want != Playing && TryClip(want, out AudioClip clip))
                Switch(want, clip);

            TargetVolume = VolumeFor(state, volume, cutsceneShare, pausedShare, menuShare);
            float dt = Time.unscaledDeltaTime;
            _level = Mathf.MoveTowards(_level, TargetVolume, dt * 0.6f);   // ducks over about half a second
            _fade = Mathf.MoveTowards(_fade, 1f, dt / _fadeSeconds);
            float setting = GameAudio.MusicVolume;   // the pause menu's music slider
            _current.volume = _level * _fade * setting;
            _previous.volume = _level * (1f - _fade) * setting;
            if (_fade >= 1f && _previous.isPlaying)
                _previous.Stop();
        }

        void Switch(MusicTrack track, AudioClip clip)
        {
            // The old track fades out on the other source while the new one fades in.
            AudioSource old = _current;
            _current = _previous;
            _previous = old;
            Playing = track;
            _fade = 0f;
            _fadeSeconds = MusicComposer.Loops(track) ? crossfadeSeconds : 0.25f;
            _current.Stop();
            _current.clip = clip;
            _current.loop = MusicComposer.Loops(track);
            _current.volume = 0f;
            if (_current.clip != null)
                _current.Play();
        }

        // False while the track is still being composed; a null clip means silence.
        bool TryClip(MusicTrack track, out AudioClip clip)
        {
            // A clip made at runtime can be destroyed with its scene; ask Unity, not C#.
            if (_clips.TryGetValue(track, out clip) && clip != null)
                return true;
            clip = null;
            if (track == MusicTrack.None)
                return true;
            float[] data;
            lock (Composed)
                if (!Composed.TryGetValue(track, out data))
                    return false;
            if (data.Length == 0)
                return true;
            clip = AudioClip.Create("Music_" + track, data.Length, 1, MusicComposer.SampleRate, false);
            clip.SetData(data, 0);
            _clips[track] = clip;
            return true;
        }

        void OnDestroy()
        {
            foreach (AudioClip clip in _clips.Values)
                if (clip != null)
                    Destroy(clip);
        }
    }
}
