using System;
using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// The player's two volume settings from the pause menu: the sound (everything that is not
    /// the music: shots, hits, voices, alarms) and the music. Each runs from 0 to 1, where 1 is
    /// the level the game was mixed at. They are kept in PlayerPrefs, so they last from one run
    /// to the next. The M key's mute is separate (<see cref="AudioListener.volume"/>) and
    /// silences both.
    /// </summary>
    /// <remarks>
    /// Every place that plays a sound multiplies its own volume by one of these. Read and set
    /// them on the main thread only, as PlayerPrefs requires.
    /// </remarks>
    public static class GameAudio
    {
        const string SoundKey = "FactoryReset.SoundVolume";
        const string MusicKey = "FactoryReset.MusicVolume";

        static float? s_sound;
        static float? s_music;

        /// <summary>The sound effects' volume, 0 to 1.</summary>
        public static float SoundVolume
        {
            get => s_sound ?? (s_sound = Load(SoundKey)).Value;
            set => Set(SoundKey, ref s_sound, value);
        }

        /// <summary>The music's volume, 0 to 1.</summary>
        public static float MusicVolume
        {
            get => s_music ?? (s_music = Load(MusicKey)).Value;
            set => Set(MusicKey, ref s_music, value);
        }

        /// <summary>Raised after either volume changes.</summary>
        public static event Action Changed;

        static float Load(string key) => Mathf.Clamp01(PlayerPrefs.GetFloat(key, 1f));

        static void Set(string key, ref float? field, float value)
        {
            value = Mathf.Clamp01(value);
            if (field.HasValue && Mathf.Approximately(field.Value, value))
                return;
            field = value;
            PlayerPrefs.SetFloat(key, value);   // written to disk when the game quits
            Changed?.Invoke();
        }

        // Domain reload is off in this project: read the saved values afresh each play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOnLoad()
        {
            s_sound = null;
            s_music = null;
            Changed = null;
        }
    }
}
