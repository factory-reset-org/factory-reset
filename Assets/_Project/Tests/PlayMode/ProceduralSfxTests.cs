using System;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Tests
{
    public sealed class ProceduralSfxTests
    {
        [Test]
        public void ASoundHasTheRequestedLength()
        {
            float[] samples = ProceduralSfx.Generate(SfxWave.Square, 900f, 260f, 0.12f, 0.09f, 44100);

            Assert.AreEqual(Mathf.RoundToInt(0.12f * 44100), samples.Length);
        }

        [TestCase(SfxWave.Sine)]
        [TestCase(SfxWave.Square)]
        [TestCase(SfxWave.Sawtooth)]
        [TestCase(SfxWave.Triangle)]
        public void EveryWaveStaysWithinItsGain(SfxWave wave)
        {
            float[] samples = ProceduralSfx.Generate(wave, 440f, 880f, 0.2f, 0.12f);

            float peak = 0f;
            foreach (float sample in samples)
            {
                Assert.That(float.IsNaN(sample), Is.False);
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            Assert.LessOrEqual(peak, 0.12f + 1e-4f);
            Assert.Greater(peak, 0.05f, "It is audible, not silent.");
        }

        [Test]
        public void TheVolumeDiesAwayLikeThePrototypesRamp()
        {
            float[] samples = ProceduralSfx.Generate(SfxWave.Square, 300f, 300f, 0.5f, 0.1f);

            float firstQuarter = Peak(samples, 0, samples.Length / 4);
            float lastQuarter = Peak(samples, samples.Length * 3 / 4, samples.Length);
            Assert.Greater(firstQuarter, 0.07f);
            Assert.Less(lastQuarter, firstQuarter * 0.15f);
            Assert.Less(Mathf.Abs(samples[samples.Length - 1]), 0.002f, "Ends in silence, no click.");
        }

        [Test]
        public void ThePitchFollowsTheSlide()
        {
            // A square wave crosses zero twice a cycle: more crossings in the same time means a higher pitch.
            float[] rising = ProceduralSfx.Generate(SfxWave.Square, 200f, 2000f, 0.2f, 0.1f);
            float[] steady = ProceduralSfx.Generate(SfxWave.Square, 200f, 200f, 0.2f, 0.1f);

            Assert.Greater(Crossings(rising), Crossings(steady) * 2);
        }

        [Test]
        public void TheSameRecipeGivesTheSameSound()
        {
            float[] a = ProceduralSfx.Generate(SfxWave.Sawtooth, 160f, 60f, 0.25f, 0.12f);
            float[] b = ProceduralSfx.Generate(SfxWave.Sawtooth, 160f, 60f, 0.25f, 0.12f);

            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void BadArgumentsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralSfx.Generate(SfxWave.Sine, 0f, 100f, 0.1f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralSfx.Generate(SfxWave.Sine, 100f, 100f, 0f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralSfx.Generate(SfxWave.Sine, 100f, 100f, 0.1f, 0f));
        }

        [Test]
        public void EveryGameSoundMakesAClipAndTheSameClipIsReused()
        {
            foreach (Sfx sfx in Enum.GetValues(typeof(Sfx)))
            {
                AudioClip clip = GameSfx.ClipFor(sfx);

                Assert.IsNotNull(clip, sfx.ToString());
                Assert.Greater(clip.length, 0.02f, sfx.ToString());
                Assert.AreSame(clip, GameSfx.ClipFor(sfx));
            }
        }

        static float Peak(float[] samples, int from, int to)
        {
            float peak = 0f;
            for (int i = from; i < to; i++)
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            return peak;
        }

        static int Crossings(float[] samples)
        {
            int count = 0;
            for (int i = 1; i < samples.Length; i++)
                if (Mathf.Sign(samples[i]) != Mathf.Sign(samples[i - 1]))
                    count++;
            return count;
        }
    }
}
