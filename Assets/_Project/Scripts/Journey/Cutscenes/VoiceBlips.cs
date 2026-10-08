using UnityEngine;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// The little "voice" each speaker makes as their line types out, built in code so no
    /// audio files are needed: one short tone per speaker, in their waveform (Factory OS
    /// square, Pip triangle, Captain sawtooth, Unit 047 sine) and pitch, with a fast fade so
    /// it reads as a blip. Every second typed character plays one, slightly detuned each time.
    /// </summary>
    public sealed class VoiceBlips
    {
        const int SampleRate = 44100;
        const float BlipSeconds = 0.045f;

        readonly AudioSource _source;
        readonly AudioClip[] _clips = new AudioClip[4];
        int _count;

        public VoiceBlips(AudioSource source)
        {
            _source = source;
            for (int i = 0; i < _clips.Length; i++)
                _clips[i] = Build((DialogueSpeaker)i);
        }

        /// <summary>Called for each typed character; plays a blip on every second one.</summary>
        public void OnTyped(DialogueSpeaker speaker)
        {
            if (_source == null || (_count++ & 1) == 1)
                return;
            _source.pitch = Random.Range(0.94f, 1.06f);
            _source.PlayOneShot(_clips[(int)speaker], 0.35f);
        }

        /// <summary>One sample of <paramref name="wave"/> at phase <paramref name="phase"/> (0 to 1), from -1 to 1.</summary>
        public static float Sample(BlipWave wave, float phase)
        {
            phase -= Mathf.Floor(phase);
            switch (wave)
            {
                case BlipWave.Square: return phase < 0.5f ? 1f : -1f;
                case BlipWave.Triangle: return phase < 0.5f ? 4f * phase - 1f : 3f - 4f * phase;
                case BlipWave.Sawtooth: return 2f * phase - 1f;
                default: return Mathf.Sin(2f * Mathf.PI * phase);
            }
        }

        static AudioClip Build(DialogueSpeaker speaker)
        {
            int length = Mathf.RoundToInt(SampleRate * BlipSeconds);
            var data = new float[length];
            BlipWave wave = DialogueSpeakers.Wave(speaker);
            float frequency = DialogueSpeakers.Pitch(speaker);
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / SampleRate;
                float envelope = Mathf.Min(1f, i / 80f) * (1f - (float)i / length);   // quick attack, linear fade
                data[i] = Sample(wave, frequency * t) * envelope * 0.6f;
            }
            AudioClip clip = AudioClip.Create($"Blip_{speaker}", length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
