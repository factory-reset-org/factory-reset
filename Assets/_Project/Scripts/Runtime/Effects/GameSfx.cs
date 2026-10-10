using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ToyFactory.Runtime.Effects
{
    /// <summary>The game's own short sounds, the prototype's set.</summary>
    public enum Sfx
    {
        Shot,
        Empty,
        Hit,
        Hurt,
        Pickup,
        Switch,
        Door,
        Box,
        Zap,
        Tick,
        Alarm,
        Mission,
        Ding,
        Boom
    }

    /// <summary>
    /// Plays the game's sound effects. Each is synthesised once, on first use, from the
    /// prototype's recipe (see <see cref="ProceduralSfx"/>) and then reused.
    /// </summary>
    public static class GameSfx
    {
        /// <summary>The prototype's master volume.</summary>
        public const float Master = 0.45f;

        readonly struct Recipe
        {
            public readonly SfxWave Wave;
            public readonly float StartHz, EndHz, Seconds, Gain;

            public Recipe(SfxWave wave, float startHz, float endHz, float seconds, float gain)
            {
                Wave = wave;
                StartHz = startHz;
                EndHz = endHz;
                Seconds = seconds;
                Gain = gain;
            }
        }

        // The prototype's SFX table, in order: wave, start Hz, end Hz, seconds, gain.
        static readonly Dictionary<Sfx, Recipe> Recipes = new Dictionary<Sfx, Recipe>
        {
            { Sfx.Shot, new Recipe(SfxWave.Square, 900f, 260f, 0.12f, 0.09f) },
            { Sfx.Empty, new Recipe(SfxWave.Square, 120f, 110f, 0.06f, 0.06f) },
            { Sfx.Hit, new Recipe(SfxWave.Triangle, 300f, 90f, 0.15f, 0.14f) },
            { Sfx.Hurt, new Recipe(SfxWave.Sawtooth, 160f, 60f, 0.25f, 0.12f) },
            { Sfx.Pickup, new Recipe(SfxWave.Sine, 520f, 1250f, 0.18f, 0.12f) },
            { Sfx.Switch, new Recipe(SfxWave.Square, 330f, 880f, 0.3f, 0.1f) },
            { Sfx.Door, new Recipe(SfxWave.Triangle, 140f, 90f, 0.35f, 0.1f) },
            { Sfx.Box, new Recipe(SfxWave.Sawtooth, 90f, 60f, 0.25f, 0.08f) },
            { Sfx.Zap, new Recipe(SfxWave.Sawtooth, 900f, 1400f, 0.25f, 0.07f) },
            { Sfx.Tick, new Recipe(SfxWave.Square, 2200f, 2000f, 0.03f, 0.03f) },
            { Sfx.Alarm, new Recipe(SfxWave.Sawtooth, 520f, 780f, 0.6f, 0.08f) },
            { Sfx.Mission, new Recipe(SfxWave.Sine, 660f, 1320f, 0.4f, 0.13f) },
            { Sfx.Ding, new Recipe(SfxWave.Sine, 1200f, 1800f, 0.18f, 0.1f) },
            { Sfx.Boom, new Recipe(SfxWave.Sawtooth, 220f, 40f, 0.8f, 0.16f) },
        };

        const int SampleRate = 44100;

        static readonly Dictionary<Sfx, AudioClip> Clips = new Dictionary<Sfx, AudioClip>();
        static AudioSource s_source;

        /// <summary>Plays a sound at the prototype's volume, scaled by <paramref name="volume"/>.</summary>
        public static void Play(Sfx sfx, float volume = 1f)
        {
            AudioClip clip = ClipFor(sfx);
            AudioSource source = Source();
            if (clip != null && source != null)
                source.PlayOneShot(clip, Master * volume);
        }

        /// <summary>The clip for <paramref name="sfx"/>, made on first use. For tests.</summary>
        public static AudioClip ClipFor(Sfx sfx)
        {
            if (Clips.TryGetValue(sfx, out AudioClip existing) && existing != null)
                return existing;
            if (!Recipes.TryGetValue(sfx, out Recipe recipe))
                return null;

            float[] samples = ProceduralSfx.Generate(recipe.Wave, recipe.StartHz, recipe.EndHz, recipe.Seconds,
                recipe.Gain, SampleRate);
            AudioClip clip = AudioClip.Create("Sfx_" + sfx, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            Clips[sfx] = clip;
            return clip;
        }

        static AudioSource Source()
        {
            if (s_source != null)
                return s_source;

            var go = new GameObject("GameSfx") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            s_source = go.AddComponent<AudioSource>();
            s_source.playOnAwake = false;
            s_source.spatialBlend = 0f;
            return s_source;
        }

        // Domain reload is off: the source and clips from the last play session may be gone.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Clips.Clear();
            s_source = null;
        }
    }
}
