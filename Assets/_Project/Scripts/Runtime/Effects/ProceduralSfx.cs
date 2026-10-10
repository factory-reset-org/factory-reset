using System;

namespace ToyFactory.Runtime.Effects
{
    /// <summary>The shape of a synthesised sound's wave.</summary>
    public enum SfxWave
    {
        Sine,
        Square,
        Sawtooth,
        Triangle
    }

    /// <summary>
    /// Makes the short sounds the prototype makes with oscillators: one wave whose pitch slides
    /// from one frequency to another (exponentially) while its volume dies away (also
    /// exponentially). No audio files are needed, and the same recipe gives the same sound.
    /// </summary>
    public static class ProceduralSfx
    {
        /// <summary>Where the volume ends: the prototype's exponential ramp target.</summary>
        public const float EndGain = 0.0008f;

        /// <summary>The samples of one sound, mono, in -1 to 1.</summary>
        public static float[] Generate(SfxWave wave, float startHz, float endHz, float seconds, float gain,
            int sampleRate = 44100)
        {
            if (startHz <= 0f) throw new ArgumentOutOfRangeException(nameof(startHz));
            if (endHz <= 0f) throw new ArgumentOutOfRangeException(nameof(endHz));
            if (seconds <= 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (gain <= EndGain) throw new ArgumentOutOfRangeException(nameof(gain));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

            int count = Math.Max(1, (int)Math.Round(seconds * sampleRate));
            var samples = new float[count];
            double phase = 0.0;
            double pitchRatio = endHz / startHz;
            double fall = EndGain / gain;

            for (int i = 0; i < count; i++)
            {
                double t = count > 1 ? i / (double)(count - 1) : 0.0;
                double hz = startHz * Math.Pow(pitchRatio, t);
                phase += hz / sampleRate;
                phase -= Math.Floor(phase);
                double amplitude = gain * Math.Pow(fall, t);
                samples[i] = (float)(Shape(wave, phase) * amplitude);
            }
            return samples;
        }

        /// <summary>One cycle of the wave at <paramref name="phase"/> (0 to 1), in -1 to 1.</summary>
        public static double Shape(SfxWave wave, double phase)
        {
            switch (wave)
            {
                case SfxWave.Square:
                    return phase < 0.5 ? 1.0 : -1.0;
                case SfxWave.Sawtooth:
                    return 2.0 * phase - 1.0;
                case SfxWave.Triangle:
                    return 4.0 * Math.Abs(phase - 0.5) - 1.0;
                default:
                    return Math.Sin(2.0 * Math.PI * phase);
            }
        }
    }
}
